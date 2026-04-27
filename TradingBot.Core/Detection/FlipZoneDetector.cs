using TradingBot.Core.Configuration;
using TradingBot.Core.Models;

namespace TradingBot.Core.Detection;

/// <summary>
/// Detects flip zones where Support has become Resistance or Resistance has become Support.
/// Implements the 4-step detection process from Section 7 of the Master Prompt exactly.
///
/// Step 1 — Monitors active zones with score >= ZoneMinScore.
/// Step 2 — Detects a break when a candle closes at least FlipBreakMinPct (0.2%) beyond
///           the zone boundary. Creates a FlipZone with Status Broken.
/// Step 3 — Detects a return when price approaches the broken level from the opposite
///           side within FlipZoneExpiry (50) candles and within ZoneClusterPct (0.3%)
///           of the original level.
/// Step 4 — Confirms the flip: inverts the zone type, assigns score +2 base flip bonus
///           plus all applicable strength bonuses, sets Status to Flipped.
///
/// Expiry — if no return within FlipZoneExpiry candles the flip zone is marked Expired
///          and must be removed.
/// </summary>
public sealed class FlipZoneDetector
{
    private readonly BotConfig _config;

    public FlipZoneDetector(BotConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Scans all active, non-mitigated zones for breaks on the candle that just closed.
    /// Any zone not already being tracked in <paramref name="existingFlipZones"/> that
    /// has been closed through by at least FlipBreakMinPct produces a new FlipZone.
    /// Call this after <see cref="SupportResistanceDetector.UpdateZones"/> on every
    /// Structure Timeframe candle close.
    /// </summary>
    /// <param name="zones">All Support and Resistance zones currently being tracked.</param>
    /// <param name="existingFlipZones">Flip zones already in progress — prevents duplicates.</param>
    /// <param name="latestCandle">The candle that just closed.</param>
    /// <param name="currentCandleIndex">Index of the candle that just closed.</param>
    public IReadOnlyList<FlipZone> DetectBreaks(
        IReadOnlyList<SupportResistanceZone> zones,
        IReadOnlyList<FlipZone> existingFlipZones,
        Candle latestCandle,
        int currentCandleIndex)
    {
        if (zones is null) throw new ArgumentNullException(nameof(zones));
        if (existingFlipZones is null) throw new ArgumentNullException(nameof(existingFlipZones));
        if (latestCandle is null) throw new ArgumentNullException(nameof(latestCandle));

        // Build a set of zone IDs already being tracked to avoid duplicate FlipZones.
        var alreadyTracked = existingFlipZones
            .Where(f => f.Status == FlipZoneStatus.Broken)
            .Select(f => f.OriginalZoneId)
            .ToHashSet();

        var newFlipZones = new List<FlipZone>();

        foreach (var zone in zones)
        {
            // Step 1 — original level must be active with score >= ZoneMinScore.
            if (!zone.IsActive || zone.Score < _config.ZoneMinScore)
                continue;

            if (zone.Status == ZoneStatus.Mitigated)
                continue;

            if (alreadyTracked.Contains(zone.Id))
                continue;

            // Step 2 — break detection.
            if (!IsBreakConfirmed(zone, latestCandle))
                continue;

            var flipZone = CreateFlipZone(zone, latestCandle, currentCandleIndex);
            newFlipZones.Add(flipZone);
        }

        return newFlipZones;
    }

    /// <summary>
    /// Processes every Broken flip zone against the candle that just closed:
    /// tracks the maximum post-break move, checks for a qualifying return,
    /// and expires flip zones that have exceeded FlipZoneExpiry without a return.
    /// Call this after <see cref="DetectBreaks"/> on every candle close.
    /// </summary>
    /// <param name="flipZones">All flip zones currently being tracked (mutated in place).</param>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="currentCandleIndex">Index of the candle that just closed.</param>
    public void UpdateFlipZones(
        IList<FlipZone> flipZones,
        IReadOnlyList<Candle> candles,
        int currentCandleIndex)
    {
        if (flipZones is null) throw new ArgumentNullException(nameof(flipZones));
        if (candles is null) throw new ArgumentNullException(nameof(candles));

        var latestCandle   = candles[currentCandleIndex];
        var previousCandle = currentCandleIndex > 0 ? candles[currentCandleIndex - 1] : null;

        foreach (var flipZone in flipZones)
        {
            if (flipZone.Status != FlipZoneStatus.Broken)
                continue;

            int candlesSinceBreak = currentCandleIndex - flipZone.BreakCandleIndex;

            // ── Expiry check (Section 7 Expiry) ──────────────────────────────
            if (candlesSinceBreak > _config.FlipZoneExpiry)
            {
                flipZone.Status   = FlipZoneStatus.Expired;
                flipZone.IsActive = false;
                continue;
            }

            // ── Track maximum post-break move ─────────────────────────────────
            UpdateExtremeAfterBreak(flipZone, latestCandle);

            // ── Step 3 — Return detection ─────────────────────────────────────
            if (!IsReturnDetected(flipZone, latestCandle, previousCandle))
                continue;

            // ── Step 4 — Flip confirmed ───────────────────────────────────────
            int candlesToReturn = currentCandleIndex - flipZone.BreakCandleIndex;

            flipZone.ReturnCandleIndex  = currentCandleIndex;
            flipZone.ReturnConfirmedAt  = latestCandle.OpenTime;
            flipZone.CandlesToReturn    = candlesToReturn;
            flipZone.Status             = FlipZoneStatus.Flipped;
            flipZone.Score              = CalculateFlipScore(flipZone);
            flipZone.IsActive           = true;
        }
    }

    // ── Break detection ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the candle closed at least FlipBreakMinPct (0.2%) beyond
    /// the zone boundary in the direction that constitutes a break (Section 7 Step 2).
    ///
    /// Support break:    candle closes below zone Bottom by >= FlipBreakMinPct.
    /// Resistance break: candle closes above zone Top    by >= FlipBreakMinPct.
    /// </summary>
    private bool IsBreakConfirmed(SupportResistanceZone zone, Candle candle)
    {
        if (zone.ZoneType == ZoneType.Support)
        {
            if (candle.Close >= zone.Bottom) return false;
            decimal breakPct = (zone.Bottom - candle.Close) / zone.Bottom * 100m;
            return breakPct >= (decimal)_config.FlipBreakMinPct;
        }
        else
        {
            if (candle.Close <= zone.Top) return false;
            decimal breakPct = (candle.Close - zone.Top) / zone.Top * 100m;
            return breakPct >= (decimal)_config.FlipBreakMinPct;
        }
    }

    // ── Return detection ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when price returns to the broken level from the opposite side of the
    /// break and the close is within ZoneClusterPct (0.3%) of the original zone boundary
    /// (Section 7 Step 3).
    ///
    /// Broken Support (now Resistance) — return from below:
    ///   Previous close was below zone Bottom.
    ///   Current candle High reaches zone Bottom.
    ///   Current close is within 0.3% of zone Bottom.
    ///
    /// Broken Resistance (now Support) — return from above:
    ///   Previous close was above zone Top.
    ///   Current candle Low reaches zone Top.
    ///   Current close is within 0.3% of zone Top.
    /// </summary>
    private bool IsReturnDetected(FlipZone flipZone, Candle candle, Candle? previousCandle)
    {
        if (previousCandle is null) return false;

        decimal proximityPct = (decimal)_config.ZoneClusterPct;

        if (flipZone.OriginalZoneType == ZoneType.Support)
        {
            // Support was broken downward — zone now acts as Resistance.
            // Return = price comes back up from below and closes near original Bottom.
            bool wasBelow       = previousCandle.Close < flipZone.Bottom;
            bool touchedFromBelow = candle.High >= flipZone.Bottom;
            decimal distancePct = Math.Abs(candle.Close - flipZone.Bottom)
                                  / flipZone.Bottom * 100m;
            bool closeNearLevel = distancePct <= proximityPct;

            return wasBelow && touchedFromBelow && closeNearLevel;
        }
        else
        {
            // Resistance was broken upward — zone now acts as Support.
            // Return = price comes back down from above and closes near original Top.
            bool wasAbove       = previousCandle.Close > flipZone.Top;
            bool touchedFromAbove = candle.Low <= flipZone.Top;
            decimal distancePct = Math.Abs(candle.Close - flipZone.Top)
                                  / flipZone.Top * 100m;
            bool closeNearLevel = distancePct <= proximityPct;

            return wasAbove && touchedFromAbove && closeNearLevel;
        }
    }

    // ── Post-break extreme tracking ───────────────────────────────────────────

    /// <summary>
    /// Updates the maximum post-break move for a Broken flip zone.
    /// Tracks the lowest Low after a Support break and the highest High after a
    /// Resistance break, then recomputes MaxMoveAfterBreakPct from BreakCandleClose.
    /// </summary>
    private static void UpdateExtremeAfterBreak(FlipZone flipZone, Candle candle)
    {
        if (flipZone.OriginalZoneType == ZoneType.Support)
        {
            // Price broke downward — track how far it fell.
            if (candle.Low < flipZone.ExtremeAfterBreak)
            {
                flipZone.ExtremeAfterBreak   = candle.Low;
                flipZone.MaxMoveAfterBreakPct =
                    (flipZone.BreakCandleClose - candle.Low)
                    / flipZone.BreakCandleClose * 100m;
            }
        }
        else
        {
            // Price broke upward — track how far it rose.
            if (candle.High > flipZone.ExtremeAfterBreak)
            {
                flipZone.ExtremeAfterBreak   = candle.High;
                flipZone.MaxMoveAfterBreakPct =
                    (candle.High - flipZone.BreakCandleClose)
                    / flipZone.BreakCandleClose * 100m;
            }
        }
    }

    // ── Flip score calculation ────────────────────────────────────────────────

    /// <summary>
    /// Calculates the flip zone's confluence score when the flip is confirmed (Section 7).
    ///
    /// Base flip bonus:              +2
    /// Original touches greater than 4: +1
    /// Move after break greater than 5%:  +1
    /// Move after break greater than 10%: +2 (replaces the +1)
    /// Move after break greater than 20%: +3 (replaces the +2)
    /// Return within 5 candles:      +1
    /// Return at or after 50 candles: -1
    /// </summary>
    private static int CalculateFlipScore(FlipZone flipZone)
    {
        int score = 2; // base flip bonus (Section 7 Step 4)

        // Original touches bonus.
        if (flipZone.OriginalTouchCount > 4)
            score += 1;

        // Move after break bonus — only the highest bracket applies.
        if (flipZone.MaxMoveAfterBreakPct > 20m)
            score += 3;
        else if (flipZone.MaxMoveAfterBreakPct > 10m)
            score += 2;
        else if (flipZone.MaxMoveAfterBreakPct > 5m)
            score += 1;

        // Return speed bonus / penalty.
        if (flipZone.CandlesToReturn <= 5)
            score += 1;
        else if (flipZone.CandlesToReturn >= 50)
            score -= 1;

        return score;
    }

    // ── Factory ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new FlipZone in the Broken state from the zone that was just broken.
    /// ExtremeAfterBreak is initialised to BreakCandleClose so the first candle
    /// after the break correctly extends the extreme.
    /// </summary>
    private static FlipZone CreateFlipZone(
        SupportResistanceZone zone,
        Candle breakCandle,
        int currentCandleIndex)
    {
        return new FlipZone
        {
            OriginalZoneId    = zone.Id,
            OriginalZoneType  = zone.ZoneType,
            FlippedZoneType   = zone.ZoneType == ZoneType.Support
                                    ? ZoneType.Resistance
                                    : ZoneType.Support,
            OriginalTouchCount = zone.TouchCount,
            Top                = zone.Top,
            Bottom             = zone.Bottom,
            BreakCandleClose   = breakCandle.Close,
            BreakCandleIndex   = currentCandleIndex,
            BreakTime          = breakCandle.OpenTime,
            ExtremeAfterBreak  = breakCandle.Close,
            Status             = FlipZoneStatus.Broken,
            IsActive           = false
        };
    }
}

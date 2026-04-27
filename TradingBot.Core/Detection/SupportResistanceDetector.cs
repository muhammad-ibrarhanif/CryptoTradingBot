using TradingBot.Core.Configuration;
using TradingBot.Core.Models;

namespace TradingBot.Core.Detection;

/// <summary>
/// Builds Support and Resistance zones from confirmed swing points and maintains
/// their scoring, validity status, and activation state on every Structure Timeframe
/// candle close. Implements Section 6 of the Master Prompt exactly.
///
/// Zone construction rules:
///   Support Zone  — sourced from confirmed swing low:
///     Bottom = swing low candle Low
///     Top    = max(Open, Close) of that candle
///
///   Resistance Zone — sourced from confirmed swing high:
///     Bottom = min(Open, Close) of that candle
///     Top    = swing high candle High
///
/// Score components (all added together):
///   Touch count  — 2 = +1, 3 = +2, 4+ = +3
///   Timeframe    — 30m = +1, 1H = +2, 4H = +3, 1D = +4
///   Price reaction — Small = +1, Strong = +2, Explosive = +3
///   Recency       — &lt;20 candles = +3, 20-50 = +2, 50-100 = +1, &gt;100 = +0
///   Round number  — within 0.5% of significant level = +2
///
/// Activation  — score >= ZoneMinScore (default 5)
/// Deactivation — closed through OR score &lt; 3 OR &gt; 200 candles no touch
/// </summary>
public sealed class SupportResistanceDetector
{
    private readonly BotConfig _config;

    public SupportResistanceDetector(BotConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Creates one zone per confirmed swing point and scores each zone relative
    /// to <paramref name="currentCandleIndex"/>. Call this once per scan
    /// initialisation or when new swing points are detected.
    /// </summary>
    /// <param name="candles">Full candle series for the Structure Timeframe, oldest first.</param>
    /// <param name="confirmedSwingPoints">All confirmed swing points to convert into zones.</param>
    /// <param name="currentCandleIndex">Index of the most recently closed candle.</param>
    public IReadOnlyList<SupportResistanceZone> BuildZones(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<SwingPoint> confirmedSwingPoints,
        int currentCandleIndex)
    {
        if (candles is null) throw new ArgumentNullException(nameof(candles));
        if (confirmedSwingPoints is null) throw new ArgumentNullException(nameof(confirmedSwingPoints));

        var zones = new List<SupportResistanceZone>();

        foreach (var swingPoint in confirmedSwingPoints.Where(sp => sp.IsConfirmed))
        {
            if (swingPoint.CandleIndex < 0 || swingPoint.CandleIndex >= candles.Count)
                continue;

            var zone = BuildZone(candles[swingPoint.CandleIndex], swingPoint);
            zone.Score    = CalculateScore(zone, currentCandleIndex);
            zone.IsActive = zone.Score >= _config.ZoneMinScore;
            zones.Add(zone);
        }

        return zones;
    }

    /// <summary>
    /// Processes the candle at <paramref name="currentCandleIndex"/> against every
    /// non-mitigated zone: detects touches, full mitigation, updates scores, and
    /// applies the activation / deactivation rules from Section 6.
    /// Call this on every Structure Timeframe candle close.
    /// </summary>
    /// <param name="zones">All zones to evaluate (the list is mutated in place).</param>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="currentCandleIndex">Index of the candle that just closed.</param>
    public void UpdateZones(
        IList<SupportResistanceZone> zones,
        IReadOnlyList<Candle> candles,
        int currentCandleIndex)
    {
        if (zones is null) throw new ArgumentNullException(nameof(zones));
        if (candles is null) throw new ArgumentNullException(nameof(candles));

        var latestCandle   = candles[currentCandleIndex];
        var previousCandle = currentCandleIndex > 0 ? candles[currentCandleIndex - 1] : null;

        foreach (var zone in zones)
        {
            if (zone.Status == ZoneStatus.Mitigated)
                continue;

            // ── Full mitigation check (highest priority) ──────────────────────
            if (IsFullyMitigated(zone, latestCandle))
            {
                zone.Status   = ZoneStatus.Mitigated;
                zone.IsActive = false;
                continue;
            }

            // ── Touch detection ───────────────────────────────────────────────
            if (IsTouched(zone, latestCandle, previousCandle))
            {
                zone.TouchCount++;
                zone.LastTouchedAt         = latestCandle.OpenTime;
                zone.LastTouchedCandleIndex = currentCandleIndex;

                if (zone.Status == ZoneStatus.Fresh)
                    zone.Status = ZoneStatus.Tested;

                var reaction = MeasureReaction(zone, latestCandle);
                if (reaction > zone.StrongestReaction)
                    zone.StrongestReaction = reaction;
            }

            // ── Recalculate score ─────────────────────────────────────────────
            zone.Score = CalculateScore(zone, currentCandleIndex);

            // ── Deactivation rules ────────────────────────────────────────────
            int candlesSinceLastTouch = currentCandleIndex - zone.LastTouchedCandleIndex;

            if (zone.Score < 3 || candlesSinceLastTouch > 200)
            {
                zone.IsActive = false;
                continue;
            }

            zone.IsActive = zone.Score >= _config.ZoneMinScore;
        }
    }

    /// <summary>
    /// Returns only the active, non-mitigated zones that price is currently approaching.
    /// Approaching means within <see cref="BotConfig.ZoneProximityPct"/> percent
    /// AND the candle close is moving toward the zone (Section 6 Proximity Filter).
    /// </summary>
    /// <param name="zones">All known zones.</param>
    /// <param name="latestCandle">The most recently closed candle.</param>
    /// <param name="previousCandle">The candle before the latest, for direction detection.</param>
    public IReadOnlyList<SupportResistanceZone> GetApproachingZones(
        IReadOnlyList<SupportResistanceZone> zones,
        Candle latestCandle,
        Candle? previousCandle)
    {
        if (zones is null) throw new ArgumentNullException(nameof(zones));
        if (latestCandle is null) throw new ArgumentNullException(nameof(latestCandle));

        return zones
            .Where(z => z.IsActive && z.Status != ZoneStatus.Mitigated)
            .Where(z => IsApproaching(z, latestCandle, previousCandle))
            .ToList();
    }

    // ── Zone construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a zone from the candle at the swing point's index using the
    /// boundary rules from Section 6. The formation touch sets TouchCount = 1.
    /// </summary>
    private static SupportResistanceZone BuildZone(Candle swingCandle, SwingPoint swingPoint)
    {
        decimal top, bottom;

        if (swingPoint.Type == SwingPointType.Low)
        {
            // Support Zone: Bottom = candle Low, Top = max(Open, Close)
            bottom = swingCandle.Low;
            top    = Math.Max(swingCandle.Open, swingCandle.Close);
        }
        else
        {
            // Resistance Zone: Bottom = min(Open, Close), Top = candle High
            bottom = Math.Min(swingCandle.Open, swingCandle.Close);
            top    = swingCandle.High;
        }

        return new SupportResistanceZone
        {
            ZoneType              = swingPoint.Type == SwingPointType.Low ? ZoneType.Support : ZoneType.Resistance,
            SourceSwingPoint      = swingPoint,
            Top                   = top,
            Bottom                = bottom,
            Status                = ZoneStatus.Fresh,
            TouchCount            = 1,
            LastTouchedAt         = swingCandle.OpenTime,
            LastTouchedCandleIndex = swingPoint.CandleIndex
        };
    }

    // ── Scoring ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates the full confluence score for a zone at <paramref name="currentCandleIndex"/>.
    /// All five Section 6 components are summed.
    /// </summary>
    private int CalculateScore(SupportResistanceZone zone, int currentCandleIndex)
    {
        int score = 0;
        score += TimeframeScore(zone.SourceSwingPoint.Timeframe);
        score += TouchCountScore(zone.TouchCount);
        score += ReactionScore(zone.StrongestReaction);
        score += RecencyScore(zone.SourceSwingPoint.CandleIndex, currentCandleIndex);
        if (IsRoundNumber(zone.Midpoint)) score += 2;
        return score;
    }

    /// <summary>
    /// Maps a timeframe string to its score contribution (Section 6 Timeframe scoring).
    /// </summary>
    private static int TimeframeScore(string timeframe) => timeframe switch
    {
        "1D"  => 4,
        "4H"  => 3,
        "1H"  => 2,
        "30m" => 1,
        _     => 0
    };

    /// <summary>
    /// Score contribution from how many times price has touched the zone (Section 6).
    /// </summary>
    private static int TouchCountScore(int touchCount) => touchCount switch
    {
        >= 4 => 3,
        3    => 2,
        2    => 1,
        _    => 0
    };

    /// <summary>
    /// Score contribution from the strongest price reaction observed (Section 6).
    /// </summary>
    private static int ReactionScore(PriceReaction reaction) => reaction switch
    {
        PriceReaction.Explosive => 3,
        PriceReaction.Strong    => 2,
        PriceReaction.Small     => 1,
        _                       => 0
    };

    /// <summary>
    /// Score contribution from how recently the swing point formed (Section 6 Recency).
    /// Uses the swing point's candle index, not the last touch, so older levels
    /// naturally lose recency credit even if recently touched.
    /// </summary>
    private static int RecencyScore(int originCandleIndex, int currentCandleIndex)
    {
        int candlesAgo = currentCandleIndex - originCandleIndex;

        return candlesAgo switch
        {
            < 20   => 3,
            < 50   => 2,
            <= 100 => 1,
            _      => 0
        };
    }

    /// <summary>
    /// Returns true when <paramref name="price"/> is within 0.5% of a psychologically
    /// significant round level. Checks multiples at the current price magnitude and
    /// one magnitude below, including their halves (e.g. $100, $50, $10, $5 for SOL).
    /// </summary>
    private static bool IsRoundNumber(decimal price)
    {
        if (price <= 0) return false;

        double p         = (double)price;
        int    magnitude = (int)Math.Floor(Math.Log10(p));

        decimal bigStep   = (decimal)Math.Pow(10, magnitude);
        decimal smallStep = (decimal)Math.Pow(10, magnitude - 1);

        // Four step levels catch $100, $50, $10, $5 patterns
        decimal[] steps = [bigStep, bigStep / 2m, smallStep, smallStep / 2m];

        foreach (decimal step in steps)
        {
            if (step <= 0) continue;
            decimal nearest    = Math.Round(price / step, MidpointRounding.AwayFromZero) * step;
            if (nearest <= 0) continue;
            decimal distancePct = Math.Abs(price - nearest) / nearest * 100m;
            if (distancePct <= 0.5m) return true;
        }

        return false;
    }

    // ── Touch and mitigation detection ────────────────────────────────────────

    /// <summary>
    /// Returns true when the candle body enters the zone from the correct approach
    /// direction and price holds (does not close through the far boundary).
    /// Requires the previous candle to have closed outside the zone to prevent
    /// counting every consolidation candle as a new touch.
    ///
    /// Support:    Low enters zone top (Low &lt;= Top), previous close was above zone.
    /// Resistance: High enters zone bottom (High &gt;= Bottom), previous close was below zone.
    /// </summary>
    private static bool IsTouched(
        SupportResistanceZone zone,
        Candle candle,
        Candle? previousCandle)
    {
        if (zone.ZoneType == ZoneType.Support)
        {
            bool approachingFromAbove = previousCandle is null || previousCandle.Close >= zone.Top;
            bool enteredZone          = candle.Low <= zone.Top;
            bool heldSupport          = candle.Close >= zone.Bottom;
            return approachingFromAbove && enteredZone && heldSupport;
        }
        else
        {
            bool approachingFromBelow = previousCandle is null || previousCandle.Close <= zone.Bottom;
            bool enteredZone          = candle.High >= zone.Bottom;
            bool heldResistance       = candle.Close <= zone.Top;
            return approachingFromBelow && enteredZone && heldResistance;
        }
    }

    /// <summary>
    /// Returns true when price closes fully through the zone boundary,
    /// making the zone invalid and requiring removal (Section 6 Level Validity).
    ///
    /// Support mitigated:    candle closes below zone Bottom.
    /// Resistance mitigated: candle closes above zone Top.
    /// </summary>
    private static bool IsFullyMitigated(SupportResistanceZone zone, Candle candle) =>
        zone.ZoneType == ZoneType.Support
            ? candle.Close < zone.Bottom
            : candle.Close > zone.Top;

    /// <summary>
    /// Measures the price bounce off the zone on the touch candle.
    /// Support:    reaction = how far Close moved above zone Bottom.
    /// Resistance: reaction = how far Close moved below zone Top.
    /// Thresholds: &lt;0.5% = Small, 0.5-2% = Strong, &gt;2% = Explosive.
    /// </summary>
    private static PriceReaction MeasureReaction(SupportResistanceZone zone, Candle candle)
    {
        decimal reactionPct;

        if (zone.ZoneType == ZoneType.Support)
        {
            if (zone.Bottom <= 0) return PriceReaction.None;
            reactionPct = (candle.Close - zone.Bottom) / zone.Bottom * 100m;
        }
        else
        {
            if (zone.Top <= 0) return PriceReaction.None;
            reactionPct = (zone.Top - candle.Close) / zone.Top * 100m;
        }

        if (reactionPct <= 0)   return PriceReaction.None;
        if (reactionPct > 2m)   return PriceReaction.Explosive;
        if (reactionPct >= 0.5m) return PriceReaction.Strong;
        return PriceReaction.Small;
    }

    // ── Proximity filter ──────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when price is within <see cref="BotConfig.ZoneProximityPct"/> percent
    /// of the zone midpoint AND the close is moving toward the zone (Section 6).
    ///
    /// Support:    price must be falling (latest close &lt; previous close).
    /// Resistance: price must be rising  (latest close &gt; previous close).
    /// </summary>
    private bool IsApproaching(
        SupportResistanceZone zone,
        Candle latestCandle,
        Candle? previousCandle)
    {
        if (zone.Midpoint <= 0) return false;

        decimal distancePct = Math.Abs(latestCandle.Close - zone.Midpoint)
                              / zone.Midpoint * 100m;

        if (distancePct > (decimal)_config.ZoneProximityPct) return false;
        if (previousCandle is null) return true;

        return zone.ZoneType == ZoneType.Support
            ? latestCandle.Close < previousCandle.Close   // falling toward support
            : latestCandle.Close > previousCandle.Close;  // rising toward resistance
    }
}

using TradingBot.Core.Configuration;
using TradingBot.Core.Models;

namespace TradingBot.Core.Detection;

/// <summary>
/// Manages the full lifecycle of unified Zone objects per Section 8 and Section 14.
///
/// Responsibilities:
///   BuildZones    — converts SupportResistanceZones and confirmed FlipZones into
///                   unified Zone objects, merging sources within ZoneClusterPct (0.3%)
///                   of each other and adding scores from all contributing timeframes.
///
///   UpdateZones   — processes each new Structure Timeframe candle close:
///                     Full mitigation   — candle closes through zone → remove (Section 14).
///                     Partial mitigation — body enters, closes back → score −1;
///                                         two partials → Mitigated (Section 14).
///                     Wick              — only extreme touches → score unchanged (Section 14).
///                     Touch / Tested    — body enters and holds → status Tested, touch count +1.
///
///   GetTopNearestZones — returns the top N active zones sorted by distance from current
///                        price, with Fresh zones ranked above Tested at equal distance
///                        (Section 8 Bot Rules: top 10 nearest, prefer fresh).
/// </summary>
public sealed class ZoneTracker
{
    private readonly BotConfig _config;

    public ZoneTracker(BotConfig config)
    {
        _config = config;
    }

    // ── Zone construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts SupportResistanceZones and confirmed FlipZones into unified Zone objects,
    /// merging any two same-direction zones whose midpoints are within ZoneClusterPct.
    /// Scores each resulting Zone using Section 8 rules.
    /// </summary>
    /// <param name="srZones">
    /// Active Support and Resistance zones from SupportResistanceDetector.
    /// </param>
    /// <param name="flipZones">
    /// All FlipZones in the Flipped state from FlipZoneDetector.
    /// </param>
    public IList<Zone> BuildZones(
        IReadOnlyList<SupportResistanceZone> srZones,
        IReadOnlyList<FlipZone> flipZones)
    {
        if (srZones is null)  throw new ArgumentNullException(nameof(srZones));
        if (flipZones is null) throw new ArgumentNullException(nameof(flipZones));

        // Normalise all inputs into a common intermediate form.
        var sources = new List<NormalizedSource>();

        foreach (var z in srZones.Where(z => z.IsActive && z.Status != ZoneStatus.Mitigated))
        {
            sources.Add(new NormalizedSource(
                Id:           z.Id,
                IsFlip:       false,
                EffectiveType: z.ZoneType == ZoneType.Support ? ZoneCategory.Demand : ZoneCategory.Supply,
                Top:          z.Top,
                Bottom:       z.Bottom,
                Timeframe:    z.SourceSwingPoint.Timeframe,
                Status:       z.Status,
                TouchCount:   z.TouchCount,
                Reaction:     z.StrongestReaction
            ));
        }

        foreach (var f in flipZones.Where(f => f.Status == FlipZoneStatus.Flipped && f.IsActive))
        {
            sources.Add(new NormalizedSource(
                Id:           f.Id,
                IsFlip:       true,
                EffectiveType: ZoneCategory.Flip,
                Top:          f.Top,
                Bottom:       f.Bottom,
                Timeframe:    f.OriginalZoneType == ZoneType.Support
                                  ? "1H"   // flip zones inherit the timeframe of their origin
                                  : "1H",
                Status:       ZoneStatus.Tested, // a flip has already been touched
                TouchCount:   1,
                Reaction:     PriceReaction.None
            ));
        }

        // Cluster same-category sources by midpoint proximity and build Zones.
        var zones = new List<Zone>();

        foreach (var category in new[] { ZoneCategory.Demand, ZoneCategory.Supply, ZoneCategory.Flip })
        {
            var categoryGroup = sources
                .Where(s => s.EffectiveType == category)
                .OrderBy(s => s.Midpoint)
                .ToList();

            foreach (var cluster in ClusterByProximity(categoryGroup))
            {
                var zone = BuildZoneFromCluster(cluster, category);
                zones.Add(zone);
            }
        }

        return zones;
    }

    /// <summary>
    /// Processes the candle at <paramref name="currentCandleIndex"/> against every
    /// non-mitigated zone, applying Section 14 mitigation rules and updating
    /// Section 8 scores. Call on every Structure Timeframe candle close.
    /// </summary>
    /// <param name="zones">All tracked zones (mutated in place).</param>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="currentCandleIndex">Index of the candle that just closed.</param>
    public void UpdateZones(
        IList<Zone> zones,
        IReadOnlyList<Candle> candles,
        int currentCandleIndex)
    {
        if (zones is null)  throw new ArgumentNullException(nameof(zones));
        if (candles is null) throw new ArgumentNullException(nameof(candles));

        var candle   = candles[currentCandleIndex];
        var previous = currentCandleIndex > 0 ? candles[currentCandleIndex - 1] : null;

        foreach (var zone in zones)
        {
            if (zone.Status == ZoneStatus.Mitigated) continue;

            var interaction = ClassifyInteraction(zone, candle, previous);

            switch (interaction)
            {
                case InteractionType.FullMitigation:
                    ApplyFullMitigation(zone, candle, currentCandleIndex);
                    break;

                case InteractionType.PartialMitigation:
                    ApplyPartialMitigation(zone, candle, currentCandleIndex);
                    break;

                case InteractionType.Wick:
                    LogWick(zone, candle, currentCandleIndex);
                    break;

                case InteractionType.Touch:
                    ApplyTouch(zone, candle);
                    break;

                case InteractionType.None:
                    break;
            }

            // Recalculate score after any interaction.
            zone.Score    = CalculateSection8Score(zone);
            zone.IsActive = zone.Status != ZoneStatus.Mitigated
                         && zone.Score   >= _config.ZoneMinScore;
        }
    }

    /// <summary>
    /// Returns the top <paramref name="count"/> active zones sorted by distance from
    /// <paramref name="currentPrice"/>, with Fresh zones ranked above Tested at equal
    /// distance (Section 8: prefer fresh over tested, track top 10 nearest).
    /// </summary>
    public IReadOnlyList<Zone> GetTopNearestZones(
        IReadOnlyList<Zone> zones,
        decimal currentPrice,
        int count = 10)
    {
        if (zones is null) throw new ArgumentNullException(nameof(zones));

        return zones
            .Where(z => z.IsActive && z.Status != ZoneStatus.Mitigated)
            .OrderBy(z => Math.Abs(currentPrice - z.Midpoint) / z.Midpoint)
            .ThenBy(z => z.Status == ZoneStatus.Fresh ? 0 : 1)  // fresh before tested
            .ThenByDescending(z => z.Score)
            .Take(count)
            .ToList();
    }

    // ── Interaction classification ────────────────────────────────────────────

    private enum InteractionType { None, FullMitigation, PartialMitigation, Wick, Touch }

    /// <summary>
    /// Classifies how the closing candle interacted with the zone boundary
    /// using the Section 14 rules. Evaluated in priority order: Full → Partial → Wick → Touch.
    ///
    /// For a Demand / Support zone (approached from above):
    ///   Full      — Close &lt; Bottom.
    ///   Partial   — min(Open,Close) &lt;= Top AND Close &gt; Top  (body entered, closed back above).
    ///   Wick      — Low &lt;= Top AND min(Open,Close) &gt; Top    (only extreme entered).
    ///   Touch     — Low &lt;= Top AND Bottom &lt;= Close &lt;= Top (entered and held).
    ///
    /// For a Supply / Resistance zone (approached from below):
    ///   Full      — Close &gt; Top.
    ///   Partial   — max(Open,Close) &gt;= Bottom AND Close &lt; Bottom (body entered, closed back below).
    ///   Wick      — High &gt;= Bottom AND max(Open,Close) &lt; Bottom (only extreme entered).
    ///   Touch     — High &gt;= Bottom AND Bottom &lt;= Close &lt;= Top (entered and held).
    ///
    /// Flip zones use Demand rules (they act as support after flip confirmation).
    /// </summary>
    private static InteractionType ClassifyInteraction(Zone zone, Candle candle, Candle? previous)
    {
        bool isDemand = zone.Category == ZoneCategory.Demand || zone.Category == ZoneCategory.Flip;

        if (isDemand)
            return ClassifyDemandInteraction(zone, candle, previous);

        return ClassifySupplyInteraction(zone, candle, previous);
    }

    private static InteractionType ClassifyDemandInteraction(Zone zone, Candle c, Candle? prev)
    {
        // Full mitigation — candle closed below zone bottom.
        if (c.Close < zone.Bottom)
            return InteractionType.FullMitigation;

        // Require approach from above to avoid counting consolidation candles.
        bool approachingFromAbove = prev is null || prev.Close >= zone.Top;
        if (!approachingFromAbove) return InteractionType.None;

        decimal bodyBottom = Math.Min(c.Open, c.Close);

        // Partial — body entered zone but candle closed back above zone top.
        if (bodyBottom <= zone.Top && c.Close > zone.Top)
            return InteractionType.PartialMitigation;

        // Wick — only the low extreme entered; open-to-close body stayed above zone top.
        if (c.Low <= zone.Top && bodyBottom > zone.Top)
            return InteractionType.Wick;

        // Touch — candle entered zone and closed within it (held support).
        if (c.Low <= zone.Top && c.Close >= zone.Bottom && c.Close <= zone.Top)
            return InteractionType.Touch;

        return InteractionType.None;
    }

    private static InteractionType ClassifySupplyInteraction(Zone zone, Candle c, Candle? prev)
    {
        // Full mitigation — candle closed above zone top.
        if (c.Close > zone.Top)
            return InteractionType.FullMitigation;

        // Require approach from below to avoid counting consolidation candles.
        bool approachingFromBelow = prev is null || prev.Close <= zone.Bottom;
        if (!approachingFromBelow) return InteractionType.None;

        decimal bodyTop = Math.Max(c.Open, c.Close);

        // Partial — body entered zone but candle closed back below zone bottom.
        if (bodyTop >= zone.Bottom && c.Close < zone.Bottom)
            return InteractionType.PartialMitigation;

        // Wick — only the high extreme entered; body stayed below zone bottom.
        if (c.High >= zone.Bottom && bodyTop < zone.Bottom)
            return InteractionType.Wick;

        // Touch — candle entered zone and closed within it (held resistance).
        if (c.High >= zone.Bottom && c.Close >= zone.Bottom && c.Close <= zone.Top)
            return InteractionType.Touch;

        return InteractionType.None;
    }

    // ── Mitigation application ────────────────────────────────────────────────

    private static void ApplyFullMitigation(Zone zone, Candle candle, int candleIndex)
    {
        zone.Status   = ZoneStatus.Mitigated;
        zone.IsActive = false;
        zone.MitigationLog.Add(new MitigationEvent
        {
            ZoneId      = zone.Id,
            Type        = MitigationType.Full,
            Timestamp   = candle.OpenTime,
            CandleClose = candle.Close,
            CandleIndex = candleIndex
        });
    }

    private static void ApplyPartialMitigation(Zone zone, Candle candle, int candleIndex)
    {
        zone.PartialMitigationCount++;
        zone.MitigationLog.Add(new MitigationEvent
        {
            ZoneId           = zone.Id,
            Type             = MitigationType.Partial,
            Timestamp        = candle.OpenTime,
            CandleClose      = candle.Close,
            CandleIndex      = candleIndex,
            PartialCountAfter = zone.PartialMitigationCount
        });

        // Two partials → full mitigation (Section 14).
        if (zone.PartialMitigationCount >= 2)
        {
            zone.Status   = ZoneStatus.Mitigated;
            zone.IsActive = false;
        }
    }

    private static void LogWick(Zone zone, Candle candle, int candleIndex)
    {
        // Score unchanged — still log the event per Section 14.
        zone.MitigationLog.Add(new MitigationEvent
        {
            ZoneId      = zone.Id,
            Type        = MitigationType.Wick,
            Timestamp   = candle.OpenTime,
            CandleClose = candle.Close,
            CandleIndex = candleIndex
        });
    }

    private static void ApplyTouch(Zone zone, Candle candle)
    {
        zone.TouchCount++;

        if (zone.Status == ZoneStatus.Fresh)
            zone.Status = ZoneStatus.Tested;

        var reaction = MeasureReaction(zone, candle);
        if (reaction > zone.StrongestReaction)
            zone.StrongestReaction = reaction;
    }

    // ── Section 8 score ───────────────────────────────────────────────────────

    /// <summary>
    /// Calculates the Section 8 confluence score for a zone.
    ///   Validity:   Fresh +3, Tested +2.
    ///   Timeframe:  4H +3, 1D +4 (only these two earn bonuses; scores add for overlap).
    ///   Flip bonus: +2 when Category is Flip.
    ///   Partials:   −1 per partial mitigation recorded.
    /// </summary>
    private static int CalculateSection8Score(Zone zone)
    {
        int score = zone.Status switch
        {
            ZoneStatus.Fresh  => 3,
            ZoneStatus.Tested => 2,
            _                 => 0
        };

        // Timeframe bonuses — add for every qualifying timeframe present.
        if (zone.ContributingTimeframes.Contains("4H")) score += 3;
        if (zone.ContributingTimeframes.Contains("1D")) score += 4;

        // Flip zone bonus.
        if (zone.Category == ZoneCategory.Flip) score += 2;

        // Partial mitigation penalties.
        score -= zone.PartialMitigationCount;

        return Math.Max(score, 0);
    }

    // ── Reaction measurement ──────────────────────────────────────────────────

    private static PriceReaction MeasureReaction(Zone zone, Candle candle)
    {
        decimal pct;

        if (zone.Category == ZoneCategory.Demand || zone.Category == ZoneCategory.Flip)
        {
            if (zone.Bottom <= 0) return PriceReaction.None;
            pct = (candle.Close - zone.Bottom) / zone.Bottom * 100m;
        }
        else
        {
            if (zone.Top <= 0) return PriceReaction.None;
            pct = (zone.Top - candle.Close) / zone.Top * 100m;
        }

        if (pct <= 0)    return PriceReaction.None;
        if (pct > 2m)    return PriceReaction.Explosive;
        if (pct >= 0.5m) return PriceReaction.Strong;
        return PriceReaction.Small;
    }

    // ── Timeframe confluence clustering ──────────────────────────────────────

    /// <summary>
    /// Groups a list of same-category sources (already sorted by midpoint ascending)
    /// into clusters where every adjacent pair of midpoints is within ZoneClusterPct.
    /// Each cluster becomes one merged Zone.
    /// </summary>
    private IEnumerable<IReadOnlyList<NormalizedSource>> ClusterByProximity(
        IReadOnlyList<NormalizedSource> sorted)
    {
        if (sorted.Count == 0) yield break;

        var cluster = new List<NormalizedSource> { sorted[0] };

        for (int i = 1; i < sorted.Count; i++)
        {
            decimal clusterMid  = cluster.Average(s => s.Midpoint);
            decimal candidateMid = sorted[i].Midpoint;
            decimal distancePct  = Math.Abs(candidateMid - clusterMid) / clusterMid * 100m;

            if (distancePct <= (decimal)_config.ZoneClusterPct)
            {
                cluster.Add(sorted[i]);
            }
            else
            {
                yield return cluster;
                cluster = [sorted[i]];
            }
        }

        yield return cluster;
    }

    /// <summary>
    /// Merges one cluster of sources into a single Zone.
    /// Boundaries span the widest range across all sources.
    /// Status is the weakest (Tested) unless ALL sources are Fresh.
    /// Score is calculated from Section 8 rules using the merged properties.
    /// </summary>
    private Zone BuildZoneFromCluster(IReadOnlyList<NormalizedSource> cluster, ZoneCategory category)
    {
        bool allFresh = cluster.All(s => s.Status == ZoneStatus.Fresh);

        var zone = new Zone
        {
            Category               = category,
            Status                 = allFresh ? ZoneStatus.Fresh : ZoneStatus.Tested,
            Top                    = cluster.Max(s => s.Top),
            Bottom                 = cluster.Min(s => s.Bottom),
            SourceZoneIds          = cluster.Where(s => !s.IsFlip).Select(s => s.Id).ToList(),
            SourceFlipZoneIds      = cluster.Where(s => s.IsFlip).Select(s => s.Id).ToList(),
            ContributingTimeframes = cluster.Select(s => s.Timeframe).Distinct().ToList(),
            TouchCount             = cluster.Max(s => s.TouchCount),
            StrongestReaction      = cluster.Max(s => s.Reaction)
        };

        zone.Score    = CalculateSection8Score(zone);
        zone.IsActive = zone.Status != ZoneStatus.Mitigated
                     && zone.Score   >= _config.ZoneMinScore;

        return zone;
    }

    // ── Normalised source record ──────────────────────────────────────────────

    private sealed record NormalizedSource(
        Guid          Id,
        bool          IsFlip,
        ZoneCategory  EffectiveType,
        decimal       Top,
        decimal       Bottom,
        string        Timeframe,
        ZoneStatus    Status,
        int           TouchCount,
        PriceReaction Reaction)
    {
        public decimal Midpoint => (Top + Bottom) / 2m;
    }
}

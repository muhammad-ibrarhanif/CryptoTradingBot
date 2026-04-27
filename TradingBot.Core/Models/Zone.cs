namespace TradingBot.Core.Models;

/// <summary>
/// The Section 8 classification of a unified zone.
/// Demand and Supply map to Support and Resistance.
/// Flip is the strongest type — broken and returned.
/// </summary>
public enum ZoneCategory
{
    /// <summary>
    /// Sourced from confirmed swing lows (Support area).
    /// Bot takes Buy signals here.
    /// </summary>
    Demand,

    /// <summary>
    /// Sourced from confirmed swing highs (Resistance area).
    /// Bot takes Sell signals here (future version).
    /// </summary>
    Supply,

    /// <summary>
    /// Sourced from a confirmed flip — broken and returned from the opposite side.
    /// Strongest zone type (Section 7 + Section 8).
    /// </summary>
    Flip
}

/// <summary>
/// The unified zone entity used for all trading decisions (Section 8).
/// A Zone may represent a single Support/Resistance level or a merged confluence
/// of levels from multiple timeframes that fall within ZoneClusterPct of each other.
///
/// Validity lifecycle per Section 8 and Section 14:
///   Fresh     — never entered after the zone was established.      Score contribution +3.
///   Tested    — entered at least once and price held.              Score contribution +2.
///   Mitigated — price closed fully through the zone boundary.      Remove from active list.
///
/// Partial mitigation (Section 14): body enters and closes back → score −1,
/// two partials → Mitigated.
///
/// Wick (Section 14): only the candle extreme touched the zone → score unchanged.
/// </summary>
public sealed class Zone
{
    /// <summary>Unique identifier for this zone instance.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Demand, Supply, or Flip classification (Section 8).</summary>
    public ZoneCategory Category { get; init; }

    /// <summary>
    /// Validity status: Fresh, Tested, or Mitigated.
    /// Starts Fresh; transitions to Tested on first touch; Mitigated on full close-through.
    /// </summary>
    public ZoneStatus Status { get; set; } = ZoneStatus.Fresh;

    // ── Zone boundaries ───────────────────────────────────────────────────────

    /// <summary>
    /// Upper boundary of the zone.
    /// When multiple sources are merged this is the highest Top across all sources.
    /// </summary>
    public decimal Top { get; init; }

    /// <summary>
    /// Lower boundary of the zone.
    /// When multiple sources are merged this is the lowest Bottom across all sources.
    /// </summary>
    public decimal Bottom { get; init; }

    /// <summary>Midpoint used for distance calculations and proximity sorting.</summary>
    public decimal Midpoint => (Top + Bottom) / 2m;

    // ── Source tracking ───────────────────────────────────────────────────────

    /// <summary>
    /// Ids of all SupportResistanceZones that contributed to this zone after merging.
    /// </summary>
    public IReadOnlyList<Guid> SourceZoneIds { get; init; } = [];

    /// <summary>
    /// Ids of all FlipZones that contributed to this zone after merging.
    /// </summary>
    public IReadOnlyList<Guid> SourceFlipZoneIds { get; init; } = [];

    /// <summary>
    /// All distinct timeframe strings from contributing sources, e.g. ["1H", "4H"].
    /// Used to apply timeframe confluence bonuses in Section 8 scoring.
    /// </summary>
    public IReadOnlyList<string> ContributingTimeframes { get; init; } = [];

    // ── Dynamic state ─────────────────────────────────────────────────────────

    /// <summary>
    /// How many times price has entered this zone.
    /// Starts at 1 to account for the formation touch.
    /// </summary>
    public int TouchCount { get; set; } = 1;

    /// <summary>
    /// Number of confirmed partial mitigations (body entered, closed back on entry side).
    /// At 2 the zone is fully mitigated and removed (Section 14).
    /// </summary>
    public int PartialMitigationCount { get; set; }

    /// <summary>Strongest price reaction recorded across all touches of this zone.</summary>
    public PriceReaction StrongestReaction { get; set; } = PriceReaction.None;

    /// <summary>
    /// Section 8 confluence score.
    /// Computed from: validity status + timeframe bonuses + flip bonus − partial penalties.
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// True when Score >= ZoneMinScore and Status is not Mitigated.
    /// Only active zones appear in the trading signal pipeline.
    /// </summary>
    public bool IsActive { get; set; }

    // ── Mitigation log ────────────────────────────────────────────────────────

    /// <summary>
    /// Chronological log of every mitigation event on this zone (Section 14).
    /// Records Full, Partial, and Wick interactions with timestamp and candle close.
    /// </summary>
    public IList<MitigationEvent> MitigationLog { get; init; } = new List<MitigationEvent>();
}

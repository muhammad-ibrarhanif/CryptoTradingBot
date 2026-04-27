namespace TradingBot.Core.Models;

/// <summary>
/// Identifies whether a zone acts as a Support level or a Resistance level (Section 6).
/// </summary>
public enum ZoneType
{
    /// <summary>
    /// Sourced from a confirmed swing low.
    /// Bottom = swing low candle Low; Top = max(Open, Close) of that candle.
    /// </summary>
    Support,

    /// <summary>
    /// Sourced from a confirmed swing high.
    /// Bottom = min(Open, Close) of that candle; Top = swing high candle High.
    /// </summary>
    Resistance
}

/// <summary>
/// Tracks whether price has interacted with the zone since it was created (Section 6).
/// </summary>
public enum ZoneStatus
{
    /// <summary>Price has never entered the zone since it was established.</summary>
    Fresh,

    /// <summary>Price entered the zone at least once and held — did not close through.</summary>
    Tested,

    /// <summary>
    /// Price closed fully through the zone boundary.
    /// The zone is invalid and must be removed from the active list.
    /// </summary>
    Mitigated
}

/// <summary>
/// Strength of the price bounce observed when the zone was touched.
/// Used as one component of the zone's confluence score (Section 6).
/// </summary>
public enum PriceReaction
{
    /// <summary>No reaction observed yet — zone has not been touched.</summary>
    None,

    /// <summary>Small bounce: less than 0.5% move away from the zone.</summary>
    Small,

    /// <summary>Strong bounce: 0.5% to 2% move away from the zone.</summary>
    Strong,

    /// <summary>Explosive bounce: greater than 2% move away from the zone.</summary>
    Explosive
}

/// <summary>
/// A Support or Resistance zone sourced from a confirmed swing point on the Structure Timeframe.
/// Every confirmed swing low produces a Support zone; every confirmed swing high produces
/// a Resistance zone (Section 6).
/// </summary>
public sealed class SupportResistanceZone
{
    /// <summary>Unique identifier for this zone instance.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Whether this zone acts as Support or Resistance.</summary>
    public ZoneType ZoneType { get; init; }

    /// <summary>The confirmed swing point from which this zone was constructed.</summary>
    public SwingPoint SourceSwingPoint { get; init; } = null!;

    // ── Zone boundaries ───────────────────────────────────────────────────────

    /// <summary>
    /// Upper boundary of the zone.
    /// Support: max(Open, Close) of the swing low candle.
    /// Resistance: the swing high candle High.
    /// </summary>
    public decimal Top { get; init; }

    /// <summary>
    /// Lower boundary of the zone.
    /// Support: the swing low candle Low.
    /// Resistance: min(Open, Close) of the swing high candle.
    /// </summary>
    public decimal Bottom { get; init; }

    /// <summary>Midpoint of the zone, used for proximity distance calculations.</summary>
    public decimal Midpoint => (Top + Bottom) / 2m;

    // ── Dynamic state — updated on each candle close ──────────────────────────

    /// <summary>
    /// Current validity status.
    /// Fresh until first touch, Tested after first touch that holds,
    /// Mitigated when price closes through the zone boundary.
    /// </summary>
    public ZoneStatus Status { get; set; } = ZoneStatus.Fresh;

    /// <summary>
    /// Number of times price has entered this zone.
    /// Starts at 1 to account for the formation touch (the swing candle itself).
    /// Score contribution: 2 = +1, 3 = +2, 4+ = +3.
    /// </summary>
    public int TouchCount { get; set; } = 1;

    /// <summary>
    /// Number of partial mitigations recorded (enters and closes back on entry side).
    /// After 2 partials the zone is fully mitigated and removed (Section 14).
    /// </summary>
    public int PartialMitigationCount { get; set; }

    /// <summary>Strongest price reaction recorded across all touches of this zone.</summary>
    public PriceReaction StrongestReaction { get; set; } = PriceReaction.None;

    /// <summary>
    /// Current confluence score calculated from timeframe, touch count,
    /// price reaction, recency, and round number bonus (Section 6).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// True when the zone meets the minimum score threshold and has not been deactivated.
    /// Requires score >= ZoneMinScore (configurable, default 5).
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>Open time of the most recent candle that touched this zone.</summary>
    public DateTime? LastTouchedAt { get; set; }

    /// <summary>
    /// Candle index of the most recent touch. Used to evaluate the
    /// "older than 200 candles no touch" deactivation rule (Section 6).
    /// Initialised to the source swing point's candle index on zone creation.
    /// </summary>
    public int LastTouchedCandleIndex { get; set; }
}

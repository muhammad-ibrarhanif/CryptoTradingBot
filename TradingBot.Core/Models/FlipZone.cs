namespace TradingBot.Core.Models;

/// <summary>
/// Tracks where a flip zone is in its 4-step lifecycle (Section 7).
/// </summary>
public enum FlipZoneStatus
{
    /// <summary>
    /// Break detected — original zone was closed through by at least FlipBreakMinPct.
    /// Waiting for price to return from the opposite side within FlipZoneExpiry candles.
    /// </summary>
    Broken,

    /// <summary>
    /// Return confirmed — price came back to the broken level from the opposite side.
    /// The zone is now active as the flipped type (Support → Resistance or vice versa).
    /// Score +2 flip bonus has been applied.
    /// </summary>
    Flipped,

    /// <summary>
    /// No return was detected within FlipZoneExpiry candles after the break.
    /// Zone is invalid and must be removed.
    /// </summary>
    Expired
}

/// <summary>
/// Represents a level where Support has become Resistance or Resistance has become Support
/// following the 4-step detection process in Section 7 of the Master Prompt.
///
/// Step 1 — Original level active (score >= 5).
/// Step 2 — Break detected: closed at least FlipBreakMinPct beyond zone boundary.
/// Step 3 — Return detected: price returns from the opposite side within
///           FlipZoneExpiry candles and within ZoneClusterPct of the original level.
/// Step 4 — Flip confirmed: zone type inverted, score +2 flip bonus added.
/// </summary>
public sealed class FlipZone
{
    /// <summary>Unique identifier for this flip zone instance.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Id of the original Support or Resistance zone that was broken.</summary>
    public Guid OriginalZoneId { get; init; }

    /// <summary>Zone type of the original level before the break.</summary>
    public ZoneType OriginalZoneType { get; init; }

    /// <summary>
    /// Zone type after the flip is confirmed — the inverse of OriginalZoneType.
    /// Support → Resistance, Resistance → Support.
    /// </summary>
    public ZoneType FlippedZoneType { get; init; }

    /// <summary>
    /// Touch count of the original zone at the moment the break was detected.
    /// Used in the bonus scoring rule: original touches greater than 4 = score +1.
    /// </summary>
    public int OriginalTouchCount { get; init; }

    // ── Zone boundaries (carried from original zone, immutable) ───────────────

    /// <summary>Upper boundary of the original zone.</summary>
    public decimal Top { get; init; }

    /// <summary>Lower boundary of the original zone.</summary>
    public decimal Bottom { get; init; }

    /// <summary>Midpoint of the original zone boundaries.</summary>
    public decimal Midpoint => (Top + Bottom) / 2m;

    // ── Break info ────────────────────────────────────────────────────────────

    /// <summary>Close price of the candle that confirmed the break.</summary>
    public decimal BreakCandleClose { get; init; }

    /// <summary>Candle index at which the break was detected.</summary>
    public int BreakCandleIndex { get; init; }

    /// <summary>Open time of the candle that confirmed the break.</summary>
    public DateTime BreakTime { get; init; }

    // ── Post-break tracking (updated on each candle until return or expiry) ───

    /// <summary>
    /// Most extreme price reached after the break in the break direction.
    /// Lowest Low for a broken Support; Highest High for a broken Resistance.
    /// Initialised to BreakCandleClose on creation.
    /// </summary>
    public decimal ExtremeAfterBreak { get; set; }

    /// <summary>
    /// Maximum percentage move from BreakCandleClose to ExtremeAfterBreak.
    /// Used for flip strength bonus scoring (Section 7).
    /// </summary>
    public decimal MaxMoveAfterBreakPct { get; set; }

    // ── Return info (set when return is confirmed) ────────────────────────────

    /// <summary>Candle index at which the return was confirmed.</summary>
    public int? ReturnCandleIndex { get; set; }

    /// <summary>Open time of the candle that confirmed the return.</summary>
    public DateTime? ReturnConfirmedAt { get; set; }

    /// <summary>
    /// Number of candles between the break and the return.
    /// Used for return speed bonus: within 5 candles = +1, at or beyond 50 = -1.
    /// </summary>
    public int CandlesToReturn { get; set; }

    // ── State ─────────────────────────────────────────────────────────────────

    /// <summary>Current lifecycle status: Broken, Flipped, or Expired.</summary>
    public FlipZoneStatus Status { get; set; } = FlipZoneStatus.Broken;

    /// <summary>
    /// Confluence score assigned when the flip is confirmed.
    /// Includes the base +2 flip bonus and all applicable strength bonuses.
    /// </summary>
    public int Score { get; set; }

    /// <summary>True once the flip is confirmed and the zone is tradeable.</summary>
    public bool IsActive { get; set; }
}

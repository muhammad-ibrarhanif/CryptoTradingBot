namespace TradingBot.Core.Models;

/// <summary>
/// The three ways price can interact with a zone boundary (Section 14).
/// </summary>
public enum MitigationType
{
    /// <summary>
    /// Candle closes fully through the zone boundary.
    /// Zone is invalid and must be removed.
    /// </summary>
    Full,

    /// <summary>
    /// Candle body enters the zone then closes back on the entry side.
    /// Score −1 per occurrence. After 2 partials the zone is fully mitigated.
    /// </summary>
    Partial,

    /// <summary>
    /// Only the candle extreme (Low or High) touched the zone.
    /// The open-to-close body remained outside the zone. Score unchanged.
    /// </summary>
    Wick
}

/// <summary>
/// A timestamped record of every mitigation interaction with a zone.
/// Section 14 requires every mitigation to be logged.
/// </summary>
public sealed class MitigationEvent
{
    /// <summary>Id of the Zone that was mitigated.</summary>
    public Guid ZoneId { get; init; }

    /// <summary>Full, Partial, or Wick — determines score impact.</summary>
    public MitigationType Type { get; init; }

    /// <summary>Open time of the candle that caused the mitigation.</summary>
    public DateTime Timestamp { get; init; }

    /// <summary>Close price of the candle that caused the mitigation.</summary>
    public decimal CandleClose { get; init; }

    /// <summary>Index of the candle that caused the mitigation.</summary>
    public int CandleIndex { get; init; }

    /// <summary>
    /// Running count of Partial mitigations on the zone after this event.
    /// Only meaningful when Type is Partial.
    /// </summary>
    public int PartialCountAfter { get; init; }
}

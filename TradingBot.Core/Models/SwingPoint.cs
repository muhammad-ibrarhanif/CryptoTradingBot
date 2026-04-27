namespace TradingBot.Core.Models;

/// <summary>
/// Identifies whether a swing point is a Swing High or Swing Low (Section 3).
/// Full names used throughout — never abbreviated.
/// </summary>
public enum SwingPointType
{
    High,
    Low
}

/// <summary>
/// A confirmed swing high or swing low detected on a specific timeframe.
/// A swing is only considered confirmed once the required number of
/// confirming candles on the right side have closed (Section 3).
/// </summary>
public sealed class SwingPoint
{
    /// <summary>Zero-based index of the swing candle within the source candle list.</summary>
    public int CandleIndex { get; init; }

    /// <summary>Open time of the candle at which the swing occurred.</summary>
    public DateTime Time { get; init; }

    /// <summary>
    /// Price of the swing: High for Swing High, Low for Swing Low.
    /// </summary>
    public decimal Price { get; init; }

    /// <summary>Whether this is a Swing High or Swing Low.</summary>
    public SwingPointType Type { get; init; }

    /// <summary>
    /// Move magnitude relative to the adjacent candle.
    /// 1 = small move less than 1%,
    /// 2 = medium move 1–3%,
    /// 3 = large move greater than 3%.
    /// </summary>
    public int Strength { get; init; }

    /// <summary>
    /// The timeframe interval on which this swing was detected, e.g. "1H", "4H", "1D".
    /// Matches the configured StructureTimeframe, RegimeTimeframe, or BiasTimeframe.
    /// </summary>
    public string Timeframe { get; init; } = string.Empty;

    /// <summary>
    /// True once the required number of confirming candles on the right side have closed.
    /// A swing is never acted upon until this is true (Section 3).
    /// </summary>
    public bool IsConfirmed { get; init; }
}

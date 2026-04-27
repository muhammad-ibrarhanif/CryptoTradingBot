namespace TradingBot.Core.Models;

/// <summary>
/// The detected market trend based on the relationship between consecutive
/// swing highs and swing lows on the Structure Timeframe (Section 4).
/// Full names used throughout — never abbreviated per Section 26.
/// </summary>
public enum MarketTrend
{
    /// <summary>
    /// Each swing high is higher than the previous swing high AND
    /// each swing low is higher than the previous swing low.
    /// Bot takes Buy signals only.
    /// </summary>
    Uptrend,

    /// <summary>
    /// Each swing high is lower than the previous swing high AND
    /// each swing low is lower than the previous swing low.
    /// Bot skips all entries (Sell side reserved for future version).
    /// </summary>
    Downtrend,

    /// <summary>
    /// No clear pattern of Higher Highs + Higher Lows or Lower Highs + Lower Lows.
    /// Bot takes Buy signals only at confirmed support levels.
    /// </summary>
    Ranging
}

/// <summary>
/// The result of a market structure analysis performed on the Structure Timeframe.
/// Checked on every Structure Timeframe candle close (Section 4).
/// </summary>
public sealed class MarketStructure
{
    /// <summary>The detected trend: Uptrend, Downtrend, or Ranging.</summary>
    public MarketTrend Trend { get; init; }

    /// <summary>
    /// The last 3 confirmed swing highs used to determine the trend, oldest first.
    /// </summary>
    public IReadOnlyList<SwingPoint> LastSwingHighs { get; init; } = [];

    /// <summary>
    /// The last 3 confirmed swing lows used to determine the trend, oldest first.
    /// </summary>
    public IReadOnlyList<SwingPoint> LastSwingLows { get; init; } = [];

    /// <summary>The candle open time at which this structure analysis was performed.</summary>
    public DateTime DetectedAt { get; init; }

    /// <summary>
    /// True when the bot is permitted to take Buy entries under this structure.
    /// Uptrend → Buy only. Ranging → Buy at support only. Downtrend → false.
    /// </summary>
    public bool AllowBuy => Trend == MarketTrend.Uptrend || Trend == MarketTrend.Ranging;

    /// <summary>
    /// Always false in Phase 1. Sell entries are reserved for a future version.
    /// </summary>
    public bool AllowSell => false;

    /// <summary>
    /// True when the bot must restrict buys to confirmed support zones only.
    /// Applies in Ranging markets (Section 4).
    /// </summary>
    public bool RequireSupportConfirmation => Trend == MarketTrend.Ranging;
}

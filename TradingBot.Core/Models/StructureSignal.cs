namespace TradingBot.Core.Models;

/// <summary>
/// The three operating states of the bot with respect to market structure (Section 5).
/// </summary>
public enum BotStructureState
{
    /// <summary>
    /// Bot is taking signals normally.
    /// Trend is intact or no Change of Character is active.
    /// </summary>
    Active,

    /// <summary>
    /// A Change of Character was detected — price broke AGAINST the current trend.
    /// Bot immediately stops all new entries and waits for either:
    ///   a) Break of Structure in the new direction within 10 structure candles → Reversed.
    ///   b) No Break of Structure within 10 candles → resume previous trend bias (Active).
    /// </summary>
    Paused,

    /// <summary>
    /// A Break of Structure in the new direction confirmed the trend has reversed.
    /// Bot resumes taking signals in the new trend direction.
    /// Transitions to Active on the next evaluation unless a new Change of Character occurs.
    /// </summary>
    Reversed
}

/// <summary>
/// A single Break of Structure event detected on the Structure Timeframe (Section 5).
///
/// Bullish Break of Structure: candle closes above previous swing high → uptrend confirmed.
/// Bearish Break of Structure: candle closes below previous swing low → downtrend confirmed.
/// </summary>
public sealed class BreakOfStructureEvent
{
    /// <summary>True = Bullish Break of Structure (closed above swing high).</summary>
    public bool IsBullish { get; init; }

    /// <summary>The swing high or swing low price level that was broken.</summary>
    public decimal BrokenLevel { get; init; }

    /// <summary>Open time of the candle that confirmed the break.</summary>
    public DateTime Time { get; init; }

    /// <summary>Index of the candle that confirmed the break.</summary>
    public int CandleIndex { get; init; }
}

/// <summary>
/// A Change of Character event detected on the Structure Timeframe (Section 5).
/// Always occurs when price breaks AGAINST the current trend.
///
/// In Uptrend:   candle closes below the previous swing low   → Bearish Change of Character.
/// In Downtrend: candle closes above the previous swing high  → Bullish Change of Character.
/// </summary>
public sealed class ChangeOfCharacterEvent
{
    /// <summary>True = Bullish Change of Character (potential reversal from downtrend to uptrend).</summary>
    public bool IsBullish { get; init; }

    /// <summary>The swing level whose breach triggered the Change of Character.</summary>
    public decimal BrokenLevel { get; init; }

    /// <summary>Open time of the candle that triggered the Change of Character.</summary>
    public DateTime Time { get; init; }

    /// <summary>Index of the candle that triggered the Change of Character.</summary>
    public int CandleIndex { get; init; }

    /// <summary>The market trend that was active when the Change of Character was detected.</summary>
    public MarketTrend TrendAtDetection { get; init; }
}

/// <summary>
/// The complete structure state output produced on every Structure Timeframe candle close.
/// Carries all information needed to drive subsequent candle evaluations without
/// external state — the previous signal is the only required input.
/// </summary>
public sealed class StructureSignal
{
    /// <summary>Current bot operating state: Active, Paused, or Reversed.</summary>
    public BotStructureState BotState { get; init; }

    /// <summary>
    /// The trend tracked by Break of Structure / Change of Character history.
    /// Updated when a Break of Structure confirms or reverses direction.
    /// </summary>
    public MarketTrend Trend { get; init; }

    /// <summary>
    /// The Break of Structure event detected on this candle, if any.
    /// Null when no swing level was broken.
    /// </summary>
    public BreakOfStructureEvent? BreakOfStructure { get; init; }

    /// <summary>
    /// The Change of Character event detected on this candle, if any.
    /// Non-null only on the first candle that triggered the Change of Character.
    /// </summary>
    public ChangeOfCharacterEvent? ChangeOfCharacter { get; init; }

    // ── Paused-state tracking — carried forward until resolved ────────────────

    /// <summary>
    /// Candle index at which the active Change of Character was detected.
    /// Null when BotState is not Paused.
    /// </summary>
    public int? ChangeOfCharacterCandleIndex { get; init; }

    /// <summary>
    /// The trend that was active before the Change of Character put the bot into Paused.
    /// Used to resume the previous bias if the 10-candle timeout expires without a
    /// confirming Break of Structure.
    /// </summary>
    public MarketTrend? TrendBeforeChangeOfCharacter { get; init; }

    /// <summary>
    /// True when the Change of Character that caused the Paused state was bearish
    /// (uptrend broken downward). A bearish Break of Structure confirms the reversal.
    /// False when the Change of Character was bullish (downtrend broken upward). A bullish
    /// Break of Structure then confirms the reversal.
    /// </summary>
    public bool WasBearishChangeOfCharacter { get; init; }

    // ── Computed helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// True when the bot may open new entries.
    /// False only while Paused (Change of Character is unresolved).
    /// </summary>
    public bool AllowNewEntries => BotState != BotStructureState.Paused;
}

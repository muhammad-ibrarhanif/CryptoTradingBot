using TradingBot.Core.Models;

namespace TradingBot.Core.Detection;

/// <summary>
/// Detects Break of Structure and Change of Character events on every Structure
/// Timeframe candle close, and drives the bot state machine (Section 5).
///
/// Break of Structure rules (Section 5):
///   Bullish Break of Structure — candle closes ABOVE the most recent confirmed swing high.
///     Confirms the uptrend continues. Bot state → Active.
///   Bearish Break of Structure — candle closes BELOW the most recent confirmed swing low.
///     Confirms the downtrend continues. Bot state → Active.
///
/// Change of Character rules (Section 5):
///   In Uptrend   — candle closes below the most recent confirmed swing low.
///     Warns the uptrend may be reversing. Bot state → Paused.
///   In Downtrend — candle closes above the most recent confirmed swing high.
///     Warns the downtrend may be reversing. Bot state → Paused.
///
/// Resolution while Paused (Section 5):
///   Break of Structure in the new direction within 10 structure candles → Reversed.
///   No confirming Break of Structure within 10 structure candles → Active (resume prior bias).
///
/// Stateless design — the previous StructureSignal carries all required state so that
/// backtests can replay any candle range without resetting internal fields.
/// </summary>
public sealed class BreakOfStructureDetector
{
    /// <summary>
    /// Number of Structure Timeframe candles the bot waits after a Change of Character
    /// for a confirming Break of Structure before resuming the previous trend bias (Section 5).
    /// </summary>
    private const int ChangeOfCharacterTimeoutCandles = 10;

    /// <summary>
    /// Evaluates the candle at <paramref name="currentCandleIndex"/> for Break of Structure
    /// and Change of Character events, then returns the updated StructureSignal.
    ///
    /// Pass null for <paramref name="previousSignal"/> on the first candle — the bot starts
    /// in Active state with a Ranging trend.
    /// </summary>
    /// <param name="candles">Full Structure Timeframe candle series, oldest first.</param>
    /// <param name="confirmedSwingPoints">
    /// All confirmed swing points available as of <paramref name="currentCandleIndex"/>.
    /// </param>
    /// <param name="currentCandleIndex">Index of the candle that just closed.</param>
    /// <param name="previousSignal">
    /// The StructureSignal produced by the previous candle evaluation.
    /// Null on the very first call.
    /// </param>
    public StructureSignal Detect(
        IReadOnlyList<Candle> candles,
        IReadOnlyList<SwingPoint> confirmedSwingPoints,
        int currentCandleIndex,
        StructureSignal? previousSignal)
    {
        if (candles is null)               throw new ArgumentNullException(nameof(candles));
        if (confirmedSwingPoints is null)  throw new ArgumentNullException(nameof(confirmedSwingPoints));

        // Seed state from the previous signal, or use safe defaults.
        var prevState      = previousSignal?.BotState     ?? BotStructureState.Active;
        var prevTrend      = previousSignal?.Trend         ?? MarketTrend.Ranging;
        var cocCandleIndex = previousSignal?.ChangeOfCharacterCandleIndex;
        var trendBeforeCoC = previousSignal?.TrendBeforeChangeOfCharacter;
        var wasBearishCoC  = previousSignal?.WasBearishChangeOfCharacter ?? false;

        var candle = candles[currentCandleIndex];

        // Most recent confirmed swing high and swing low strictly before this candle.
        var prevSwingHigh = GetMostRecentSwingPoint(confirmedSwingPoints, SwingPointType.High,  currentCandleIndex);
        var prevSwingLow  = GetMostRecentSwingPoint(confirmedSwingPoints, SwingPointType.Low,   currentCandleIndex);

        bool bullishBreak = prevSwingHigh is not null && candle.Close > prevSwingHigh.Price;
        bool bearishBreak = prevSwingLow  is not null && candle.Close < prevSwingLow.Price;

        // ── State machine ─────────────────────────────────────────────────────

        BotStructureState newState;
        MarketTrend       newTrend      = prevTrend;
        int?              newCoCIndex   = null;
        MarketTrend?      newTrendBeforeCoC = null;
        bool              newWasBearishCoC  = false;

        BreakOfStructureEvent?  bosEvent = null;
        ChangeOfCharacterEvent? cocEvent = null;

        switch (prevState)
        {
            // ── Active and Reversed both evaluate the same way. ───────────────
            // Reversed is treated as a one-candle transitional state; the next
            // evaluation always starts fresh from Active in the new trend.
            case BotStructureState.Active:
            case BotStructureState.Reversed:
            {
                // Change of Character: price breaks AGAINST the current trend.
                bool bearishChangeOfCharacter = prevTrend == MarketTrend.Uptrend   && bearishBreak;
                bool bullishChangeOfCharacter = prevTrend == MarketTrend.Downtrend && bullishBreak;

                if (bearishChangeOfCharacter || bullishChangeOfCharacter)
                {
                    // The same break that triggers the Change of Character is NOT
                    // reported as a Break of Structure — it is the Change of Character.
                    cocEvent = new ChangeOfCharacterEvent
                    {
                        IsBullish          = bullishChangeOfCharacter,
                        BrokenLevel        = bullishChangeOfCharacter
                                                 ? prevSwingHigh!.Price
                                                 : prevSwingLow!.Price,
                        Time               = candle.OpenTime,
                        CandleIndex        = currentCandleIndex,
                        TrendAtDetection   = prevTrend
                    };

                    newState          = BotStructureState.Paused;
                    newCoCIndex       = currentCandleIndex;
                    newTrendBeforeCoC = prevTrend;
                    newWasBearishCoC  = bearishChangeOfCharacter;
                    // Trend stays as prevTrend — it has not reversed yet.
                }
                else if (bullishBreak || bearishBreak)
                {
                    // Break of Structure confirms or establishes a trend direction.
                    bosEvent = new BreakOfStructureEvent
                    {
                        IsBullish    = bullishBreak,
                        BrokenLevel  = bullishBreak ? prevSwingHigh!.Price : prevSwingLow!.Price,
                        Time         = candle.OpenTime,
                        CandleIndex  = currentCandleIndex
                    };

                    newState = BotStructureState.Active;
                    newTrend = bullishBreak ? MarketTrend.Uptrend : MarketTrend.Downtrend;
                }
                else
                {
                    newState = BotStructureState.Active;
                }

                break;
            }

            // ── Paused: waiting for confirming Break of Structure or timeout. ─
            case BotStructureState.Paused:
            {
                int candlesSinceCoC = currentCandleIndex - (cocCandleIndex ?? currentCandleIndex);

                // Timeout reached — resume the previous trend bias (Section 5).
                if (candlesSinceCoC >= ChangeOfCharacterTimeoutCandles)
                {
                    newState = BotStructureState.Active;
                    newTrend = trendBeforeCoC ?? prevTrend;
                    break;
                }

                // Check for a confirming Break of Structure in the new direction.
                // After a bearish Change of Character (uptrend broken down),
                // confirmation = bearish Break of Structure (close below swing low).
                // After a bullish Change of Character (downtrend broken up),
                // confirmation = bullish Break of Structure (close above swing high).
                bool confirmsBearishReversal = wasBearishCoC  && bearishBreak;
                bool confirmsBullishReversal = !wasBearishCoC && bullishBreak;

                if (confirmsBearishReversal || confirmsBullishReversal)
                {
                    bosEvent = new BreakOfStructureEvent
                    {
                        IsBullish   = confirmsBullishReversal,
                        BrokenLevel = confirmsBullishReversal ? prevSwingHigh!.Price : prevSwingLow!.Price,
                        Time        = candle.OpenTime,
                        CandleIndex = currentCandleIndex
                    };

                    newState = BotStructureState.Reversed;
                    newTrend = confirmsBullishReversal ? MarketTrend.Uptrend : MarketTrend.Downtrend;
                }
                else
                {
                    // Still waiting — carry Paused state forward unchanged.
                    newState          = BotStructureState.Paused;
                    newCoCIndex       = cocCandleIndex;
                    newTrendBeforeCoC = trendBeforeCoC;
                    newWasBearishCoC  = wasBearishCoC;
                }

                break;
            }

            default:
                newState = BotStructureState.Active;
                break;
        }

        return new StructureSignal
        {
            BotState                      = newState,
            Trend                         = newTrend,
            BreakOfStructure              = bosEvent,
            ChangeOfCharacter             = cocEvent,
            ChangeOfCharacterCandleIndex  = newCoCIndex,
            TrendBeforeChangeOfCharacter  = newTrendBeforeCoC,
            WasBearishChangeOfCharacter   = newWasBearishCoC
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recent confirmed swing point of the given type whose candle index
    /// is strictly less than <paramref name="currentCandleIndex"/>, or null if none exists.
    /// </summary>
    private static SwingPoint? GetMostRecentSwingPoint(
        IReadOnlyList<SwingPoint> swingPoints,
        SwingPointType type,
        int currentCandleIndex)
    {
        return swingPoints
            .Where(sp => sp.IsConfirmed
                      && sp.Type         == type
                      && sp.CandleIndex  <  currentCandleIndex)
            .MaxBy(sp => sp.CandleIndex);
    }
}

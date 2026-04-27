using TradingBot.Core.Models;

namespace TradingBot.Core.Indicators;

/// <summary>
/// Calculates the Relative Strength Index using Wilder's Smoothed Moving Average.
/// Built entirely from scratch — no third-party indicator libraries (Section 28).
///
/// Algorithm:
///   1. Compute price changes (Close[i] − Close[i−1]) from a warm-up start.
///   2. Initialise average gain and average loss as simple averages over the first period.
///   3. Apply Wilder's smoothing for every subsequent candle:
///        avgGain = (prevAvgGain × (period − 1) + currentGain) / period
///        avgLoss = (prevAvgLoss × (period − 1) + currentLoss) / period
///   4. RS = avgGain / avgLoss
///   5. RSI = 100 − (100 / (1 + RS))
///
/// To minimise look-back bias the warm-up starts at most 3 × period candles before
/// <paramref name="endIndex"/>. This gives Wilder's EMA sufficient candles to converge
/// before the target index.
/// </summary>
public static class RsiCalculator
{
    /// <summary>
    /// Calculates RSI at <paramref name="endIndex"/> using <paramref name="period"/> bars.
    /// Returns 50 (neutral) when there are fewer than period + 1 candles available.
    /// </summary>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="period">RSI lookback period (default 14 per Section 17).</param>
    /// <param name="endIndex">Index of the candle whose RSI is required.</param>
    public static decimal Calculate(IReadOnlyList<Candle> candles, int period, int endIndex)
    {
        if (candles is null) throw new ArgumentNullException(nameof(candles));
        if (period < 1)      throw new ArgumentOutOfRangeException(nameof(period));

        // Need at least period + 1 closes to produce one RSI value.
        if (endIndex < period) return 50m;

        // Warm-up start: go back at most 3 × period candles so Wilder's EMA converges.
        int warmupStart = Math.Max(0, endIndex - period * 3);

        // ── Step 1: initialise with the first `period` price changes ──────────
        decimal sumGain = 0m;
        decimal sumLoss = 0m;

        int initEnd = warmupStart + period;
        for (int i = warmupStart + 1; i <= initEnd && i <= endIndex; i++)
        {
            decimal change = candles[i].Close - candles[i - 1].Close;
            if (change > 0) sumGain += change;
            else            sumLoss += Math.Abs(change);
        }

        decimal avgGain = sumGain / period;
        decimal avgLoss = sumLoss / period;

        // ── Step 2: Wilder's smoothing for all remaining candles ──────────────
        for (int i = initEnd + 1; i <= endIndex; i++)
        {
            decimal change = candles[i].Close - candles[i - 1].Close;
            decimal gain   = change > 0 ? change        : 0m;
            decimal loss   = change < 0 ? Math.Abs(change) : 0m;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;
        }

        if (avgLoss == 0m) return 100m;

        decimal rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    /// <summary>
    /// Returns RSI values for every index in [<paramref name="startIndex"/>,
    /// <paramref name="endIndex"/>] in a single pass, avoiding redundant recalculation.
    /// Useful for the backtester when a range of RSI values is needed at once.
    /// </summary>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="period">RSI lookback period.</param>
    /// <param name="startIndex">First index to include in the output.</param>
    /// <param name="endIndex">Last index to include in the output.</param>
    public static IReadOnlyList<decimal> CalculateRange(
        IReadOnlyList<Candle> candles,
        int period,
        int startIndex,
        int endIndex)
    {
        if (candles is null)             throw new ArgumentNullException(nameof(candles));
        if (period < 1)                  throw new ArgumentOutOfRangeException(nameof(period));
        if (startIndex > endIndex)       throw new ArgumentException("startIndex must be <= endIndex.");

        var results = new decimal[endIndex - startIndex + 1];

        // Warm up from as early as possible to get accurate Wilder smoothing.
        int warmupStart = Math.Max(0, startIndex - period * 3);

        decimal avgGain = 0m;
        decimal avgLoss = 0m;
        bool    initialised = false;

        // Initialise over the first `period` changes from warmupStart.
        int initEnd = warmupStart + period;
        if (initEnd <= endIndex)
        {
            decimal sumGain = 0m;
            decimal sumLoss = 0m;

            for (int i = warmupStart + 1; i <= initEnd; i++)
            {
                decimal change = candles[i].Close - candles[i - 1].Close;
                if (change > 0) sumGain += change;
                else            sumLoss += Math.Abs(change);
            }

            avgGain     = sumGain / period;
            avgLoss     = sumLoss / period;
            initialised = true;

            // Apply Wilder's smoothing up to startIndex − 1 to warm up the values.
            for (int i = initEnd + 1; i < startIndex; i++)
            {
                decimal change = candles[i].Close - candles[i - 1].Close;
                avgGain = (avgGain * (period - 1) + (change > 0 ? change        : 0m)) / period;
                avgLoss = (avgLoss * (period - 1) + (change < 0 ? Math.Abs(change) : 0m)) / period;
            }
        }

        // Fill results from startIndex to endIndex.
        for (int i = startIndex; i <= endIndex; i++)
        {
            if (!initialised || i < period)
            {
                results[i - startIndex] = 50m; // neutral until enough data
                continue;
            }

            decimal change = candles[i].Close - candles[i - 1].Close;
            decimal gain   = change > 0 ? change        : 0m;
            decimal loss   = change < 0 ? Math.Abs(change) : 0m;

            avgGain = (avgGain * (period - 1) + gain) / period;
            avgLoss = (avgLoss * (period - 1) + loss) / period;

            decimal rsi = avgLoss == 0m
                ? 100m
                : 100m - (100m / (1m + avgGain / avgLoss));

            results[i - startIndex] = rsi;
        }

        return results;
    }
}

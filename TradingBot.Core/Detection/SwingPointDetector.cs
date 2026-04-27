using TradingBot.Core.Configuration;
using TradingBot.Core.Models;

namespace TradingBot.Core.Detection;

/// <summary>
/// Detects confirmed swing highs and swing lows from a candle series.
/// Implements Section 3 of the Master Prompt exactly:
///   Swing High: a candle with lower highs on both sides, minimum 2 candles each side.
///   Swing Low:  a candle with higher lows on both sides, minimum 2 candles each side.
///   Confirmed only after the right-side candles have closed.
///   Strength 1 (less than 1%), 2 (1 to 3%), 3 (greater than 3%).
/// </summary>
public sealed class SwingPointDetector
{
    private readonly BotConfig _config;

    public SwingPointDetector(BotConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Scans the full candle list and returns all confirmed swing points.
    /// Uses SwingLookback and SwingConfirmCandles from config.
    /// Candles must be ordered oldest-first.
    /// </summary>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="timeframe">Timeframe label stamped on each result, e.g. "1H".</param>
    public IReadOnlyList<SwingPoint> Detect(IReadOnlyList<Candle> candles, string timeframe)
    {
        if (candles is null) throw new ArgumentNullException(nameof(candles));

        int confirm = _config.SwingConfirmCandles;
        int start = Math.Max(0, candles.Count - _config.SwingLookback);
        var results = new List<SwingPoint>();

        for (int i = start + confirm; i <= candles.Count - 1 - confirm; i++)
        {
            if (IsSwingHigh(candles, i, confirm))
                results.Add(Build(candles, i, SwingPointType.High, timeframe, isConfirmed: true));

            if (IsSwingLow(candles, i, confirm))
                results.Add(Build(candles, i, SwingPointType.Low, timeframe, isConfirmed: true));
        }

        return results;
    }

    /// <summary>
    /// Returns confirmed swing points as they would appear at <paramref name="asOfIndex"/>,
    /// preventing any look-ahead bias during backtesting or live scanning.
    /// </summary>
    /// <param name="candles">Full candle series, oldest first.</param>
    /// <param name="asOfIndex">Index of the most recently closed candle.</param>
    /// <param name="timeframe">Timeframe label stamped on each result, e.g. "1H".</param>
    public IReadOnlyList<SwingPoint> DetectUpTo(IReadOnlyList<Candle> candles, int asOfIndex, string timeframe)
    {
        if (candles is null) throw new ArgumentNullException(nameof(candles));

        int confirm = _config.SwingConfirmCandles;
        int start = Math.Max(0, asOfIndex - _config.SwingLookback);
        var results = new List<SwingPoint>();

        int lastCandidate = asOfIndex - confirm;

        for (int i = start + confirm; i <= lastCandidate; i++)
        {
            if (IsSwingHigh(candles, i, confirm))
                results.Add(Build(candles, i, SwingPointType.High, timeframe, isConfirmed: true));

            if (IsSwingLow(candles, i, confirm))
                results.Add(Build(candles, i, SwingPointType.Low, timeframe, isConfirmed: true));
        }

        return results;
    }

    // ── private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the candle at <paramref name="index"/> has strictly lower highs
    /// on both sides for <paramref name="confirm"/> candles each (Section 3 Swing High rule).
    /// </summary>
    private static bool IsSwingHigh(IReadOnlyList<Candle> candles, int index, int confirm)
    {
        decimal pivot = candles[index].High;

        for (int j = 1; j <= confirm; j++)
        {
            if (candles[index - j].High >= pivot) return false;
            if (candles[index + j].High >= pivot) return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true when the candle at <paramref name="index"/> has strictly higher lows
    /// on both sides for <paramref name="confirm"/> candles each (Section 3 Swing Low rule).
    /// </summary>
    private static bool IsSwingLow(IReadOnlyList<Candle> candles, int index, int confirm)
    {
        decimal pivot = candles[index].Low;

        for (int j = 1; j <= confirm; j++)
        {
            if (candles[index - j].Low <= pivot) return false;
            if (candles[index + j].Low <= pivot) return false;
        }

        return true;
    }

    private static SwingPoint Build(
        IReadOnlyList<Candle> candles,
        int index,
        SwingPointType type,
        string timeframe,
        bool isConfirmed)
    {
        decimal price = type == SwingPointType.High
            ? candles[index].High
            : candles[index].Low;

        return new SwingPoint
        {
            CandleIndex  = index,
            Time         = candles[index].OpenTime,
            Price        = price,
            Type         = type,
            Strength     = CalculateStrength(candles, index, type),
            Timeframe    = timeframe,
            IsConfirmed  = isConfirmed
        };
    }

    /// <summary>
    /// Calculates swing strength from the percentage move between the pivot candle
    /// and the adjacent candle's opposite extreme.
    /// Less than 1% = 1, 1 to 3% = 2, greater than 3% = 3 (Section 3 Swing Strength).
    /// </summary>
    private static int CalculateStrength(IReadOnlyList<Candle> candles, int index, SwingPointType type)
    {
        decimal pivotPrice = type == SwingPointType.High
            ? candles[index].High
            : candles[index].Low;

        decimal referencePrice = type == SwingPointType.High
            ? candles[index - 1].Low
            : candles[index - 1].High;

        if (referencePrice == 0) return 1;

        decimal movePct = Math.Abs(pivotPrice - referencePrice) / referencePrice * 100m;

        return movePct switch
        {
            > 3m  => 3,
            >= 1m => 2,
            _     => 1
        };
    }
}

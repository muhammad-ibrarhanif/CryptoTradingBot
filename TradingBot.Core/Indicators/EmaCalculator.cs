using TradingBot.Core.Models;

namespace TradingBot.Core.Indicators;

public static class EmaCalculator
{
    public static decimal Calculate(List<Candle> candles, int index, int period)
    {
        if (index < period) return candles[index].Close;

        decimal multiplier = 2m / (period + 1);
        decimal ema = candles[0].Close;

        for (int i = 1; i <= index; i++)
            ema = (candles[i].Close - ema) * multiplier + ema;

        return ema;
    }

    public static decimal[] CalculateAll(List<Candle> candles, int period)
    {
        var emaValues = new decimal[candles.Count];
        decimal multiplier = 2m / (period + 1);

        emaValues[0] = candles[0].Close;
        for (int i = 1; i < candles.Count; i++)
            emaValues[i] = (candles[i].Close - emaValues[i - 1]) * multiplier + emaValues[i - 1];

        return emaValues;
    }
}
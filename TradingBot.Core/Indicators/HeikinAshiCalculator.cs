using TradingBot.Core.Models;

namespace TradingBot.Core.Indicators;

public class HeikinAshiCandle
{
    public DateTime OpenTime { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public bool IsBullish => Close > Open;
    public bool IsBearish => Close < Open;
    public decimal Body => Math.Abs(Close - Open);
    public decimal UpperWick => High - Math.Max(Open, Close);
    public decimal LowerWick => Math.Min(Open, Close) - Low;
}

public static class HeikinAshiCalculator
{
    public static List<HeikinAshiCandle> Calculate(List<Candle> candles)
    {
        var haCandles = new List<HeikinAshiCandle>();

        for (int i = 0; i < candles.Count; i++)
        {
            var ha = new HeikinAshiCandle
            {
                OpenTime = candles[i].OpenTime
            };

            if (i == 0)
            {
                ha.Open = (candles[i].Open + candles[i].Close) / 2;
            }
            else
            {
                ha.Open = (haCandles[i - 1].Open + haCandles[i - 1].Close) / 2;
            }

            ha.Close = (candles[i].Open + candles[i].High + candles[i].Low + candles[i].Close) / 4;
            ha.High = Math.Max(candles[i].High, Math.Max(ha.Open, ha.Close));
            ha.Low = Math.Min(candles[i].Low, Math.Min(ha.Open, ha.Close));

            haCandles.Add(ha);
        }

        return haCandles;
    }

    public static string GetTrend(List<HeikinAshiCandle> haCandles, int lookback = 3)
    {
        if (haCandles.Count < lookback) return "WAITING";

        bool allBullish = true;
        bool allBearish = true;

        for (int i = haCandles.Count - lookback; i < haCandles.Count; i++)
        {
            if (!haCandles[i].IsBullish) allBullish = false;
            if (!haCandles[i].IsBearish) allBearish = false;
        }

        if (allBullish) return "📈 UPTREND";
        if (allBearish) return "📉 DOWNTREND";
        return "➡️ RANGING";
    }
}
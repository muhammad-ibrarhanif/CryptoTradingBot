using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Backtester.Models;

namespace TradingBot.Backtester;

public static class TrendScanner
{
    private static readonly List<string> Coins = new()
    {
        "BTCUSDT", "ETHUSDT", "BNBUSDT", "SOLUSDT", "XRPUSDT",
        "DOGEUSDT", "ADAUSDT", "AVAXUSDT", "DOTUSDT", "LINKUSDT",
        "MATICUSDT", "UNIUSDT", "ATOMUSDT", "LTCUSDT", "ETCUSDT"
    };

    public static async Task<List<TrendScore>> ScanTrendsAsync(DateTime currentTime)
    {
        var results = new List<TrendScore>();
        var startTime = currentTime.AddHours(-24);

        using var client = new BinanceRestClient();

        foreach (var symbol in Coins)
        {
            try
            {
                var klines = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.OneHour, startTime, currentTime, limit: 30);
                if (!klines.Success || klines.Data == null || !klines.Data.Any()) continue;

                var candles = klines.Data.Select(k => new Candle
                {
                    OpenTime = k.OpenTime,
                    Open = k.OpenPrice,
                    High = k.HighPrice,
                    Low = k.LowPrice,
                    Close = k.ClosePrice,
                    Volume = k.Volume,
                    CloseTime = k.CloseTime
                }).ToList();

                if (candles.Count < 20) continue;

                var score = CalculateScore(candles, symbol);
                results.Add(score);
            }
            catch { }
        }

        return results.OrderByDescending(r => r.Score).ToList();
    }

    private static TrendScore CalculateScore(List<Candle> candles, string symbol)
    {
        decimal currentPrice = candles.Last().Close;
        decimal price24hAgo = candles.First().Close;
        decimal priceChange = ((currentPrice - price24hAgo) / price24hAgo) * 100m;
        decimal rsi = CalculateRsi(candles, candles.Count - 1, 14);

        decimal avgVolume = candles.Skip(candles.Count - 10).Average(c => c.Volume);
        decimal volumeMomentum = candles.Last().Volume / avgVolume;

        decimal ema20 = CalculateEma(candles, candles.Count - 1, 20);
        bool aboveEma20 = currentPrice > ema20;

        decimal score = 0;
        score += Math.Min(30, Math.Max(0, priceChange * 2));
        score += Math.Min(20, volumeMomentum * 10);
        if (aboveEma20) score += 15;
        if (rsi >= 40 && rsi <= 70) score += 20 - Math.Abs(50 - rsi) / 2;

        score = Math.Min(100, Math.Max(0, score));

        bool isUptrend = priceChange > -2m && rsi > 40 && rsi < 75 && aboveEma20;

        return new TrendScore
        {
            Symbol = symbol,
            Score = score,
            IsUptrend = isUptrend,
            PriceChangePercent = priceChange,
            Rsi = rsi,
            VolumeMomentum = volumeMomentum
        };
    }

    private static decimal CalculateRsi(List<Candle> candles, int index, int period)
    {
        if (index < period) return 50m;
        decimal avgGain = 0, avgLoss = 0;
        for (int i = index - period + 1; i <= index; i++)
        {
            decimal change = candles[i].Close - candles[i - 1].Close;
            if (change > 0) avgGain += change;
            else avgLoss += Math.Abs(change);
        }
        avgGain /= period;
        avgLoss /= period;
        if (avgLoss == 0) return 100m;
        return 100m - (100m / (1m + avgGain / avgLoss));
    }

    private static decimal CalculateEma(List<Candle> candles, int index, int period)
    {
        if (index < period) return candles[index].Close;
        decimal multiplier = 2m / (period + 1);
        decimal ema = candles[0].Close;
        for (int i = 1; i <= index; i++)
            ema = (candles[i].Close - ema) * multiplier + ema;
        return ema;
    }
}
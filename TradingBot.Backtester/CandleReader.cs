using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public static class CandleReader
{
    public static async Task ReadAllTimeframesAsync(string symbol, DateTime date)
    {
        Console.WriteLine($"\n========================================");
        Console.WriteLine($"READING CANDLES FOR {symbol} ON {date:yyyy-MM-dd}");
        Console.WriteLine($"========================================\n");

        // STEP 1: 1H CANDLES (Overall trend)
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📊 STEP 1: 1-HOUR CANDLES (Overall Trend)");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var start1H = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0);
        var end1H = start1H.AddHours(24);
        var candles1H = await FetchCandles(symbol, KlineInterval.OneHour, start1H, end1H);

        Console.WriteLine($"\n{"Time",-8} {"Type",-8} {"Open",-8} {"High",-8} {"Low",-8} {"Close",-8} {"Body",-8}");
        Console.WriteLine(new string('-', 60));

        foreach (var candle in candles1H)
        {
            string candleType = candle.Close > candle.Open ? "BULLISH" : "BEARISH";
            string timeStr = candle.OpenTime.ToString("HH:00");
            Console.WriteLine($"{timeStr,-8} {candleType,-8} {candle.Open,-8:F2} {candle.High,-8:F2} {candle.Low,-8:F2} {candle.Close,-8:F2} {candle.Body,-8:F2}");
        }

        // STEP 2: 15M CANDLES (Support/Resistance) - Focus on key hours
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📊 STEP 2: 15-MINUTE CANDLES (Support/Resistance)");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var start15m = new DateTime(date.Year, date.Month, date.Day, 12, 0, 0);
        var end15m = start15m.AddHours(6);
        var candles15m = await FetchCandles(symbol, KlineInterval.FifteenMinutes, start15m, end15m);

        Console.WriteLine($"\n{"Time",-10} {"Type",-8} {"Open",-8} {"High",-8} {"Low",-8} {"Close",-8} {"Lowest",-8}");
        Console.WriteLine(new string('-', 65));

        // Find the lowest low (support)
        decimal lowestLow = candles15m.Min(c => c.Low);

        foreach (var candle in candles15m)
        {
            string candleType = candle.Close > candle.Open ? "BULLISH" : "BEARISH";
            string timeStr = candle.OpenTime.ToString("HH:mm");
            string isSupport = Math.Abs(candle.Low - lowestLow) / lowestLow < 0.002m ? "← SUPPORT" : "";
            Console.WriteLine($"{timeStr,-10} {candleType,-8} {candle.Open,-8:F2} {candle.High,-8:F2} {candle.Low,-8:F2} {candle.Close,-8:F2} {isSupport}");
        }

        // STEP 3: 5M CANDLES (Entry pattern around 14:35)
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📊 STEP 3: 5-MINUTE CANDLES (Entry Pattern at 14:35)");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var start5m = new DateTime(date.Year, date.Month, date.Day, 14, 0, 0);
        var end5m = start5m.AddHours(2);
        var candles5m = await FetchCandles(symbol, KlineInterval.FiveMinutes, start5m, end5m);

        Console.WriteLine($"\n{"Time",-8} {"Type",-8} {"Open",-8} {"High",-8} {"Low",-8} {"Close",-8} {"Body",-8} {"LowerWick",-8} {"Hammer?",-8}");
        Console.WriteLine(new string('-', 75));

        foreach (var candle in candles5m)
        {
            string candleType = candle.Close > candle.Open ? "BULLISH" : "BEARISH";
            string timeStr = candle.OpenTime.ToString("HH:mm");
            bool isHammer = candle.LowerWick > candle.Body * 2 && candle.Body > 0;
            string hammer = isHammer ? "YES ✓" : "";
            Console.WriteLine($"{timeStr,-8} {candleType,-8} {candle.Open,-8:F4} {candle.High,-8:F4} {candle.Low,-8:F4} {candle.Close,-8:F4} {candle.Body,-8:F4} {candle.LowerWick,-8:F4} {hammer}");
        }
    }

    private static async Task<List<Candle>> FetchCandles(string symbol, KlineInterval interval, DateTime start, DateTime end)
    {
        using var client = new BinanceRestClient();
        var allCandles = new List<Candle>();
        var currentStart = start;

        while (currentStart < end)
        {
            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, interval, currentStart, end, limit: 1000);
            if (!result.Success || result.Data == null || !result.Data.Any()) break;

            var candles = result.Data.Select(k => new Candle
            {
                OpenTime = k.OpenTime,
                Open = k.OpenPrice,
                High = k.HighPrice,
                Low = k.LowPrice,
                Close = k.ClosePrice,
                Volume = k.Volume,
                CloseTime = k.CloseTime
            }).ToList();

            allCandles.AddRange(candles);
            if (candles.Count < 1000) break;
            currentStart = candles.Last().OpenTime.AddMinutes(GetMinutesForInterval(interval));
            await Task.Delay(100);
        }

        return allCandles;
    }

    private static int GetMinutesForInterval(KlineInterval interval) => interval switch
    {
        KlineInterval.FiveMinutes => 5,
        KlineInterval.FifteenMinutes => 15,
        KlineInterval.OneHour => 60,
        _ => 60
    };
}
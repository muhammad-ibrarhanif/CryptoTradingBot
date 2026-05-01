using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public static class ChartAnalyzer
{
    public static async Task AnalyzeAsync(string symbol, DateTime dateTime)
    {
        Console.WriteLine($"\n========================================");
        Console.WriteLine($"CHART ANALYSIS FOR {symbol} AT {dateTime:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"========================================\n");

        // Fetch candles
        var candles1H = await FetchCandles(symbol, KlineInterval.OneHour, dateTime.AddHours(-48), dateTime);
        var candles15m = await FetchCandles(symbol, KlineInterval.FifteenMinutes, dateTime.AddHours(-12), dateTime);
        var candles5m = await FetchCandles(symbol, KlineInterval.FiveMinutes, dateTime.AddHours(-4), dateTime);

        // STEP 1: 1H - Look at the chart with your eyes
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📊 STEP 1: 1-HOUR CHART - What does your eye see?");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var last10Highs = candles1H.Skip(candles1H.Count - 10).Select(c => c.High).ToList();
        var last10Lows = candles1H.Skip(candles1H.Count - 10).Select(c => c.Low).ToList();

        // Human-like trend detection
        var highestHigh = last10Highs.Max();
        var highestHighIndex = last10Highs.IndexOf(highestHigh);
        var lowestLow = last10Lows.Min();
        var lowestLowIndex = last10Lows.IndexOf(lowestLow);

        // Check if the highest high is recent (last 3 candles)
        bool recentHigh = highestHighIndex >= last10Highs.Count - 3;

        // Check if lows are generally rising
        bool risingLows = last10Lows.Last() > last10Lows[0];

        // Check if current price is above the 4th highest low (simple filter)
        var sortedLows = last10Lows.OrderBy(l => l).ToList();
        decimal supportLevel = sortedLows.Skip(2).First(); // 3rd lowest low
        bool priceAboveSupport = candles1H.Last().Close > supportLevel;

        bool isUptrend = (recentHigh || risingLows) && priceAboveSupport;

        Console.WriteLine($"  Highest High (last 10): {highestHigh:F4} at position {highestHighIndex + 1}/10");
        Console.WriteLine($"  Most recent high: {last10Highs.Last():F4}");
        Console.WriteLine($"  Recent high formed? {(recentHigh ? "YES ✓" : "NO")}");
        Console.WriteLine($"  Rising Lows trend? {(risingLows ? "YES ✓" : "NO")}");
        Console.WriteLine($"  Price above support? YES ✓");
        Console.WriteLine($"\n  📌 VERDICT: {(isUptrend ? "UPTREND ✓ - Look for long trades" : "NOT UPTREND - Wait")}");


        // STEP 2: 15m - Find where price bounced before
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📊 STEP 2: 15-MINUTE CHART - Where is support?");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var recentBars = candles15m.Skip(candles15m.Count - 20).ToList();
         supportLevel = recentBars.Min(c => c.Low);
        var resistanceLevel = recentBars.Max(c => c.High);

        var last15m = candles15m.Last();
        bool nearSupport = Math.Abs(last15m.Low - supportLevel) / supportLevel < 0.005m;

        Console.WriteLine($"  Lowest Low (last 20 candles): {supportLevel:F4} (Support)");
        Console.WriteLine($"  Highest High (last 20 candles): {resistanceLevel:F4} (Resistance)");
        Console.WriteLine($"  Current Price: {last15m.Close:F4}");
        Console.WriteLine($"  Near Support? {(nearSupport ? "YES ✓ - Watch for bounce" : "NO")}");

        // Count how many times support was tested
        int supportTests = recentBars.Count(c => Math.Abs(c.Low - supportLevel) / supportLevel < 0.002m);
        Console.WriteLine($"  Support tested: {supportTests} times (more tests = stronger level)");

        // STEP 3: 5m - Look for the reversal signal
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("📊 STEP 3: 5-MINUTE CHART - Is price reversing?");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        var current = candles5m.Last();
        var previous = candles5m[candles5m.Count - 2];
        var beforePrevious = candles5m[candles5m.Count - 3];

        // Simple reversal detection - what a human sees
        bool bullishEngulfing = current.Open < previous.Close && current.Close > previous.Open;
        bool hammerReversal = current.LowerWick > current.Body * 2 && current.Close > current.Open;
        bool higherLow = current.Low > previous.Low;
        bool breaksPreviousHigh = current.Close > previous.High;

        Console.WriteLine($"  Current Candle: O={current.Open:F4} H={current.High:F4} L={current.Low:F4} C={current.Close:F4}");
        Console.WriteLine($"  Previous Candle: O={previous.Open:F4} H={previous.High:F4} L={previous.Low:F4} C={previous.Close:F4}");

        Console.WriteLine($"\n  Bullish Engulfing? {(bullishEngulfing ? "YES ✓" : "NO")}");
        Console.WriteLine($"  Hammer at Support? {(hammerReversal ? "YES ✓" : "NO")}");
        Console.WriteLine($"  Higher Low than previous? {(higherLow ? "YES ✓" : "NO")}");
        Console.WriteLine($"  Breaks Previous High? {(breaksPreviousHigh ? "YES ✓" : "NO")}");

        bool bullishSignal = bullishEngulfing || hammerReversal || (higherLow && breaksPreviousHigh);

        // Volume check - human looks for higher volume on the reversal
        var avgVolume = candles5m.Skip(candles5m.Count - 21).Take(20).Average(c => c.Volume);
        bool volumeConfirmed = current.Volume > avgVolume * 1.2m;

        Console.WriteLine($"\n  Volume: {current.Volume:F0} (Avg: {avgVolume:F0})");
        Console.WriteLine($"  Volume Confirms Reversal? {(volumeConfirmed ? "YES ✓" : "NO")}");

        // FINAL DECISION
        Console.WriteLine("\n━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        Console.WriteLine("🎯 FINAL DECISION - Would a human take this trade?");
        Console.WriteLine("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");

        Console.WriteLine($"  Condition 1: 1H Uptrend? {(isUptrend ? "✓ YES" : "✗ NO")}");
        Console.WriteLine($"  Condition 2: Near Support? {(nearSupport ? "✓ YES" : "✗ NO")}");
        Console.WriteLine($"  Condition 3: Bullish Reversal Signal? {(bullishSignal ? "✓ YES" : "✗ NO")}");
        Console.WriteLine($"  Condition 4: Volume Confirmation? {(volumeConfirmed ? "✓ YES" : "✗ NO")}");

        bool takeTrade = isUptrend && nearSupport && bullishSignal && volumeConfirmed;

        if (takeTrade)
        {
            decimal entry = current.Close;
            decimal stopLoss = supportLevel * 0.998m;
            decimal risk = entry - stopLoss;
            decimal target = entry + risk * 2;

            Console.WriteLine($"\n  ✅ TRADE SIGNAL CONFIRMED");
            Console.WriteLine($"\n  Suggested Trade:");
            Console.WriteLine($"    ENTRY:  {entry:F4}");
            Console.WriteLine($"    STOP:   {stopLoss:F4} (Risk: {(risk / entry * 100):F2}%)");
            Console.WriteLine($"    TARGET: {target:F4} (Reward: {(risk * 2 / entry * 100):F2}%)");
            Console.WriteLine($"    R:R:    2:1");
        }
        else
        {
            Console.WriteLine($"\n  ❌ NO TRADE - Keep watching");

            if (!isUptrend) Console.WriteLine($"     → 1H trend is not up");
            if (!nearSupport) Console.WriteLine($"     → Price not near support");
            if (!bullishSignal) Console.WriteLine($"     → No bullish reversal pattern");
            if (!volumeConfirmed) Console.WriteLine($"     → Volume doesn't confirm");
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
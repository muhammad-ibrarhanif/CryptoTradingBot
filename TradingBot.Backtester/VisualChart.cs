using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public static class VisualChart
{
    public static async Task DisplayAsync(string symbol, DateTime start, DateTime end)
    {
        Console.Clear();
        Console.WriteLine($"\n========================================");
        Console.WriteLine($"VISUAL CHART WITH DYNAMIC S/R: {symbol}");
        Console.WriteLine($"Period: {start:yyyy-MM-dd HH:mm} to {end:yyyy-MM-dd HH:mm} UTC");
        Console.WriteLine($"========================================\n");

        // Fetch candles
        var candles5m = await Fetch5mCandles(symbol, start, end);
        var candles15m = await Fetch15mCandles(symbol, start.AddHours(-4), end);

        // Get initial support/resistance from historical data
        var support = FindStrongSupport(candles15m);
        var resistance = FindStrongResistance(candles15m);
        var brokenSupport = false;
        var brokenResistance = false;
        var newResistanceFromSupport = 0m;
        var newSupportFromResistance = 0m;

        Console.WriteLine($"INITIAL SUPPORT: {support:F4} (tested {GetTestCount(candles15m, support)} times)");
        Console.WriteLine($"INITIAL RESISTANCE: {resistance:F4} (tested {GetTestCount(candles15m, resistance)} times)");
        Console.WriteLine($"\n⚠️ Note: When support breaks, it becomes resistance");
        Console.WriteLine($"⚠️ When resistance breaks, it becomes support\n");

        Console.WriteLine($"{"Time",-8} {"Price",-8} {"Candle",-12} {"Zone",-35} {"Pattern",-25} {"Signal",-15}");
        Console.WriteLine(new string('-', 110));

        for (int i = 2; i < candles5m.Count; i++)
        {
            var current = candles5m[i];
            var previous = candles5m[i - 1];
            var twoBack = candles5m[i - 2];

            // Check for BREAKS
            // Support broken: price closes BELOW support
            if (!brokenSupport && current.Close < support)
            {
                brokenSupport = true;
                newResistanceFromSupport = support;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{current.OpenTime:HH:mm,-8} {current.Close,-8:F2} {'⬇',-12} ✘ SUPPORT BROKEN! 83.90 NOW BECOMES RESISTANCE ✘", 0);
                Console.ResetColor();
            }

            // Resistance broken: price closes ABOVE resistance
            if (!brokenResistance && current.Close > resistance)
            {
                brokenResistance = true;
                newSupportFromResistance = resistance;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"{current.OpenTime:HH:mm,-8} {current.Close,-8:F2} {'⬆',-12} ✓ RESISTANCE BROKEN! 84.60 NOW BECOMES SUPPORT ✓", 0);
                Console.ResetColor();
            }

            // Determine current active zones
            string zone = "";
            bool nearSupport = false;
            bool nearResistance = false;

            if (!brokenSupport && !brokenResistance)
            {
                // Normal mode: original support/resistance
                nearSupport = Math.Abs(current.Low - support) / support < 0.0015m;
                nearResistance = Math.Abs(current.High - resistance) / resistance < 0.0015m;

                if (nearSupport)
                    zone = "▼ SUPPORT ZONE (original) ▼";
                else if (nearResistance)
                    zone = "▲ RESISTANCE ZONE (original) ▲";
            }
            else if (brokenSupport && !brokenResistance)
            {
                // Support broken: now it's resistance
                nearResistance = Math.Abs(current.High - newResistanceFromSupport) / newResistanceFromSupport < 0.0015m;
                nearSupport = Math.Abs(current.Low - resistance) / resistance < 0.0015m;

                if (nearResistance)
                    zone = "▲ OLD SUPPORT → NOW RESISTANCE ▲";
                else if (nearSupport)
                    zone = "▼ SUPPORT ZONE ▼";
            }
            else if (!brokenSupport && brokenResistance)
            {
                // Resistance broken: now it's support
                nearSupport = Math.Abs(current.Low - newSupportFromResistance) / newSupportFromResistance < 0.0015m;
                nearResistance = Math.Abs(current.High - support) / support < 0.0015m;

                if (nearSupport)
                    zone = "▼ OLD RESISTANCE → NOW SUPPORT ▼";
                else if (nearResistance)
                    zone = "▲ RESISTANCE ZONE ▲";
            }

            // Pattern detection
            string pattern = "";
            string signal = "";

            // Hammer at support
            bool isHammer = current.LowerWick > current.Body * 2 && current.Body > 0 && current.Close > current.Open;
            if (isHammer && nearSupport)
            {
                pattern = "🔨 HAMMER AT SUPPORT";
                signal = "BUY ★★";
            }

            // Doji at support
            bool isDoji = current.Body < (current.High - current.Low) * 0.1m;
            if (isDoji && nearSupport)
            {
                pattern = "✚ DOJI AT SUPPORT";
                signal = "BUY ★";
            }

            // Morning Star
            bool isMorningStar = twoBack.Close < twoBack.Open &&
                                 previous.Body < (previous.High - previous.Low) * 0.1m &&
                                 current.Close > current.Open &&
                                 current.Close > twoBack.High;

            if (isMorningStar && nearSupport)
            {
                pattern = "⭐ MORNING STAR";
                signal = "BUY ★★★";
            }

            // Shooting Star at resistance
            bool isShootingStar = current.UpperWick > current.Body * 2 && current.Body > 0 && current.Close < current.Open;
            if (isShootingStar && nearResistance)
            {
                pattern = "💫 SHOOTING STAR AT RESISTANCE";
                signal = "SELL ★★";
            }

            // Piercing Pattern at support
            bool isPiercing = previous.Close < previous.Open &&
                              current.Open < previous.Low &&
                              current.Close > (previous.Open + previous.Close) / 2;

            if (isPiercing && nearSupport)
            {
                pattern = "📌 PIERCING PATTERN";
                signal = "BUY ★★";
            }

            // Color based on signal
            if (signal.Contains("BUY"))
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else if (signal.Contains("SELL"))
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }
            else if (current.Close > current.Open)
            {
                Console.ForegroundColor = ConsoleColor.Green;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
            }

            string candleDisplay = DrawCandle(current);
            Console.WriteLine($"{current.OpenTime:HH:mm,-8} {current.Close,-8:F2} {candleDisplay,-12} {zone,-35} {pattern,-25} {signal,-15}");
        }

        Console.ResetColor();
        Console.WriteLine($"\n========================================");
        Console.WriteLine("DYNAMIC S/R LEGEND:");
        Console.WriteLine("  ▼ SUPPORT ZONE = Price near support (good for BUY)");
        Console.WriteLine("  ▲ RESISTANCE ZONE = Price near resistance (good for SELL)");
        Console.WriteLine("  ✘ SUPPORT BROKEN → Level becomes RESISTANCE");
        Console.WriteLine("  ✓ RESISTANCE BROKEN → Level becomes SUPPORT");
        Console.WriteLine("========================================");
    }

    private static string DrawCandle(Candle candle)
    {
        if (candle.Close > candle.Open)
        {
            int bodyLength = (int)(candle.Body / 0.01m);
            bodyLength = Math.Max(1, Math.Min(10, bodyLength));
            return new string('█', bodyLength);
        }
        else
        {
            int bodyLength = (int)(candle.Body / 0.01m);
            bodyLength = Math.Max(1, Math.Min(10, bodyLength));
            return new string('░', bodyLength);
        }
    }

    private static decimal FindStrongSupport(List<Candle> candles)
    {
        var lowLevels = candles.Select(c => Math.Round(c.Low / 0.05m) * 0.05m)
                               .GroupBy(l => l)
                               .Select(g => new { Level = g.Key, Count = g.Count() })
                               .OrderByDescending(g => g.Count)
                               .ToList();

        if (lowLevels.Any())
            return lowLevels.First().Level;

        return candles.Min(c => c.Low);
    }

    private static decimal FindStrongResistance(List<Candle> candles)
    {
        var highLevels = candles.Select(c => Math.Round(c.High / 0.05m) * 0.05m)
                                .GroupBy(h => h)
                                .Select(g => new { Level = g.Key, Count = g.Count() })
                                .OrderByDescending(g => g.Count)
                                .ToList();

        if (highLevels.Any())
            return highLevels.First().Level;

        return candles.Max(c => c.High);
    }

    private static int GetTestCount(List<Candle> candles, decimal level)
    {
        return candles.Count(c => Math.Abs(c.Low - level) / level < 0.002m ||
                                  Math.Abs(c.High - level) / level < 0.002m);
    }

    private static async Task<List<Candle>> Fetch5mCandles(string symbol, DateTime start, DateTime end)
    {
        using var client = new BinanceRestClient();
        var allCandles = new List<Candle>();
        var currentStart = start;

        while (currentStart < end)
        {
            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.FiveMinutes, currentStart, end, limit: 1000);
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
            currentStart = candles.Last().OpenTime.AddMinutes(5);
            await Task.Delay(100);
        }

        return allCandles;
    }

    private static async Task<List<Candle>> Fetch15mCandles(string symbol, DateTime start, DateTime end)
    {
        using var client = new BinanceRestClient();
        var allCandles = new List<Candle>();
        var currentStart = start;

        while (currentStart < end)
        {
            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.FifteenMinutes, currentStart, end, limit: 1000);
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
            currentStart = candles.Last().OpenTime.AddMinutes(15);
            await Task.Delay(100);
        }

        return allCandles;
    }
}
using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public static class NaturalScalper
{
    public static async Task<BacktestResult> RunAsync(
        string symbol,
        DateTime start,
        DateTime end,
        decimal initialBalance = 3000m,
        decimal riskPercent = 1.0m)
    {
        Console.WriteLine($"\n=== NATURAL SCALPER (Human-like) ===");
        Console.WriteLine($"Symbol: {symbol}");
        Console.WriteLine($"Period: {start:yyyy-MM-dd HH:mm} to {end:yyyy-MM-dd HH:mm}");
        Console.WriteLine($"Risk per trade: {riskPercent}%");

        // Fetch candles for different timeframes
        var candles1H = await FetchCandles(symbol, KlineInterval.OneHour, start.AddHours(-48), end);
        var candles15m = await FetchCandles(symbol, KlineInterval.FifteenMinutes, start.AddHours(-12), end);
        var candles5m = await FetchCandles(symbol, KlineInterval.FiveMinutes, start, end);

        Console.WriteLine($"  1H candles: {candles1H.Count}");
        Console.WriteLine($"  15m candles: {candles15m.Count}");
        Console.WriteLine($"  5m candles: {candles5m.Count}");

        if (candles5m.Count < 50)
        {
            Console.WriteLine("  Insufficient data");
            return CreateEmptyResult(symbol, initialBalance);
        }

        decimal balance = initialBalance;
        int trades = 0, wins = 0;
        List<decimal> returns = new();

        bool inPosition = false;
        decimal entryPrice = 0;
        decimal stopLoss = 0;
        decimal targetPrice = 0;
        int entryIndex = 0;

        // Precompute 1H EMA50
        decimal[] ema50_1H = new decimal[candles1H.Count];
        if (candles1H.Count > 0)
        {
            decimal multiplier = 2m / 51m;
            ema50_1H[0] = candles1H[0].Close;
            for (int i = 1; i < candles1H.Count; i++)
                ema50_1H[i] = (candles1H[i].Close - ema50_1H[i - 1]) * multiplier + ema50_1H[i - 1];
        }

        for (int i = 20; i < candles5m.Count; i++)
        {
            var current5m = candles5m[i];
            var previous5m = candles5m[i - 1];

            bool isUptrend = IsUptrend(candles1H, ema50_1H, current5m.OpenTime);
            decimal support = FindSupport(candles15m, current5m.OpenTime);
            bool nearSupport = support > 0 && Math.Abs(current5m.Low - support) / support < 0.005m;
            bool bullishReversal = IsBullishReversal(current5m, previous5m);
            bool volumeConfirmation = IsVolumeConfirmed(candles5m, i);

            if (!inPosition && isUptrend && nearSupport && bullishReversal && volumeConfirmation)
            {
                entryPrice = current5m.Close;
                stopLoss = support * 0.998m;
                targetPrice = entryPrice + (entryPrice - stopLoss) * 2;
                inPosition = true;
                entryIndex = i;

                decimal riskAmount = balance * riskPercent / 100m;
                decimal stopDistance = entryPrice - stopLoss;
                if (stopDistance <= 0) stopDistance = entryPrice * 0.005m;
                decimal positionSize = riskAmount / stopDistance;

                Console.WriteLine($"\n  [{current5m.OpenTime:MM-dd HH:mm}] ENTRY LONG at {entryPrice:F4}");
                Console.WriteLine($"     Trend: {(isUptrend ? "Uptrend ✓" : "Downtrend")}");
                Console.WriteLine($"     Support: {support:F4} (nearby: {nearSupport})");
                Console.WriteLine($"     Pattern: {(bullishReversal ? "Bullish ✓" : "Bearish")}");
                Console.WriteLine($"     Volume: {(volumeConfirmation ? "Confirmed ✓" : "Low")}");
                Console.WriteLine($"     Stop: {stopLoss:F4} | Target: {targetPrice:F4}");
            }

            if (inPosition)
            {
                if (current5m.Low <= stopLoss)
                {
                    decimal exitPrice = stopLoss;
                    decimal stopDistance = entryPrice - stopLoss;
                    if (stopDistance <= 0) stopDistance = entryPrice * 0.005m;
                    decimal positionSize = (balance * riskPercent / 100m) / stopDistance;
                    decimal pnl = (exitPrice - entryPrice) * positionSize;
                    balance += pnl;
                    trades++;
                    if (pnl > 0) wins++;
                    returns.Add(pnl / (entryPrice * positionSize) * 100m);

                    Console.WriteLine($"  [{current5m.OpenTime:MM-dd HH:mm}] EXIT STOP LOSS at {exitPrice:F4} | PnL: {pnl:F2} | Balance: {balance:F2}");
                    inPosition = false;
                }
                else if (current5m.High >= targetPrice)
                {
                    decimal exitPrice = targetPrice;
                    decimal stopDistance = entryPrice - stopLoss;
                    if (stopDistance <= 0) stopDistance = entryPrice * 0.005m;
                    decimal positionSize = (balance * riskPercent / 100m) / stopDistance;
                    decimal pnl = (exitPrice - entryPrice) * positionSize;
                    balance += pnl;
                    trades++;
                    if (pnl > 0) wins++;
                    returns.Add(pnl / (entryPrice * positionSize) * 100m);

                    Console.WriteLine($"  [{current5m.OpenTime:MM-dd HH:mm}] EXIT TARGET at {exitPrice:F4} | PnL: {pnl:F2} | Balance: {balance:F2}");
                    inPosition = false;
                }
                else if (i - entryIndex > 48)
                {
                    decimal exitPrice = current5m.Close;
                    decimal stopDistance = entryPrice - stopLoss;
                    if (stopDistance <= 0) stopDistance = entryPrice * 0.005m;
                    decimal positionSize = (balance * riskPercent / 100m) / stopDistance;
                    decimal pnl = (exitPrice - entryPrice) * positionSize;
                    balance += pnl;
                    trades++;
                    if (pnl > 0) wins++;
                    returns.Add(pnl / (entryPrice * positionSize) * 100m);

                    Console.WriteLine($"  [{current5m.OpenTime:MM-dd HH:mm}] EXIT TIME (4 hours) at {exitPrice:F4} | PnL: {pnl:F2} | Balance: {balance:F2}");
                    inPosition = false;
                }
            }
        }

        double winRate = trades == 0 ? 0 : (double)wins / trades * 100;
        decimal totalReturn = (balance - initialBalance) / initialBalance * 100m;
        decimal days = (decimal)(end - start).TotalDays;
        decimal dailyProfit = days > 0 ? (balance - initialBalance) / days : 0;

        Console.WriteLine($"\n=== NATURAL SCALPER RESULTS ===");
        Console.WriteLine($"Trades: {trades} | Wins: {wins} | Win Rate: {winRate:F1}%");
        Console.WriteLine($"Return: {totalReturn:F2}% | Daily Profit: ${dailyProfit:F2}");

        return new BacktestResult
        {
            StrategyName = $"NaturalScalper-{symbol}",
            StartingBalance = initialBalance,
            EndingBalance = balance,
            TotalTrades = trades,
            WinningTrades = wins,
            WinRate = winRate,
            TotalReturnPercent = totalReturn,
            AvgReturnPerTrade = trades == 0 ? 0 : returns.Average()
        };
    }

    private static bool IsUptrend(List<Candle> candles1H, decimal[] ema50, DateTime currentTime)
    {
        if (candles1H.Count < 10) return true;

        int idx = candles1H.Count - 1;
        while (idx >= 0 && candles1H[idx].CloseTime > currentTime)
            idx--;

        if (idx < 5) return true;

        bool priceAboveEma = candles1H[idx].Close > ema50[idx];
        bool higherHighs = candles1H[idx].High > candles1H[idx - 1].High &&
                           candles1H[idx - 1].High > candles1H[idx - 2].High;
        bool higherLows = candles1H[idx].Low > candles1H[idx - 1].Low &&
                          candles1H[idx - 1].Low > candles1H[idx - 2].Low;

        return priceAboveEma && higherHighs && higherLows;
    }

    private static decimal FindSupport(List<Candle> candles15m, DateTime currentTime)
    {
        if (candles15m.Count < 20) return 0;

        int idx = candles15m.Count - 1;
        while (idx >= 0 && candles15m[idx].CloseTime > currentTime)
            idx--;

        if (idx < 10) return candles15m[idx].Low;

        decimal lowestLow = candles15m[idx].Low;
        for (int i = idx - 10; i <= idx; i++)
        {
            if (candles15m[i].Low < lowestLow)
                lowestLow = candles15m[i].Low;
        }

        return lowestLow;
    }

    private static bool IsBullishReversal(Candle current, Candle previous)
    {
        bool hammer = current.LowerWick > current.Body * 2 &&
                      current.Body > 0 &&
                      current.Close > current.Open;

        bool engulfing = current.Open < previous.Close &&
                         current.Close > previous.Open;

        bool higherLow = current.Low > previous.Low;
        bool closeAbovePrevHigh = current.Close > previous.High;

        return hammer || engulfing || (higherLow && closeAbovePrevHigh);
    }

    private static bool IsVolumeConfirmed(List<Candle> candles5m, int currentIndex)
    {
        if (currentIndex < 21) return true;

        decimal avgVolume = 0;
        for (int i = currentIndex - 20; i < currentIndex; i++)
            avgVolume += candles5m[i].Volume;
        avgVolume /= 20;

        return candles5m[currentIndex].Volume > avgVolume * 1.2m;
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

    private static BacktestResult CreateEmptyResult(string symbol, decimal balance)
    {
        return new BacktestResult
        {
            StrategyName = $"NaturalScalper-{symbol}",
            StartingBalance = balance,
            EndingBalance = balance,
            TotalTrades = 0,
            WinningTrades = 0,
            WinRate = 0,
            TotalReturnPercent = 0,
            AvgReturnPerTrade = 0
        };
    }
}
using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public static class ScalpingStrategy
{
    public static async Task<BacktestResult> RunAsync(
        string symbol,
        DateTime start,
        DateTime end,
        decimal initialBalance = 3000m,
        decimal riskPercent = 0.5m,
        decimal stopLossPercent = 0.3m,
        decimal profitTargetPercent = 0.55m,
        int rsiPeriod = 5,
        int rsiOversold = 28,
        int volumePeriod = 20,
        decimal volumeMultiplier = 1.3m,
        int emaPeriod = 20,
        bool useVolumeFilter = true,
        bool useEmaFilter = true,
        bool useTimeFilter = true,
        bool useTrendFilter = true,
        string trendTimeframe = "1H",
        bool verbose = true)
    {
        Console.WriteLine($"\n=== SCALPING {symbol} ===");
        Console.WriteLine($"  Period: {start:HH:mm} to {end:HH:mm}");
        Console.WriteLine($"  Risk: {riskPercent}% | Stop: {stopLossPercent}% | Target: {profitTargetPercent}%");
        Console.WriteLine($"  RSI threshold: <{rsiOversold} | Volume multiplier: >{volumeMultiplier}x | EMA filter: {(useEmaFilter ? "ON" : "OFF")}");

        var candles1m = await FetchCandles(symbol, KlineInterval.OneMinute, start, end);
        Console.WriteLine($"  1m candles: {candles1m.Count}");

        if (candles1m.Count < 50)
        {
            Console.WriteLine($"  Insufficient 1m data");
            return CreateEmptyResult(symbol, initialBalance);
        }

        // Simplified trend detection using EMA on trend timeframe
        bool[] isUptrend = new bool[candles1m.Count];

        if (useTrendFilter)
        {
            var candlesTrend = await FetchCandles(symbol, GetKlineInterval(trendTimeframe), start.AddHours(-24), end);
            Console.WriteLine($"  {trendTimeframe} candles: {candlesTrend.Count}");

            // Precompute EMA50 on trend timeframe
            decimal[] trendEma = new decimal[candlesTrend.Count];
            if (candlesTrend.Count > 0)
            {
                decimal multiplier = 2m / (50 + 1);
                trendEma[0] = candlesTrend[0].Close;
                for (int i = 1; i < candlesTrend.Count; i++)
                    trendEma[i] = (candlesTrend[i].Close - trendEma[i - 1]) * multiplier + trendEma[i - 1];
            }

            int trendIdx = 0;

            for (int i = 0; i < candles1m.Count; i++)
            {
                // Find the latest trend candle
                while (trendIdx < candlesTrend.Count && candlesTrend[trendIdx].CloseTime <= candles1m[i].CloseTime)
                    trendIdx++;

                int idx = Math.Max(0, trendIdx - 1);

                if (idx >= 0 && idx < candlesTrend.Count && trendEma[idx] > 0)
                {
                    decimal currentTrendPrice = candlesTrend[idx].Close;
                    decimal trendEmaValue = trendEma[idx];

                    // Simple uptrend: price above EMA50 on the trend timeframe
                    isUptrend[i] = currentTrendPrice > trendEmaValue;
                }
                else
                {
                    isUptrend[i] = true; // Default to true if not enough data
                }
            }
        }
        else
        {
            for (int i = 0; i < candles1m.Count; i++)
                isUptrend[i] = true;
        }

        decimal balance = initialBalance;
        int trades = 0, wins = 0;
        List<decimal> returns = new();

        // Precompute indicators on 1m
        decimal[] rsi = new decimal[candles1m.Count];
        decimal[] volumeSma = new decimal[candles1m.Count];
        decimal[] ema = new decimal[candles1m.Count];

        for (int i = rsiPeriod; i < candles1m.Count; i++)
            rsi[i] = CalculateRsi(candles1m, i, rsiPeriod);

        if (useVolumeFilter)
        {
            for (int i = volumePeriod; i < candles1m.Count; i++)
                volumeSma[i] = candles1m.Skip(i - volumePeriod).Take(volumePeriod).Average(c => c.Volume);
        }

        if (useEmaFilter)
        {
            decimal multiplier = 2m / (emaPeriod + 1);
            ema[0] = candles1m[0].Close;
            for (int i = 1; i < candles1m.Count; i++)
                ema[i] = (candles1m[i].Close - ema[i - 1]) * multiplier + ema[i - 1];
        }

        int logCount = 0;
        int totalMinutes = 0;
        int trendFail = 0, rsiFail = 0, volumeFail = 0, emaFail = 0;

        for (int i = Math.Max(rsiPeriod + volumePeriod + 10, 60); i < candles1m.Count; i++)
        {
            totalMinutes++;

            if (useTimeFilter)
            {
                int hour = candles1m[i].OpenTime.Hour;
                if (hour < 8 || hour > 18) continue;
            }

            if (!isUptrend[i])
            {
                trendFail++;
                continue;
            }

            bool oversold = rsi[i] < rsiOversold;
            bool volumeSpike = !useVolumeFilter || (volumeSma[i] > 0 && candles1m[i].Volume > volumeSma[i] * volumeMultiplier);
            bool aboveEma = !useEmaFilter || (candles1m[i].Close > ema[i]);

            if (!oversold) rsiFail++;
            if (!volumeSpike && useVolumeFilter) volumeFail++;
            if (!aboveEma && useEmaFilter) emaFail++;

            if (verbose && logCount < 30 && !(oversold && volumeSpike && aboveEma))
            {
                string reasons = "";
                if (!oversold) reasons += $"RSI={rsi[i]:F1} (<{rsiOversold} needed) ";
                if (!volumeSpike && useVolumeFilter) reasons += $"Vol={candles1m[i].Volume:F0} vs SMA={volumeSma[i]:F0} (>={volumeMultiplier}x needed) ";
                if (!aboveEma && useEmaFilter) reasons += $"Close={candles1m[i].Close:F2} < EMA={ema[i]:F2} ";
                if (string.IsNullOrEmpty(reasons)) reasons = "Unknown";

                Console.WriteLine($"    [{candles1m[i].OpenTime:HH:mm}] REJECT: {reasons}");
                logCount++;
            }

            if (oversold && volumeSpike && aboveEma)
            {
                decimal entryPrice = candles1m[i].Close;
                decimal stopPrice = entryPrice * (1 - stopLossPercent / 100m);
                decimal targetPrice = entryPrice * (1 + profitTargetPercent / 100m);
                decimal riskAmount = balance * riskPercent / 100m;
                decimal stopDistance = entryPrice - stopPrice;

                if (stopDistance <= 0) continue;

                decimal positionSize = riskAmount / stopDistance;
                bool hitStop = false, hitTarget = false;
                decimal exitPrice = 0;
                int exitIndex = i;

                for (int j = i + 1; j < candles1m.Count; j++)
                {
                    if (candles1m[j].Low <= stopPrice)
                    {
                        hitStop = true;
                        exitPrice = stopPrice;
                        exitIndex = j;
                        break;
                    }
                    if (candles1m[j].High >= targetPrice)
                    {
                        hitTarget = true;
                        exitPrice = targetPrice;
                        exitIndex = j;
                        break;
                    }
                }

                if (!hitStop && !hitTarget) continue;

                decimal pnl = (exitPrice - entryPrice) * positionSize;
                balance += pnl;
                trades++;
                if (pnl > 0) wins++;
                returns.Add(pnl / (entryPrice * positionSize) * 100m);

                Console.WriteLine($"    >>> {(hitTarget ? "WIN" : "LOSS")} at {candles1m[i].OpenTime:HH:mm} | {entryPrice:F2} -> {exitPrice:F2} | PnL {pnl:F2} | Bal {balance:F2}");

                i = exitIndex;
            }
        }

        double winRate = trades == 0 ? 0 : (double)wins / trades * 100;
        decimal totalReturn = (balance - initialBalance) / initialBalance * 100m;

        Console.WriteLine($"\n  STATISTICS:");
        Console.WriteLine($"    Minutes analyzed: {totalMinutes}");
        Console.WriteLine($"    Trend filter blocked: {trendFail} minutes");
        Console.WriteLine($"    RSI condition failed: {rsiFail} minutes");
        Console.WriteLine($"    Volume condition failed: {volumeFail} minutes");
        Console.WriteLine($"    EMA condition failed: {emaFail} minutes");
        Console.WriteLine($"\n  SUMMARY: {trades} trades | {wins} wins | {winRate:F1}% WR | Return {totalReturn:F2}%");

        return new BacktestResult
        {
            StrategyName = $"Scalping-{symbol}",
            StartingBalance = initialBalance,
            EndingBalance = balance,
            TotalTrades = trades,
            WinningTrades = wins,
            WinRate = winRate,
            TotalReturnPercent = totalReturn,
            AvgReturnPerTrade = trades == 0 ? 0 : returns.Average()
        };
    }

    private static BacktestResult CreateEmptyResult(string symbol, decimal balance)
    {
        return new BacktestResult
        {
            StrategyName = $"Scalping-{symbol}",
            StartingBalance = balance,
            EndingBalance = balance,
            TotalTrades = 0,
            WinningTrades = 0,
            WinRate = 0,
            TotalReturnPercent = 0,
            AvgReturnPerTrade = 0
        };
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
        KlineInterval.OneMinute => 1,
        KlineInterval.FiveMinutes => 5,
        KlineInterval.FifteenMinutes => 15,
        KlineInterval.ThirtyMinutes => 30,
        KlineInterval.OneHour => 60,
        KlineInterval.FourHour => 240,
        _ => 60
    };

    private static KlineInterval GetKlineInterval(string timeframe) => timeframe switch
    {
        "1m" => KlineInterval.OneMinute,
        "5m" => KlineInterval.FiveMinutes,
        "15m" => KlineInterval.FifteenMinutes,
        "30m" => KlineInterval.ThirtyMinutes,
        "1H" => KlineInterval.OneHour,
        "4H" => KlineInterval.FourHour,
        "1D" => KlineInterval.OneDay,
        _ => KlineInterval.OneHour
    };

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

        decimal rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }
}
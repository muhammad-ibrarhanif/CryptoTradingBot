using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Configuration;
using TradingBot.Core.Detection;
using TradingBot.Core.Engine;
using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public static class SwingStrategy
{
    public static async Task<BacktestResult> RunAsync(
        string symbol,
        DateTime start,
        DateTime end,
        decimal initialBalance = 10000m,
        decimal riskPercent = 1.0m,
        decimal minStopDistancePercent = 0.2m,
        decimal profitTargetMultiplier = 2.0m)
    {
        Console.WriteLine($"\n=== SWING BACKTEST (Simplified) ===");
        Console.WriteLine($"Period: {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
        Console.WriteLine($"Risk: {riskPercent}% | Min Stop Dist: {minStopDistancePercent}% | Profit Target: {profitTargetMultiplier}x stop");

        // Configuration (lowered thresholds to get trades)
        var config = new BotConfig
        {
            Phase = "PaperTrade",
            TradingPair = "SOL/USDT",
            EntryTimeframe = "30m",
            StructureTimeframe = "1H",
            RiskPerTradePct = (double)riskPercent,
            ZoneMinScore = 3,
            ZoneTradeMinScore = 3,
            MinConfluenceScore = 3,
            RsiEntryMax = 50,
            HardStopPct = 2.0,
            StopLossBufferPct = 0.1,
            BreakevenTriggerPct = 1.0,
            TimeStopCandles = 12,
            RsiExit1 = 50,
            RsiExit2 = 60,
            RsiExit3 = 70,
            RsiExit1Pct = 25,
            RsiExit2Pct = 25,
            RsiExit3Pct = 50,
            BinanceFeePercent = 0.1,
            SlippageBufferPct = 0.05,
            SwingLookback = 100,
            SwingConfirmCandles = 2,
            ZoneClusterPct = 0.3,
            FlipZoneExpiry = 50,
            FlipBreakMinPct = 0.2
        };

        // Fetch data
        var entryCandles = await FetchCandles(symbol, KlineInterval.ThirtyMinutes, start, end);
        var structureCandles = await FetchCandles(symbol, KlineInterval.OneHour, start, end);
        Console.WriteLine($"Entry candles: {entryCandles.Count}, Structure candles: {structureCandles.Count}");

        // Run backtest logic
        var result = RunBacktest(entryCandles, structureCandles, config, initialBalance, minStopDistancePercent, profitTargetMultiplier);
        return result;
    }

    private static BacktestResult RunBacktest(
        List<Candle> entryCandles,
        List<Candle> structureCandles,
        BotConfig config,
        decimal initialBalance,
        decimal minStopDistancePercent,
        decimal profitTargetMultiplier)
    {
        var swingDetector = new SwingPointDetector(config);
        var structureDetector = new MarketStructureDetector();
        var bosDetector = new BreakOfStructureDetector();
        var srDetector = new SupportResistanceDetector(config);
        var flipDetector = new FlipZoneDetector(config);
        var zoneTracker = new ZoneTracker(config);
        var entryEvaluator = new EntryEvaluator(config);
        var exitEvaluator = new ExitEvaluator(config);

        List<SupportResistanceZone> srZones = new();
        List<FlipZone> flipZones = new();
        List<Zone> unifiedZones = new();
        StructureSignal? currentSignal = null;
        MarketStructure currentStructure = new() { Trend = MarketTrend.Ranging };
        OpenPosition? position = null;
        List<CompletedTrade> trades = new();
        decimal balance = initialBalance;
        decimal peak = initialBalance;
        decimal maxDrawdown = 0m;

        int nextStructIdx = 0;
        int warmup = config.SwingLookback + config.SwingConfirmCandles;

        for (int entryIdx = 0; entryIdx < entryCandles.Count; entryIdx++)
        {
            var entryCandle = entryCandles[entryIdx];

            while (nextStructIdx < structureCandles.Count && structureCandles[nextStructIdx].CloseTime <= entryCandle.CloseTime)
            {
                var structCandle = structureCandles[nextStructIdx];
                var swings = swingDetector.DetectUpTo(structureCandles, nextStructIdx, config.StructureTimeframe);
                currentStructure = structureDetector.Detect(swings, structCandle.OpenTime);
                currentSignal = bosDetector.Detect(structureCandles, swings, nextStructIdx, currentSignal);

                var newSr = srDetector.BuildZones(structureCandles, swings, nextStructIdx);
                srZones = newSr.ToList();
                srDetector.UpdateZones(srZones, structureCandles, nextStructIdx);

                var newFlip = flipDetector.DetectBreaks(srZones, flipZones, structCandle, nextStructIdx);
                foreach (var fz in newFlip) flipZones.Add(fz);
                flipDetector.UpdateFlipZones(flipZones, structureCandles, nextStructIdx);

                unifiedZones = zoneTracker.BuildZones(srZones, flipZones)
                    .OrderBy(z => Math.Abs(z.Midpoint - entryCandle.Close) / z.Midpoint)
                    .ToList();

                nextStructIdx++;
            }

            if (nextStructIdx < warmup) continue;

            if (position == null)
            {
                var nearest = zoneTracker.GetTopNearestZones(unifiedZones, entryCandle.Close, 10);
                var eval = entryEvaluator.Evaluate(entryCandles, entryIdx,
                    currentSignal ?? new StructureSignal { BotState = BotStructureState.Active, Trend = MarketTrend.Ranging },
                    currentStructure, nearest, false);

                if (eval.ShouldEnter)
                {
                    decimal entryPrice = eval.EntryPrice!.Value;
                    decimal rawStop = eval.StopLossPrice!.Value;
                    decimal rawDist = entryPrice - rawStop;
                    decimal minDist = entryPrice * (minStopDistancePercent / 100m);
                    decimal finalDist = Math.Max(rawDist, minDist);
                    decimal finalStop = entryPrice - finalDist;
                    decimal riskAmount = balance * (decimal)config.RiskPerTradePct / 100m;
                    decimal size = riskAmount / finalDist;

                    position = new OpenPosition
                    {
                        EntryPrice = entryPrice,
                        StopLossPrice = finalStop,
                        EntryZone = eval.TriggeringZone!,
                        EntryCandleIndex = entryIdx,
                        EntryTime = entryCandle.OpenTime,
                        PositionSize = size,
                        RemainingFraction = 1.0m
                    };
                }
            }
            else
            {
                // Custom profit target
                decimal profitTarget = position.EntryPrice + (position.EntryPrice - position.StopLossPrice) * profitTargetMultiplier;
                if (entryCandle.High >= profitTarget)
                {
                    decimal pnl = exitEvaluator.CalculateNetPnl(position.EntryPrice, profitTarget, position.PositionSize, position.RemainingFraction);
                    balance += pnl;
                    trades.Add(new CompletedTrade
                    {
                        PositionId = position.Id,
                        EntryPrice = position.EntryPrice,
                        ExitPrice = profitTarget,
                        EntryTime = position.EntryTime,
                        ExitTime = entryCandle.OpenTime,
                        FractionClosed = position.RemainingFraction,
                        PositionSize = position.PositionSize,
                        NetPnl = pnl,
                        ExitReason = ExitReason.RsiCrossedExit1
                    });
                    position = null;
                    continue;
                }

                var exitEval = exitEvaluator.Evaluate(entryCandles, entryIdx, position,
                    currentSignal ?? new StructureSignal { BotState = BotStructureState.Active, Trend = MarketTrend.Ranging },
                    unifiedZones);
                if (exitEval.HasAction)
                {
                    decimal pnl = exitEvaluator.CalculateNetPnl(position.EntryPrice, exitEval.ExitPrice, position.PositionSize, exitEval.FractionToClose);
                    balance += pnl;
                    trades.Add(new CompletedTrade
                    {
                        PositionId = position.Id,
                        EntryPrice = position.EntryPrice,
                        ExitPrice = exitEval.ExitPrice,
                        EntryTime = position.EntryTime,
                        ExitTime = entryCandle.OpenTime,
                        FractionClosed = exitEval.FractionToClose,
                        PositionSize = position.PositionSize,
                        NetPnl = pnl,
                        ExitReason = exitEval.Reason ?? ExitReason.HardStopHit
                    });
                    if (exitEval.FullExit || exitEval.FractionToClose >= 0.999m)
                        position = null;
                    else
                        position.RemainingFraction -= exitEval.FractionToClose;
                }
            }

            if (balance > peak) peak = balance;
            decimal dd = (peak - balance) / peak * 100m;
            if (dd > maxDrawdown) maxDrawdown = dd;
        }

        if (position != null)
        {
            decimal finalPrice = entryCandles.Last().Close;
            decimal pnl = exitEvaluator.CalculateNetPnl(position.EntryPrice, finalPrice, position.PositionSize, position.RemainingFraction);
            balance += pnl;
            trades.Add(new CompletedTrade
            {
                PositionId = position.Id,
                EntryPrice = position.EntryPrice,
                ExitPrice = finalPrice,
                EntryTime = position.EntryTime,
                ExitTime = entryCandles.Last().OpenTime,
                FractionClosed = position.RemainingFraction,
                PositionSize = position.PositionSize,
                NetPnl = pnl,
                ExitReason = ExitReason.HardStopHit
            });
        }

        double winRate = trades.Count == 0 ? 0 : (double)trades.Count(t => t.NetPnl > 0) / trades.Count * 100;
        decimal totalReturn = (balance - initialBalance) / initialBalance * 100m;
        decimal avgReturn = trades.Count == 0 ? 0 : trades.Average(t => t.NetPnl / (t.EntryPrice * t.PositionSize) * 100m);

        return new BacktestResult
        {
            StrategyName = "Swing (Simplified)",
            StartingBalance = initialBalance,
            EndingBalance = balance,
            TotalTrades = trades.Count,
            WinningTrades = trades.Count(t => t.NetPnl > 0),
            WinRate = winRate,
            TotalReturnPercent = totalReturn,
            AvgReturnPerTrade = avgReturn
        };
    }

    private static async Task<List<Candle>> FetchCandles(string symbol, KlineInterval interval, DateTime start, DateTime end)
    {
        using var client = new BinanceRestClient();
        var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, interval, start, end);
        if (!result.Success) throw new Exception($"Failed to fetch {symbol} {interval}: {result.Error}");
        return result.Data.Select(k => new Candle
        {
            OpenTime = k.OpenTime,
            Open = k.OpenPrice,
            High = k.HighPrice,
            Low = k.LowPrice,
            Close = k.ClosePrice,
            Volume = k.Volume,
            CloseTime = k.CloseTime
        }).ToList();
    }
}
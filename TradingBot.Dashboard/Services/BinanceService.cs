using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Core.Indicators;
using TradingBot.Dashboard.Models;

namespace TradingBot.Dashboard.Services;

public class BinanceService
{
    private readonly BinanceRestClient _client;

    public BinanceService()
    {
        _client = new BinanceRestClient();
    }

    // ============ BACKTEST METHODS ============

    public async Task<BacktestMetrics> RunBacktestAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var metrics = new BacktestMetrics();
        var historicalSignals = new List<HistoricalSignal>();
        var patternStats = new Dictionary<string, (int total, int wins, decimal totalPnL)>();

        var candles5M = await GetHistoricalCandlesAsync(symbol, "5m", startDate, endDate);

        if (candles5M.Count < 100)
        {
            Console.WriteLine($"Insufficient data for backtest. Got {candles5M.Count} candles");
            return metrics;
        }

        Console.WriteLine($"Backtesting {candles5M.Count} 5M candles from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

        for (int i = 60; i < candles5M.Count - 24; i++)
        {
            var currentCandles = candles5M.Take(i + 1).ToList();
            var analysis = AnalyzeTimeframe(currentCandles, "5M");

            var lastCandle = candles5M[i];
            var nearSupport = analysis.NearSupport;
            var nearResistance = analysis.NearResistance;
            var pattern = analysis.Pattern.ToUpper();

            bool isBuySignal = (pattern.Contains("HAMMER") || pattern.Contains("DOJI") ||
                               pattern.Contains("BULLISH ENGULFING") || pattern.Contains("MORNING STAR") ||
                               pattern.Contains("TWEEZERS BOTTOM")) && nearSupport;

            bool isSellSignal = (pattern.Contains("SHOOTING STAR") || pattern.Contains("EVENING STAR") ||
                                pattern.Contains("BEARISH ENGULFING") || pattern.Contains("TWEEZERS TOP")) && nearResistance;

            if (isBuySignal || isSellSignal)
            {
                var entryPrice = lastCandle.Close;
                var patternName = ExtractPatternName(analysis.Pattern);

                if (!patternStats.ContainsKey(patternName))
                    patternStats[patternName] = (0, 0, 0);

                decimal targetPrice = isBuySignal ? analysis.Resistance : analysis.Support;
                decimal stopPrice = isBuySignal ? analysis.Support * 0.995m : analysis.Resistance * 1.005m;

                decimal exitPrice = entryPrice;
                bool hitTarget = false;
                bool hitStop = false;
                int exitIndex = i;

                for (int j = i + 1; j < Math.Min(i + 48, candles5M.Count); j++)
                {
                    if (isBuySignal && candles5M[j].High >= targetPrice)
                    {
                        exitPrice = targetPrice;
                        hitTarget = true;
                        exitIndex = j;
                        break;
                    }
                    if (isSellSignal && candles5M[j].Low <= targetPrice)
                    {
                        exitPrice = targetPrice;
                        hitTarget = true;
                        exitIndex = j;
                        break;
                    }
                    if (isBuySignal && candles5M[j].Low <= stopPrice)
                    {
                        exitPrice = stopPrice;
                        hitStop = true;
                        exitIndex = j;
                        break;
                    }
                    if (isSellSignal && candles5M[j].High >= stopPrice)
                    {
                        exitPrice = stopPrice;
                        hitStop = true;
                        exitIndex = j;
                        break;
                    }
                }

                if (!hitTarget && !hitStop && exitIndex == i)
                {
                    exitPrice = candles5M[Math.Min(i + 48, candles5M.Count - 1)].Close;
                }

                decimal pnlPercent = isBuySignal ? (exitPrice - entryPrice) / entryPrice * 100m : (entryPrice - exitPrice) / entryPrice * 100m;
                bool isWinner = pnlPercent > 0;

                historicalSignals.Add(new HistoricalSignal
                {
                    Time = lastCandle.OpenTime,
                    Type = isBuySignal ? "BUY" : "SELL",
                    Pattern = patternName,
                    EntryPrice = entryPrice,
                    ExitPrice = exitPrice,
                    PnL = pnlPercent,
                    PnLPercent = pnlPercent,
                    IsWinner = isWinner,
                    Timeframe = "5M",
                    Reason = isBuySignal ? $"{patternName} at support {analysis.Support:F4}" : $"{patternName} at resistance {analysis.Resistance:F4}"
                });

                var stats = patternStats[patternName];
                patternStats[patternName] = (stats.total + 1, stats.wins + (isWinner ? 1 : 0), stats.totalPnL + pnlPercent);

                i = exitIndex + 12;
            }
        }

        var winners = historicalSignals.Where(s => s.IsWinner).ToList();
        var losers = historicalSignals.Where(s => !s.IsWinner).ToList();

        decimal totalPnL = historicalSignals.Sum(s => s.PnL);
        decimal totalWins = winners.Sum(w => w.PnL);
        decimal totalLosses = losers.Sum(l => Math.Abs(l.PnL));

        var bestPattern = patternStats.OrderByDescending(p => p.Value.wins / (double)p.Value.total).FirstOrDefault();

        metrics.TotalSignals = historicalSignals.Count;
        metrics.TotalTrades = historicalSignals.Count;
        metrics.WinningTrades = winners.Count;
        metrics.LosingTrades = losers.Count;
        metrics.WinRate = metrics.TotalTrades > 0 ? (double)winners.Count / metrics.TotalTrades * 100 : 0;
        metrics.TotalPnL = totalPnL;
        metrics.TotalPnLPercent = totalPnL;
        metrics.AvgWin = winners.Count > 0 ? totalWins / winners.Count : 0;
        metrics.AvgLoss = losers.Count > 0 ? totalLosses / losers.Count : 0;
        metrics.ProfitFactor = totalLosses > 0 ? totalWins / totalLosses : totalWins > 0 ? 999 : 0;
        metrics.BestPattern = bestPattern.Key ?? "None";
        metrics.BestPatternWins = bestPattern.Value.wins;

        Console.WriteLine($"Backtest complete: {metrics.TotalTrades} trades, Win Rate: {metrics.WinRate:F1}%, Total P&L: {metrics.TotalPnL:F2}%");

        return metrics;
    }

    public async Task<(BacktestMetrics Metrics, List<HistoricalSignal> Signals)> Run1MinuteScalpingBacktestAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var metrics = new BacktestMetrics();
        var historicalSignals = new List<HistoricalSignal>();
        var patternStats = new Dictionary<string, (int total, int wins, decimal totalPnL)>();

        var candles1M = await Get1MinuteCandlesAsync(symbol, startDate, endDate);
        var candles15M = await GetHistoricalCandlesAsync(symbol, "15m", startDate.AddDays(-3), endDate);
        var candles1H = await GetHistoricalCandlesAsync(symbol, "1H", startDate.AddDays(-7), endDate);

        if (candles1M.Count < 500)
        {
            Console.WriteLine($"Insufficient 1M data: {candles1M.Count} candles");
            return (metrics, historicalSignals);
        }

        Console.WriteLine($"1M Scalping Backtest: {candles1M.Count} candles from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

        const decimal targetPercent = 0.15m;
        const decimal stopPercent = 0.1m;
        const int maxHoldMinutes = 10;

        int startIndex = 0;
        for (int i = 0; i < candles1M.Count; i++)
        {
            if (candles1M[i].OpenTime >= startDate)
            {
                startIndex = i;
                break;
            }
        }

        for (int i = startIndex + 30; i < candles1M.Count - maxHoldMinutes; i++)
        {
            var analysisCandles1M = candles1M.Skip(Math.Max(0, i - 30)).Take(30).ToList();
            var analysis1M = AnalyzeTimeframe(analysisCandles1M, "1M");

            var relevant15M = candles15M.Where(c => c.OpenTime <= candles1M[i].OpenTime).ToList();
            var analysis15M = AnalyzeTimeframe(relevant15M, "15M");

            var relevant1H = candles1H.Where(c => c.OpenTime <= candles1M[i].OpenTime).ToList();
            var analysis1H = AnalyzeTimeframe(relevant1H, "1H");

            var currentCandle = candles1M[i];
            var pattern = analysis1M.Pattern.ToUpper();
            var nearSupport = analysis1M.NearSupport;
            var nearResistance = analysis1M.NearResistance;

            bool isUptrend = analysis15M.Trend.Contains("UPTREND") || analysis1H.Trend.Contains("UPTREND");
            bool isDowntrend = analysis15M.Trend.Contains("DOWNTREND") || analysis1H.Trend.Contains("DOWNTREND");

            bool isBuy = (pattern.Contains("HAMMER") || pattern.Contains("TWEEZERS BOTTOM") || pattern.Contains("BULLISH ENGULFING")) && nearSupport && isUptrend;
            bool isSell = (pattern.Contains("SHOOTING STAR") || pattern.Contains("TWEEZERS TOP") || pattern.Contains("BEARISH ENGULFING")) && nearResistance && isDowntrend;

            if (isBuy || isSell)
            {
                decimal entryPrice = currentCandle.Close;
                decimal targetPrice = isBuy ? entryPrice * (1 + targetPercent / 100m) : entryPrice * (1 - targetPercent / 100m);
                decimal stopPrice = isBuy ? entryPrice * (1 - stopPercent / 100m) : entryPrice * (1 + stopPercent / 100m);

                string patternName = ExtractPatternName(analysis1M.Pattern);

                decimal exitPrice = entryPrice;
                bool hitTarget = false;
                bool hitStop = false;
                int exitIndex = i;

                for (int j = i + 1; j < Math.Min(i + maxHoldMinutes, candles1M.Count); j++)
                {
                    if (isBuy && candles1M[j].High >= targetPrice)
                    {
                        exitPrice = targetPrice;
                        hitTarget = true;
                        exitIndex = j;
                        break;
                    }
                    if (isSell && candles1M[j].Low <= targetPrice)
                    {
                        exitPrice = targetPrice;
                        hitTarget = true;
                        exitIndex = j;
                        break;
                    }
                    if (isBuy && candles1M[j].Low <= stopPrice)
                    {
                        exitPrice = stopPrice;
                        hitStop = true;
                        exitIndex = j;
                        break;
                    }
                    if (isSell && candles1M[j].High >= stopPrice)
                    {
                        exitPrice = stopPrice;
                        hitStop = true;
                        exitIndex = j;
                        break;
                    }
                }

                if (!hitTarget && !hitStop)
                {
                    exitPrice = candles1M[Math.Min(i + maxHoldMinutes, candles1M.Count - 1)].Close;
                }

                decimal pnlPercent = isBuy ? (exitPrice - entryPrice) / entryPrice * 100m : (entryPrice - exitPrice) / entryPrice * 100m;
                bool isWinner = pnlPercent > 0;

                historicalSignals.Add(new HistoricalSignal
                {
                    Time = currentCandle.OpenTime,
                    Type = isBuy ? "BUY" : "SELL",
                    Pattern = patternName,
                    EntryPrice = entryPrice,
                    ExitPrice = exitPrice,
                    PnL = pnlPercent,
                    PnLPercent = pnlPercent,
                    IsWinner = isWinner,
                    Timeframe = "1M",
                    Reason = isBuy ? $"{patternName} at support {analysis1M.Support:F4}" : $"{patternName} at resistance {analysis1M.Resistance:F4}"
                });

                if (!patternStats.ContainsKey(patternName))
                    patternStats[patternName] = (0, 0, 0);
                var stats = patternStats[patternName];
                patternStats[patternName] = (stats.total + 1, stats.wins + (isWinner ? 1 : 0), stats.totalPnL + pnlPercent);

                i = exitIndex + 5;
            }
        }

        var winners = historicalSignals.Where(s => s.IsWinner).ToList();
        var losers = historicalSignals.Where(s => !s.IsWinner).ToList();

        decimal totalPnL = historicalSignals.Sum(s => s.PnL);
        decimal totalWins = winners.Sum(w => w.PnL);
        decimal totalLosses = losers.Sum(l => Math.Abs(l.PnL));

        var bestPattern = patternStats.OrderByDescending(p => p.Value.wins / (double)p.Value.total).FirstOrDefault();

        metrics.TotalSignals = historicalSignals.Count;
        metrics.TotalTrades = historicalSignals.Count;
        metrics.WinningTrades = winners.Count;
        metrics.LosingTrades = losers.Count;
        metrics.WinRate = metrics.TotalTrades > 0 ? (double)winners.Count / metrics.TotalTrades * 100 : 0;
        metrics.TotalPnL = totalPnL;
        metrics.TotalPnLPercent = totalPnL;
        metrics.AvgWin = winners.Count > 0 ? totalWins / winners.Count : 0;
        metrics.AvgLoss = losers.Count > 0 ? totalLosses / losers.Count : 0;
        metrics.ProfitFactor = totalLosses > 0 ? totalWins / totalLosses : totalWins > 0 ? 999 : 0;
        metrics.BestPattern = bestPattern.Key ?? "None";
        metrics.BestPatternWins = bestPattern.Value.wins;

        Console.WriteLine($"1M Scalping Backtest complete: {metrics.TotalTrades} trades, Win Rate: {metrics.WinRate:F1}%, Total P&L: {metrics.TotalPnL:F2}%");

        return (metrics, historicalSignals);
    }

    private string ExtractPatternName(string pattern)
    {
        if (pattern.Contains("HAMMER")) return "Hammer";
        if (pattern.Contains("ENGULFING")) return "Engulfing";
        if (pattern.Contains("MORNING STAR")) return "Morning Star";
        if (pattern.Contains("EVENING STAR")) return "Evening Star";
        if (pattern.Contains("SHOOTING STAR")) return "Shooting Star";
        if (pattern.Contains("DOJI")) return "Doji";
        if (pattern.Contains("TWEEZERS BOTTOM")) return "Tweezers Bottom";
        if (pattern.Contains("TWEEZERS TOP")) return "Tweezers Top";
        if (pattern.Contains("THREE BLACK CROWS")) return "Three Black Crows";
        if (pattern.Contains("THREE WHITE SOLDIERS")) return "Three White Soldiers";
        if (pattern.Contains("PIERCING")) return "Piercing";
        if (pattern.Contains("DARK CLOUD")) return "Dark Cloud";
        if (pattern.Contains("HARAMI")) return "Harami";
        return "Pattern";
    }

    // ============ DATA FETCHING METHODS ============


    public async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int hours)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-hours);
        var klineInterval = GetKlineInterval(interval);

        // Validate dates
        if (startTime >= endTime)
            return new List<Candle>();

        using var client = new BinanceRestClient();

        var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, startTime, endTime, limit: 500);

        if (!result.Success || result.Data == null)
            return new List<Candle>();

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

    //public async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int hours)
    //{
    //    var endTime = DateTime.UtcNow;
    //    var startTime = endTime.AddHours(-hours);
    //    var klineInterval = GetKlineInterval(interval);

    //    var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, startTime, endTime, limit: 500);

    //    if (!result.Success || result.Data == null)
    //        return new List<Candle>();

    //    return result.Data.Select(k => new Candle
    //    {
    //        OpenTime = k.OpenTime,
    //        Open = k.OpenPrice,
    //        High = k.HighPrice,
    //        Low = k.LowPrice,
    //        Close = k.ClosePrice,
    //        Volume = k.Volume,
    //        CloseTime = k.CloseTime
    //    }).ToList();
    //}

    public async Task<List<Candle>> GetHistoricalCandlesAsync(string symbol, string interval, DateTime start, DateTime end)
    {
        var klineInterval = GetKlineInterval(interval);
        var allCandles = new List<Candle>();
        var currentStart = start;

        // Ensure we don't go beyond end date
        if (currentStart >= end)
            return allCandles;

        using var client = new BinanceRestClient();

        while (currentStart < end)
        {
            // Calculate remaining time
            var remaining = end - currentStart;
            var requestEnd = currentStart.AddMinutes(GetMinutesForInterval(klineInterval) * 500);

            if (requestEnd > end)
                requestEnd = end;

            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, currentStart, requestEnd, limit: 500);

            if (!result.Success || result.Data == null || !result.Data.Any())
                break;

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

            if (candles.Count == 0)
                break;

            allCandles.AddRange(candles);

            // Move to next batch using the last candle's time + 1 interval
            var lastCandleTime = candles.Last().OpenTime;
            currentStart = lastCandleTime.AddMinutes(GetMinutesForInterval(klineInterval));

            // Prevent infinite loop
            if (currentStart >= end)
                break;

            await Task.Delay(100);
        }

        return allCandles;
    }

    //public async Task<List<Candle>> GetHistoricalCandlesAsync(string symbol, string interval, DateTime start, DateTime end)
    //{
    //    var klineInterval = GetKlineInterval(interval);
    //    var allCandles = new List<Candle>();
    //    var currentStart = start;

    //    while (currentStart < end)
    //    {
    //        var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, currentStart, end, limit: 1000);
    //        if (!result.Success || result.Data == null || !result.Data.Any())
    //            break;

    //        var candles = result.Data.Select(k => new Candle
    //        {
    //            OpenTime = k.OpenTime,
    //            Open = k.OpenPrice,
    //            High = k.HighPrice,
    //            Low = k.LowPrice,
    //            Close = k.ClosePrice,
    //            Volume = k.Volume,
    //            CloseTime = k.CloseTime
    //        }).ToList();

    //        allCandles.AddRange(candles);

    //        if (candles.Count < 1000)
    //            break;

    //        currentStart = candles.Last().OpenTime.AddMinutes(GetMinutesForInterval(klineInterval));
    //        await Task.Delay(100);
    //    }

    //    return allCandles;
    //}


    public async Task<List<Candle>> Get1MinuteCandlesAsync(string symbol, DateTime start, DateTime end)
    {
        var allCandles = new List<Candle>();
        var currentStart = start;

        if (currentStart >= end)
            return allCandles;

        using var client = new BinanceRestClient();

        while (currentStart < end)
        {
            var requestEnd = currentStart.AddHours(6); // 6 hours at a time
            if (requestEnd > end)
                requestEnd = end;

            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.OneMinute, currentStart, requestEnd, limit: 1000);

            if (!result.Success || result.Data == null || !result.Data.Any())
                break;

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

            if (candles.Count == 0)
                break;

            allCandles.AddRange(candles);

            // Move to next batch
            currentStart = candles.Last().OpenTime.AddMinutes(1);

            if (currentStart >= end)
                break;

            await Task.Delay(100);
        }

        return allCandles;
    }


    //public async Task<List<Candle>> Get1MinuteCandlesAsync(string symbol, DateTime start, DateTime end)
    //{
    //    var allCandles = new List<Candle>();
    //    var currentStart = start;

    //    while (currentStart < end)
    //    {
    //        var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.OneMinute, currentStart, end, limit: 1000);
    //        if (!result.Success || result.Data == null || !result.Data.Any())
    //            break;

    //        var candles = result.Data.Select(k => new Candle
    //        {
    //            OpenTime = k.OpenTime,
    //            Open = k.OpenPrice,
    //            High = k.HighPrice,
    //            Low = k.LowPrice,
    //            Close = k.ClosePrice,
    //            Volume = k.Volume,
    //            CloseTime = k.CloseTime
    //        }).ToList();

    //        allCandles.AddRange(candles);

    //        if (candles.Count < 1000)
    //            break;

    //        currentStart = candles.Last().OpenTime.AddMinutes(1);
    //        await Task.Delay(100);
    //    }

    //    return allCandles;
    //}

    // ============ ANALYSIS METHODS ============

    public AnalysisResult AnalyzeTimeframe(List<Candle> candles, string timeframe)
    {
        var result = new AnalysisResult();

        if (candles == null || candles.Count < 20)
        {
            result.Trend = "Insufficient data";
            result.Support = 0;
            result.Resistance = 0;
            result.Pattern = "Not enough candles";
            return result;
        }

        var lastCandle = candles.Last();
        var prevCandle = candles.Count >= 2 ? candles[candles.Count - 2] : null;
        var twoBack = candles.Count >= 3 ? candles[candles.Count - 3] : null;
        var threeBack = candles.Count >= 4 ? candles[candles.Count - 4] : null;

        // ============ INDICATORS ============

        // EMA20 for trend
        decimal ema20 = 0;
        if (candles.Count >= 20)
        {
            ema20 = EmaCalculator.Calculate(candles, candles.Count - 1, 20);
        }

        // RSI for momentum
        decimal rsi = 50m;
        if (candles.Count >= 15)
        {
            rsi = RsiCalculator.Calculate(candles, 14, candles.Count - 1);
        }

        // Heikin Ashi for clear trend signal
        var haCandles = HeikinAshiCalculator.Calculate(candles);
        var lastHa = haCandles.Last();
        var prevHa = haCandles.Count >= 2 ? haCandles[haCandles.Count - 2] : null;

        // ============ TREND DETECTION (Using Heikin Ashi + EMA + RSI) ============

        bool priceBelowEma = lastCandle.Close < ema20;
        bool priceAboveEma = lastCandle.Close > ema20;
        bool haBearish = lastHa.IsBearish;
        bool haBullish = lastHa.IsBullish;
        bool rsiBearish = rsi < 50;
        bool rsiBullish = rsi > 50;

        // Count consecutive Heikin Ashi candles in same direction
        int consecutiveHaBearish = 0;
        int consecutiveHaBullish = 0;
        for (int i = haCandles.Count - 1; i >= 0 && i >= haCandles.Count - 5; i--)
        {
            if (haCandles[i].IsBearish)
                consecutiveHaBearish++;
            else
                break;
        }
        for (int i = haCandles.Count - 1; i >= 0 && i >= haCandles.Count - 5; i--)
        {
            if (haCandles[i].IsBullish)
                consecutiveHaBullish++;
            else
                break;
        }

        // Downtrend conditions (matches TradingView with Heikin Ashi)
        bool isStrongDowntrend = haBearish && priceBelowEma && rsiBearish && consecutiveHaBearish >= 2;
        bool isWeakDowntrend = haBearish && (priceBelowEma || rsiBearish);

        // Uptrend conditions
        bool isStrongUptrend = haBullish && priceAboveEma && rsiBullish && consecutiveHaBullish >= 2;
        bool isWeakUptrend = haBullish && (priceAboveEma || rsiBullish);

        // Determine trend
        if (isStrongDowntrend)
        {
            result.Trend = "📉 STRONG DOWNTREND";
        }
        else if (isWeakDowntrend)
        {
            result.Trend = "📉 DOWNTREND";
        }
        else if (isStrongUptrend)
        {
            result.Trend = "📈 STRONG UPTREND";
        }
        else if (isWeakUptrend)
        {
            result.Trend = "📈 UPTREND";
        }
        else
        {
            result.Trend = "➡️ RANGING";
        }

        // Store indicator values for display
        result.Ema20 = ema20;
        result.Rsi = rsi;
        result.HeikinAshiColor = lastHa.IsBullish ? "🟢 GREEN" : "🔴 RED";
        result.ConsecutiveHaDirection = lastHa.IsBearish ? consecutiveHaBearish : consecutiveHaBullish;

        // ============ SUPPORT/RESISTANCE ============
        var lows = candles.Skip(Math.Max(0, candles.Count - 20)).Select(c => c.Low).ToList();
        var highs = candles.Skip(Math.Max(0, candles.Count - 20)).Select(c => c.High).ToList();

        result.Support = lows.Min();
        result.Resistance = highs.Max();

        // Ensure support is lower than resistance
        if (result.Support >= result.Resistance)
        {
            result.Support = lows.Take(10).Min();
            result.Resistance = highs.Take(10).Max();
        }

        result.NearSupport = Math.Abs(lastCandle.Low - result.Support) / result.Support < 0.005m;
        result.NearResistance = Math.Abs(lastCandle.High - result.Resistance) / result.Resistance < 0.005m;

        // ============ PATTERN DETECTION ============
        var body = lastCandle.Body;
        var range = lastCandle.High - lastCandle.Low;
        var lowerWick = lastCandle.LowerWick;
        var upperWick = lastCandle.UpperWick;

        if (range == 0)
        {
            result.Pattern = "No movement";
            return result;
        }

        // ============ SINGLE CANDLE PATTERNS ============

        // Hammer (bullish reversal)
        if (lowerWick > body * 2 && body > 0 && lastCandle.IsBullish)
        {
            if (result.NearSupport)
                result.Pattern = "🔨 HAMMER (AT SUPPORT!) - Bullish reversal";
            else
                result.Pattern = "🔨 HAMMER - Bullish reversal";
        }
        // Inverted Hammer (bullish reversal after drop)
        else if (upperWick > body * 2 && body > 0 && lastCandle.IsBullish && upperWick > lowerWick)
        {
            if (result.NearSupport)
                result.Pattern = "⚡ INVERTED HAMMER (AT SUPPORT!) - Bullish reversal";
            else
                result.Pattern = "⚡ INVERTED HAMMER - Potential bullish reversal";
        }
        // Shooting Star (bearish reversal)
        else if (upperWick > body * 2 && body > 0 && lastCandle.IsBearish && upperWick > lowerWick)
        {
            if (result.NearResistance)
                result.Pattern = "💫 SHOOTING STAR (AT RESISTANCE!) - Bearish reversal";
            else
                result.Pattern = "💫 SHOOTING STAR - Bearish reversal";
        }
        // Hanging Man (bearish reversal after uptrend)
        else if (lowerWick > body * 2 && body > 0 && lastCandle.IsBearish && lowerWick > upperWick)
        {
            if (result.NearResistance)
                result.Pattern = "🪢 HANGING MAN (AT RESISTANCE!) - Bearish reversal";
            else
                result.Pattern = "🪢 HANGING MAN - Potential bearish reversal";
        }
        // Doji (indecision)
        else if (body < range * 0.1m && range > 0)
        {
            if (result.NearSupport)
                result.Pattern = "✚ DOJI (AT SUPPORT) - Possible bounce UP";
            else if (result.NearResistance)
                result.Pattern = "✚ DOJI (AT RESISTANCE) - Possible bounce DOWN";
            else
                result.Pattern = "✚ DOJI - Market indecision";
        }
        // Spinning Top (neutral)
        else if (body < range * 0.3m && body > 0)
        {
            result.Pattern = "🌀 SPINNING TOP - Neutral, wait for confirmation";
        }
        // Marubozu (strong momentum)
        else if (upperWick < body * 0.1m && lowerWick < body * 0.1m && body > 0)
        {
            if (lastCandle.IsBullish)
                result.Pattern = "🟢 BULLISH MARUBOZU - Strong buying pressure";
            else
                result.Pattern = "🔴 BEARISH MARUBOZU - Strong selling pressure";
        }

        // ============ TWO CANDLE PATTERNS ============

        else if (prevCandle != null)
        {
            // Bullish Engulfing
            if (prevCandle.IsBearish && lastCandle.IsBullish &&
                lastCandle.Open < prevCandle.Close && lastCandle.Close > prevCandle.Open)
            {
                if (result.NearSupport)
                    result.Pattern = "🟢 BULLISH ENGULFING (AT SUPPORT!) - STRONG BUY";
                else
                    result.Pattern = "🟢 BULLISH ENGULFING - Strong reversal signal";
            }
            // Bearish Engulfing
            else if (prevCandle.IsBullish && lastCandle.IsBearish &&
                     lastCandle.Open > prevCandle.Close && lastCandle.Close < prevCandle.Open)
            {
                if (result.NearResistance)
                    result.Pattern = "🔴 BEARISH ENGULFING (AT RESISTANCE!) - STRONG SELL";
                else
                    result.Pattern = "🔴 BEARISH ENGULFING - Strong reversal signal";
            }
            // Bullish Harami (pregnant woman pattern)
            else if (prevCandle.IsBearish && lastCandle.IsBullish &&
                     lastCandle.Open > prevCandle.Close && lastCandle.Close < prevCandle.Open &&
                     lastCandle.Body < prevCandle.Body * 0.5m)
            {
                if (result.NearSupport)
                    result.Pattern = "🤰 BULLISH HARAMI (AT SUPPORT!) - Potential reversal UP";
                else
                    result.Pattern = "🤰 BULLISH HARAMI - Potential bullish reversal";
            }
            // Bearish Harami
            else if (prevCandle.IsBullish && lastCandle.IsBearish &&
                     lastCandle.Open < prevCandle.Close && lastCandle.Close > prevCandle.Open &&
                     lastCandle.Body < prevCandle.Body * 0.5m)
            {
                if (result.NearResistance)
                    result.Pattern = "🤰 BEARISH HARAMI (AT RESISTANCE!) - Potential reversal DOWN";
                else
                    result.Pattern = "🤰 BEARISH HARAMI - Potential bearish reversal";
            }
            // Piercing Pattern
            else if (prevCandle.IsBearish && lastCandle.IsBullish &&
                     lastCandle.Open < prevCandle.Low &&
                     lastCandle.Close > (prevCandle.Open + prevCandle.Close) / 2 &&
                     lastCandle.Close < prevCandle.Open)
            {
                if (result.NearSupport)
                    result.Pattern = "📌 PIERCING PATTERN (AT SUPPORT!) - Bullish reversal";
                else
                    result.Pattern = "📌 PIERCING PATTERN - Bullish reversal";
            }
            // Dark Cloud Cover
            else if (prevCandle.IsBullish && lastCandle.IsBearish &&
                     lastCandle.Open > prevCandle.High &&
                     lastCandle.Close < (prevCandle.Open + prevCandle.Close) / 2 &&
                     lastCandle.Close > prevCandle.Open)
            {
                if (result.NearResistance)
                    result.Pattern = "☁️ DARK CLOUD COVER (AT RESISTANCE!) - Bearish reversal";
                else
                    result.Pattern = "☁️ DARK CLOUD COVER - Bearish reversal";
            }
            // Tweezers Bottom
            else if (Math.Abs(lastCandle.Low - prevCandle.Low) / lastCandle.Low < 0.001m && lastCandle.IsBullish)
            {
                if (result.NearSupport)
                    result.Pattern = "✂️ TWEEZERS BOTTOM (AT SUPPORT!) - Double bottom BUY";
                else
                    result.Pattern = "✂️ TWEEZERS BOTTOM - Support holding";
            }
            // Tweezers Top
            else if (Math.Abs(lastCandle.High - prevCandle.High) / lastCandle.High < 0.001m && lastCandle.IsBearish)
            {
                if (result.NearResistance)
                    result.Pattern = "✂️ TWEEZERS TOP (AT RESISTANCE!) - Double top SELL";
                else
                    result.Pattern = "✂️ TWEEZERS TOP - Resistance holding";
            }
        }

        // ============ THREE CANDLE PATTERNS ============

        else if (twoBack != null && prevCandle != null)
        {
            // Morning Star (bullish reversal)
            if (twoBack.IsBearish &&
                prevCandle.Body < (prevCandle.High - prevCandle.Low) * 0.3m &&
                lastCandle.IsBullish &&
                lastCandle.Close > twoBack.High)
            {
                if (result.NearSupport)
                    result.Pattern = "⭐ MORNING STAR (AT SUPPORT!) - STRONG BUY ★★★";
                else
                    result.Pattern = "⭐ MORNING STAR - Strong bullish reversal";
            }
            // Evening Star (bearish reversal)
            else if (twoBack.IsBullish &&
                     prevCandle.Body < (prevCandle.High - prevCandle.Low) * 0.3m &&
                     lastCandle.IsBearish &&
                     lastCandle.Close < twoBack.Low)
            {
                if (result.NearResistance)
                    result.Pattern = "🌙 EVENING STAR (AT RESISTANCE!) - STRONG SELL ★★★";
                else
                    result.Pattern = "🌙 EVENING STAR - Strong bearish reversal";
            }
            // Three White Soldiers (strong bullish continuation)
            else if (twoBack.IsBullish && prevCandle.IsBullish && lastCandle.IsBullish &&
                     lastCandle.Close > prevCandle.Close && prevCandle.Close > twoBack.Close &&
                     lastCandle.Open > prevCandle.Open && prevCandle.Open > twoBack.Open)
            {
                result.Pattern = "⚪⚪⚪ THREE WHITE SOLDIERS - Strong bullish continuation";
            }
            // Three Black Crows (strong bearish continuation)
            else if (twoBack.IsBearish && prevCandle.IsBearish && lastCandle.IsBearish &&
                     lastCandle.Close < prevCandle.Close && prevCandle.Close < twoBack.Close &&
                     lastCandle.Open < prevCandle.Open && prevCandle.Open < twoBack.Open)
            {
                result.Pattern = "🐦‍⬛🐦‍⬛🐦‍⬛ THREE BLACK CROWS - Strong bearish continuation";
            }
            // Abandoned Baby (rare, strong reversal)
            else if (twoBack.IsBearish &&
                     prevCandle.Body < (prevCandle.High - prevCandle.Low) * 0.1m &&
                     lastCandle.IsBullish &&
                     prevCandle.Low < twoBack.Low && prevCandle.Low < lastCandle.Low)
            {
                if (result.NearSupport)
                    result.Pattern = "👶 ABANDONED BABY (AT SUPPORT!) - Rare strong BUY";
                else
                    result.Pattern = "👶 ABANDONED BABY - Rare strong reversal signal";
            }
        }

        // ============ FOUR CANDLE PATTERNS ============

        else if (threeBack != null && twoBack != null && prevCandle != null)
        {
            // Three Inside Up
            if (threeBack.IsBearish && twoBack.IsBullish && twoBack.Body < threeBack.Body * 0.5m &&
                prevCandle.IsBullish && prevCandle.Close > threeBack.High && lastCandle.IsBullish)
            {
                if (result.NearSupport)
                    result.Pattern = "📈 THREE INSIDE UP (AT SUPPORT!) - Bullish reversal confirmed";
                else
                    result.Pattern = "📈 THREE INSIDE UP - Bullish reversal confirmed";
            }
            // Three Inside Down
            else if (threeBack.IsBullish && twoBack.IsBearish && twoBack.Body < threeBack.Body * 0.5m &&
                     prevCandle.IsBearish && prevCandle.Close < threeBack.Low && lastCandle.IsBearish)
            {
                if (result.NearResistance)
                    result.Pattern = "📉 THREE INSIDE DOWN (AT RESISTANCE!) - Bearish reversal confirmed";
                else
                    result.Pattern = "📉 THREE INSIDE DOWN - Bearish reversal confirmed";
            }
        }

        // ============ DEFAULT ============

        else
        {
            result.Pattern = lastCandle.IsBullish ? "🟢 Bullish candle" : "🔴 Bearish candle";
        }

        return result;
    }

    // ============ HELPER METHODS ============

    private KlineInterval GetKlineInterval(string interval) => interval switch
    {
        "1H" => KlineInterval.OneHour,
        "15m" => KlineInterval.FifteenMinutes,
        "5m" => KlineInterval.FiveMinutes,
        "1m" => KlineInterval.OneMinute,
        _ => KlineInterval.OneHour
    };

    private int GetMinutesForInterval(KlineInterval interval) => interval switch
    {
        KlineInterval.OneMinute => 1,
        KlineInterval.FiveMinutes => 5,
        KlineInterval.FifteenMinutes => 15,
        KlineInterval.OneHour => 60,
        _ => 60
    };
}
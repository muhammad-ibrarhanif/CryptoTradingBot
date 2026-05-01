using Binance.Net.Clients;
using Binance.Net.Enums;
using TradingBot.Core.Models;
using TradingBot.Dashboard.Models;

namespace TradingBot.Dashboard.Services;

public class BinanceService
{
    private readonly BinanceRestClient _client;

    public BinanceService()
    {
        _client = new BinanceRestClient();
    }

    // ============ DATA FETCHING METHODS ============

    // Live mode - get recent candles
    public async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int hours)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-hours);
        var klineInterval = GetKlineInterval(interval);

        var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, startTime, endTime, limit: 500);

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

    // Historical mode - get candles for a date range (batch fetching)
    public async Task<List<Candle>> GetHistoricalCandlesAsync(string symbol, string interval, DateTime start, DateTime end)
    {
        var klineInterval = GetKlineInterval(interval);
        var allCandles = new List<Candle>();
        var currentStart = start;

        while (currentStart < end)
        {
            var result = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, currentStart, end, limit: 1000);
            if (!result.Success || result.Data == null || result.Data.Length == 0)
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

            allCandles.AddRange(candles);

            if (candles.Count < 1000)
                break;

            currentStart = candles.Last().OpenTime.AddMinutes(GetMinutesForInterval(klineInterval));
            await Task.Delay(100);
        }

        return allCandles;
    }

    // ============ ANALYSIS METHODS ============

    // Analyze a timeframe for patterns and support/resistance
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

        // TREND DETECTION - Last 10 candles
        int higherHighs = 0, higherLows = 0;
        int lowerHighs = 0, lowerLows = 0;

        for (int i = candles.Count - 10; i < candles.Count; i++)
        {
            if (i > 0)
            {
                if (candles[i].High > candles[i - 1].High) higherHighs++;
                if (candles[i].Low > candles[i - 1].Low) higherLows++;
                if (candles[i].High < candles[i - 1].High) lowerHighs++;
                if (candles[i].Low < candles[i - 1].Low) lowerLows++;
            }
        }

        result.HigherHighs = higherHighs;
        result.HigherLows = higherLows;

        if (higherHighs >= 7 && higherLows >= 7)
            result.Trend = "📈 STRONG UPTREND";
        else if (higherHighs >= 5 && higherLows >= 5)
            result.Trend = "📈 Uptrend";
        else if (lowerHighs >= 7 && lowerLows >= 7)
            result.Trend = "📉 STRONG DOWNTREND";
        else if (lowerHighs >= 5 && lowerLows >= 5)
            result.Trend = "📉 Downtrend";
        else
            result.Trend = "➡️ Ranging";

        // SUPPORT/RESISTANCE - Last 20 candles
        var lows = candles.Skip(Math.Max(0, candles.Count - 20)).Select(c => c.Low).ToList();
        var highs = candles.Skip(Math.Max(0, candles.Count - 20)).Select(c => c.High).ToList();

        var supportGroups = lows.GroupBy(l => Math.Round(l / 0.05m) * 0.05m)
                                .Select(g => new { Level = g.Key, Count = g.Count() })
                                .OrderByDescending(g => g.Count);

        var resistanceGroups = highs.GroupBy(h => Math.Round(h / 0.05m) * 0.05m)
                                     .Select(g => new { Level = g.Key, Count = g.Count() })
                                     .OrderByDescending(g => g.Count);

        result.Support = supportGroups.FirstOrDefault()?.Level ?? lows.Min();
        result.Resistance = resistanceGroups.FirstOrDefault()?.Level ?? highs.Max();

        // Check if near support/resistance (within 0.3%)
        result.NearSupport = Math.Abs(lastCandle.Low - result.Support) / result.Support < 0.003m;
        result.NearResistance = Math.Abs(lastCandle.High - result.Resistance) / result.Resistance < 0.003m;

        // PATTERN DETECTION
        var body = lastCandle.Body;
        var range = lastCandle.High - lastCandle.Low;
        var lowerWick = lastCandle.LowerWick;
        var upperWick = lastCandle.UpperWick;

        result.Pattern = DetectPattern(lastCandle, prevCandle, twoBack, threeBack, result);

        return result;
    }

    private string DetectPattern(Candle current, Candle? previous, Candle? twoBack, Candle? threeBack, AnalysisResult result)
    {
        var body = current.Body;
        var range = current.High - current.Low;
        var lowerWick = current.LowerWick;
        var upperWick = current.UpperWick;

        if (range == 0) return "No movement";

        // ============ SINGLE CANDLE PATTERNS ============

        // Hammer (bullish reversal)
        if (lowerWick > body * 2 && body > 0 && current.IsBullish && lowerWick > upperWick)
        {
            return result.NearSupport ? "🔨 HAMMER (AT SUPPORT! - BUY SIGNAL)" : "🔨 HAMMER - Bullish reversal";
        }

        // Inverted Hammer
        if (upperWick > body * 2 && body > 0 && current.IsBullish && upperWick > lowerWick)
        {
            return result.NearSupport ? "⚡ INVERTED HAMMER (AT SUPPORT! - BUY)" : "⚡ INVERTED HAMMER - Potential bullish reversal";
        }

        // Shooting Star (bearish reversal)
        if (upperWick > body * 2 && body > 0 && current.IsBearish && upperWick > lowerWick)
        {
            return result.NearResistance ? "💫 SHOOTING STAR (AT RESISTANCE! - SELL SIGNAL)" : "💫 SHOOTING STAR - Bearish reversal";
        }

        // Hanging Man (bearish reversal after uptrend)
        if (lowerWick > body * 2 && body > 0 && current.IsBearish && lowerWick > upperWick)
        {
            return result.NearResistance ? "🪢 HANGING MAN (AT RESISTANCE! - SELL)" : "🪢 HANGING MAN - Potential bearish reversal";
        }

        // Doji (indecision)
        if (body < range * 0.1m && range > 0)
        {
            if (result.NearSupport) return "✚ DOJI (AT SUPPORT - Possible bounce UP)";
            if (result.NearResistance) return "✚ DOJI (AT RESISTANCE - Possible bounce DOWN)";
            return "✚ DOJI - Market indecision";
        }

        // Spinning Top
        if (body < range * 0.3m && body > 0)
        {
            return "🌀 SPINNING TOP - Neutral, wait for confirmation";
        }

        // Marubozu
        if (upperWick < body * 0.1m && lowerWick < body * 0.1m && body > 0)
        {
            return current.IsBullish ? "🟢 BULLISH MARUBOZU - Strong buying" : "🔴 BEARISH MARUBOZU - Strong selling";
        }

        // ============ TWO CANDLE PATTERNS ============

        if (previous != null)
        {
            // Bullish Engulfing
            if (previous.IsBearish && current.IsBullish &&
                current.Open < previous.Close && current.Close > previous.Open)
            {
                return result.NearSupport ? "🟢 BULLISH ENGULFING (AT SUPPORT! - STRONG BUY)" : "🟢 BULLISH ENGULFING - Strong reversal";
            }

            // Bearish Engulfing
            if (previous.IsBullish && current.IsBearish &&
                current.Open > previous.Close && current.Close < previous.Open)
            {
                return result.NearResistance ? "🔴 BEARISH ENGULFING (AT RESISTANCE! - STRONG SELL)" : "🔴 BEARISH ENGULFING - Strong reversal";
            }

            // Bullish Harami
            if (previous.IsBearish && current.IsBullish &&
                current.Open > previous.Close && current.Close < previous.Open &&
                current.Body < previous.Body * 0.5m)
            {
                return "🤰 BULLISH HARAMI - Potential reversal";
            }

            // Bearish Harami
            if (previous.IsBullish && current.IsBearish &&
                current.Open < previous.Close && current.Close > previous.Open &&
                current.Body < previous.Body * 0.5m)
            {
                return "🤰 BEARISH HARAMI - Potential reversal down";
            }

            // Piercing Pattern
            if (previous.IsBearish && current.IsBullish &&
                current.Open < previous.Low &&
                current.Close > (previous.Open + previous.Close) / 2 &&
                current.Close < previous.Open)
            {
                return result.NearSupport ? "📌 PIERCING PATTERN (AT SUPPORT! - BUY)" : "📌 PIERCING PATTERN - Bullish reversal";
            }

            // Dark Cloud Cover
            if (previous.IsBullish && current.IsBearish &&
                current.Open > previous.High &&
                current.Close < (previous.Open + previous.Close) / 2 &&
                current.Close > previous.Open)
            {
                return result.NearResistance ? "☁️ DARK CLOUD COVER (AT RESISTANCE! - SELL)" : "☁️ DARK CLOUD COVER - Bearish reversal";
            }

            // Tweezers Bottom
            if (Math.Abs(current.Low - previous.Low) / current.Low < 0.001m && current.IsBullish)
            {
                return result.NearSupport ? "✂️ TWEEZERS BOTTOM (AT SUPPORT! - BUY SIGNAL)" : "✂️ TWEEZERS BOTTOM - Support holding";
            }

            // Tweezers Top
            if (Math.Abs(current.High - previous.High) / current.High < 0.001m && current.IsBearish)
            {
                return result.NearResistance ? "✂️ TWEEZERS TOP (AT RESISTANCE! - SELL SIGNAL)" : "✂️ TWEEZERS TOP - Resistance holding";
            }
        }

        // ============ THREE CANDLE PATTERNS ============

        if (twoBack != null && previous != null)
        {
            // Morning Star
            if (twoBack.IsBearish &&
                previous.Body < (previous.High - previous.Low) * 0.3m &&
                current.IsBullish &&
                current.Close > twoBack.High)
            {
                return result.NearSupport ? "⭐ MORNING STAR (AT SUPPORT! - STRONG BUY ★★★)" : "⭐ MORNING STAR - Strong bullish reversal";
            }

            // Evening Star
            if (twoBack.IsBullish &&
                previous.Body < (previous.High - previous.Low) * 0.3m &&
                current.IsBearish &&
                current.Close < twoBack.Low)
            {
                return result.NearResistance ? "🌙 EVENING STAR (AT RESISTANCE! - STRONG SELL ★★★)" : "🌙 EVENING STAR - Strong bearish reversal";
            }

            // Three White Soldiers
            if (twoBack.IsBullish && previous.IsBullish && current.IsBullish &&
                current.Close > previous.Close && previous.Close > twoBack.Close &&
                current.Open > previous.Open && previous.Open > twoBack.Open)
            {
                return "⚪⚪⚪ THREE WHITE SOLDIERS - Strong bullish continuation";
            }

            // Three Black Crows
            if (twoBack.IsBearish && previous.IsBearish && current.IsBearish &&
                current.Close < previous.Close && previous.Close < twoBack.Close &&
                current.Open < previous.Open && previous.Open < twoBack.Open)
            {
                return "🐦‍⬛🐦‍⬛🐦‍⬛ THREE BLACK CROWS - Strong bearish continuation";
            }
        }

        // ============ FOUR CANDLE PATTERNS ============

        if (threeBack != null && twoBack != null && previous != null)
        {
            // Three Inside Up
            if (threeBack.IsBearish && twoBack.IsBullish && twoBack.Body < threeBack.Body * 0.5m &&
                previous.IsBullish && previous.Close > threeBack.High && current.IsBullish)
            {
                return "📈 THREE INSIDE UP - Bullish reversal confirmed";
            }

            // Three Inside Down
            if (threeBack.IsBullish && twoBack.IsBearish && twoBack.Body < threeBack.Body * 0.5m &&
                previous.IsBearish && previous.Close < threeBack.Low && current.IsBearish)
            {
                return "📉 THREE INSIDE DOWN - Bearish reversal confirmed";
            }
        }

        return current.IsBullish ? "🟢 Bullish candle" : "🔴 Bearish candle";
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
            Console.WriteLine("Insufficient data for backtest");
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
                decimal entryPrice = lastCandle.Close;
                string patternName = ExtractPatternName(analysis.Pattern);

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

                if (!hitTarget && !hitStop)
                {
                    exitPrice = candles5M[Math.Min(i + 48, candles5M.Count - 1)].Close;
                }

                // Calculate P&L correctly
                decimal pnlPercent = 0;
                bool isWinner = false;

                if (isBuySignal)
                {
                    pnlPercent = (exitPrice - entryPrice) / entryPrice * 100m;
                    isWinner = pnlPercent > 0;
                }
                else
                {
                    pnlPercent = (entryPrice - exitPrice) / entryPrice * 100m;
                    isWinner = pnlPercent > 0;
                }

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

                // Track pattern stats
                if (!patternStats.ContainsKey(patternName))
                    patternStats[patternName] = (0, 0, 0);
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

        var bestPattern = patternStats.OrderByDescending(p => p.Value.wins / (double)p.Value.total)
                                       .FirstOrDefault();

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

        Console.WriteLine($"Backtest complete: {metrics.TotalTrades} trades, Win Rate: {metrics.WinRate:F1}%, Total P&L: {metrics.TotalPnL:F2}%, Profit Factor: {metrics.ProfitFactor:F2}");

        return metrics;
    }

    // ============ HELPER METHODS ============

    private KlineInterval GetKlineInterval(string interval) => interval switch
    {
        "1H" => KlineInterval.OneHour,
        "15m" => KlineInterval.FifteenMinutes,
        "5m" => KlineInterval.FiveMinutes,
        _ => KlineInterval.OneHour
    };

    private int GetMinutesForInterval(KlineInterval interval) => interval switch
    {
        KlineInterval.FiveMinutes => 5,
        KlineInterval.FifteenMinutes => 15,
        KlineInterval.OneHour => 60,
        _ => 60
    };

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

    // ============ 1-MINUTE SCALPING METHODS ============

    public async Task<List<Candle>> Get1MinuteCandlesAsync(string symbol, DateTime start, DateTime end)
    {
        var allCandles = new List<Candle>();
        var currentStart = start;

        using var client = new BinanceRestClient();

        while (currentStart < end)
        {
            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, KlineInterval.OneMinute, currentStart, end, limit: 1000);
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

            allCandles.AddRange(candles);

            if (candles.Count < 1000)
                break;

            currentStart = candles.Last().OpenTime.AddMinutes(1);
            await Task.Delay(100);
        }

        return allCandles;
    }

    public async Task<(BacktestMetrics Metrics, List<HistoricalSignal> Signals)> Run1MinuteScalpingBacktestAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        var metrics = new BacktestMetrics();
        var historicalSignals = new List<HistoricalSignal>();
        var patternStats = new Dictionary<string, (int total, int wins, decimal totalPnL)>();

        // Use SAME warmup for all timeframes (7 days before start date)
        var warmupStart = startDate.AddDays(-7);

        var candles1M = await Get1MinuteCandlesAsync(symbol, warmupStart, endDate);
        var candles15M = await GetHistoricalCandlesAsync(symbol, "15m", warmupStart, endDate);
        var candles1H = await GetHistoricalCandlesAsync(symbol, "1H", warmupStart, endDate);

        if (candles1M.Count < 500)
        {
            Console.WriteLine($"Insufficient 1M data: {candles1M.Count} candles");
            return (metrics, historicalSignals);
        }

        Console.WriteLine($"1M Scalping Backtest: {candles1M.Count} candles from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

        const decimal targetPercent = 0.15m;
        const decimal stopPercent = 0.1m;
        const int maxHoldMinutes = 10;

        // Find the index where the start date begins (to only backtest from startDate)
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
            // Get 1M analysis (using last 30 candles for pattern detection)
            var analysisCandles1M = candles1M.Skip(Math.Max(0, i - 30)).Take(30).ToList();
            var analysis1M = AnalyzeTimeframe(analysisCandles1M, "1M");

            // Get higher timeframe trends (using candles up to current time)
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
                    Reason = isBuy ? $"{patternName} at support {analysis1M.Support:F4}" : $"{patternName} at resistance {analysis1M.Resistance:F4}",
                    Trend1H = analysis1H.Trend,
                    Trend15M = analysis15M.Trend
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
}
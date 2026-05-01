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
        var patternWins = new Dictionary<string, int>();
        var patternCounts = new Dictionary<string, int>();

        var candles5M = await GetHistoricalCandlesAsync(symbol, "5m", startDate, endDate);

        if (candles5M.Count < 100)
        {
            Console.WriteLine("Insufficient data for backtest");
            return metrics;
        }

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

                if (!patternCounts.ContainsKey(patternName)) patternCounts[patternName] = 0;
                patternCounts[patternName]++;

                decimal exitPrice = entryPrice;
                bool isWinner = false;
                decimal targetPrice = 0;
                decimal stopPrice = 0;

                if (isBuySignal)
                {
                    targetPrice = analysis.Resistance;
                    stopPrice = analysis.Support * 0.995m;

                    int j = i + 1;
                    for (; j < Math.Min(i + 48, candles5M.Count); j++)
                    {
                        if (candles5M[j].High >= targetPrice)
                        {
                            exitPrice = targetPrice;
                            isWinner = true;
                            break;
                        }
                        if (candles5M[j].Low <= stopPrice)
                        {
                            exitPrice = stopPrice;
                            isWinner = false;
                            break;
                        }
                    }

                    if (j >= candles5M.Count || j >= i + 48)
                    {
                        exitPrice = candles5M[Math.Min(j - 1, candles5M.Count - 1)].Close;
                        isWinner = exitPrice > entryPrice;
                    }
                }
                else if (isSellSignal)
                {
                    targetPrice = analysis.Support;
                    stopPrice = analysis.Resistance * 1.005m;

                    int j = i + 1;
                    for (; j < Math.Min(i + 48, candles5M.Count); j++)
                    {
                        if (candles5M[j].Low <= targetPrice)
                        {
                            exitPrice = targetPrice;
                            isWinner = true;
                            break;
                        }
                        if (candles5M[j].High >= stopPrice)
                        {
                            exitPrice = stopPrice;
                            isWinner = false;
                            break;
                        }
                    }

                    if (j >= candles5M.Count || j >= i + 48)
                    {
                        exitPrice = candles5M[Math.Min(j - 1, candles5M.Count - 1)].Close;
                        isWinner = exitPrice < entryPrice;
                    }
                }

                decimal pnlPercent = isBuySignal ? (exitPrice - entryPrice) / entryPrice * 100m : (entryPrice - exitPrice) / entryPrice * 100m;

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
                    Timeframe = "5M"
                });

                if (isWinner)
                {
                    if (!patternWins.ContainsKey(patternName)) patternWins[patternName] = 0;
                    patternWins[patternName]++;
                }

                i += 12;
            }
        }

        var winners = historicalSignals.Where(s => s.IsWinner).ToList();
        var losers = historicalSignals.Where(s => !s.IsWinner).ToList();

        var bestPattern = patternCounts.OrderByDescending(p => patternWins.GetValueOrDefault(p.Key, 0) / (double)p.Value)
                                       .FirstOrDefault();

        metrics.TotalSignals = historicalSignals.Count;
        metrics.TotalTrades = historicalSignals.Count;
        metrics.WinningTrades = winners.Count;
        metrics.LosingTrades = losers.Count;
        metrics.WinRate = metrics.TotalTrades > 0 ? (double)winners.Count / metrics.TotalTrades * 100 : 0;
        metrics.TotalPnL = historicalSignals.Sum(s => s.PnL);
        metrics.TotalPnLPercent = metrics.TotalPnL;
        metrics.AvgWin = winners.Count > 0 ? winners.Average(w => w.PnL) : 0;
        metrics.AvgLoss = losers.Count > 0 ? Math.Abs(losers.Average(l => l.PnL)) : 0;
        metrics.ProfitFactor = metrics.AvgLoss > 0 ? metrics.AvgWin / metrics.AvgLoss : metrics.AvgWin > 0 ? 999 : 0;
        metrics.BestPattern = bestPattern.Key ?? "None";
        metrics.BestPatternWins = patternWins.GetValueOrDefault(bestPattern.Key, 0);

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
}
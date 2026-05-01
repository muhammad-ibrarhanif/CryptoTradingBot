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

    public async Task<List<Candle>> GetCandlesAsync(string symbol, string interval, int hours)
    {
        var endTime = DateTime.UtcNow;
        var startTime = endTime.AddHours(-hours);

        var klineInterval = interval switch
        {
            "1H" => KlineInterval.OneHour,
            "15m" => KlineInterval.FifteenMinutes,
            "5m" => KlineInterval.FiveMinutes,
            _ => KlineInterval.OneHour
        };

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

    public AnalysisResult AnalyzeTimeframe(List<Candle> candles, string timeframe)
    {
        var result = new AnalysisResult();

        if (candles == null || candles.Count < 20)
        {
            result.Trend = "Insufficient data";
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

        result.NearSupport = Math.Abs(lastCandle.Low - result.Support) / result.Support < 0.003m;
        result.NearResistance = Math.Abs(lastCandle.High - result.Resistance) / result.Resistance < 0.003m;

        // PATTERN DETECTION
        var body = lastCandle.Body;
        var range = lastCandle.High - lastCandle.Low;
        var lowerWick = lastCandle.LowerWick;
        var upperWick = lastCandle.UpperWick;

        result.Pattern = DetectSingleCandlePattern(lastCandle, result);

        // Multi-candle patterns
        if (prevCandle != null)
        {
            var twoCandlePattern = DetectTwoCandlePattern(lastCandle, prevCandle, result);
            if (!string.IsNullOrEmpty(twoCandlePattern))
                result.Pattern = twoCandlePattern;
        }

        if (twoBack != null && prevCandle != null)
        {
            var threeCandlePattern = DetectThreeCandlePattern(lastCandle, prevCandle, twoBack, result);
            if (!string.IsNullOrEmpty(threeCandlePattern))
                result.Pattern = threeCandlePattern;
        }

        if (threeBack != null && twoBack != null && prevCandle != null)
        {
            var fourCandlePattern = DetectFourCandlePattern(lastCandle, prevCandle, twoBack, threeBack, result);
            if (!string.IsNullOrEmpty(fourCandlePattern))
                result.Pattern = fourCandlePattern;
        }

        return result;
    }

    private string DetectSingleCandlePattern(Candle candle, AnalysisResult result)
    {
        var body = candle.Body;
        var range = candle.High - candle.Low;
        var lowerWick = candle.LowerWick;
        var upperWick = candle.UpperWick;

        if (range == 0) return "No movement";

        // Hammer (bullish reversal)
        if (lowerWick > body * 2 && body > 0 && candle.IsBullish && lowerWick > upperWick)
        {
            return result.NearSupport ? "🔨 HAMMER (AT SUPPORT! - STRONG BUY)" : "🔨 HAMMER - Bullish reversal";
        }

        // Inverted Hammer (bullish reversal after drop)
        if (upperWick > body * 2 && body > 0 && candle.IsBullish && upperWick > lowerWick)
        {
            return result.NearSupport ? "⚡ INVERTED HAMMER (AT SUPPORT! - BUY SIGNAL)" : "⚡ INVERTED HAMMER - Potential bullish reversal";
        }

        // Shooting Star (bearish reversal)
        if (upperWick > body * 2 && body > 0 && candle.IsBearish && upperWick > lowerWick)
        {
            return result.NearResistance ? "💫 SHOOTING STAR (AT RESISTANCE! - SELL SIGNAL)" : "💫 SHOOTING STAR - Bearish reversal";
        }

        // Hanging Man (bearish reversal after uptrend)
        if (lowerWick > body * 2 && body > 0 && candle.IsBearish && lowerWick > upperWick)
        {
            return result.NearResistance ? "🪢 HANGING MAN (AT RESISTANCE! - SELL SIGNAL)" : "🪢 HANGING MAN - Potential bearish reversal";
        }

        // Doji (indecision)
        if (body < range * 0.1m && range > 0)
        {
            if (result.NearSupport) return "✚ DOJI (AT SUPPORT - Possible bounce UP)";
            if (result.NearResistance) return "✚ DOJI (AT RESISTANCE - Possible bounce DOWN)";
            return "✚ DOJI - Market indecision";
        }

        // Spinning Top (neutral)
        if (body < range * 0.3m && body > 0)
        {
            return "🌀 SPINNING TOP - Neutral, wait for confirmation";
        }

        // Marubozu (strong momentum)
        if (upperWick < body * 0.1m && lowerWick < body * 0.1m && body > 0)
        {
            if (candle.IsBullish) return "🟢 BULLISH MARUBOZU - Strong buying pressure";
            return "🔴 BEARISH MARUBOZU - Strong selling pressure";
        }

        return "";
    }

    private string DetectTwoCandlePattern(Candle current, Candle previous, AnalysisResult result)
    {
        // Bullish Engulfing
        if (previous.IsBearish && current.IsBullish &&
            current.Open < previous.Close && current.Close > previous.Open)
        {
            return result.NearSupport ? "🟢 BULLISH ENGULFING (AT SUPPORT! - STRONG BUY)" : "🟢 BULLISH ENGULFING - Strong reversal signal";
        }

        // Bearish Engulfing
        if (previous.IsBullish && current.IsBearish &&
            current.Open > previous.Close && current.Close < previous.Open)
        {
            return result.NearResistance ? "🔴 BEARISH ENGULFING (AT RESISTANCE! - STRONG SELL)" : "🔴 BEARISH ENGULFING - Strong reversal signal";
        }

        // Bullish Harami (pregnant woman pattern)
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

        // Tweezers Bottom (two lows at same level)
        if (Math.Abs(current.Low - previous.Low) / current.Low < 0.001m && current.IsBullish)
        {
            return result.NearSupport ? "✂️ TWEEZERS BOTTOM (AT SUPPORT! - BUY SIGNAL)" : "✂️ TWEEZERS BOTTOM - Support holding";
        }

        // Tweezers Top (two highs at same level)
        if (Math.Abs(current.High - previous.High) / current.High < 0.001m && current.IsBearish)
        {
            return result.NearResistance ? "✂️ TWEEZERS TOP (AT RESISTANCE! - SELL SIGNAL)" : "✂️ TWEEZERS TOP - Resistance holding";
        }

        return "";
    }

    private string DetectThreeCandlePattern(Candle current, Candle previous, Candle twoBack, AnalysisResult result)
    {
        // Morning Star (bullish reversal)
        bool isMorningStar = twoBack.IsBearish &&
                             previous.Body < (previous.High - previous.Low) * 0.3m &&
                             current.IsBullish &&
                             current.Close > twoBack.High;

        if (isMorningStar)
        {
            return result.NearSupport ? "⭐ MORNING STAR (AT SUPPORT! - STRONG BUY ★★★)" : "⭐ MORNING STAR - Strong bullish reversal";
        }

        // Evening Star (bearish reversal)
        bool isEveningStar = twoBack.IsBullish &&
                             previous.Body < (previous.High - previous.Low) * 0.3m &&
                             current.IsBearish &&
                             current.Close < twoBack.Low;

        if (isEveningStar)
        {
            return result.NearResistance ? "🌙 EVENING STAR (AT RESISTANCE! - STRONG SELL ★★★)" : "🌙 EVENING STAR - Strong bearish reversal";
        }

        // Three White Soldiers (strong bullish continuation)
        bool threeWhiteSoldiers = twoBack.IsBullish && previous.IsBullish && current.IsBullish &&
                                   twoBack.Close > twoBack.Open && previous.Close > previous.Open && current.Close > current.Open &&
                                   current.Close > previous.Close && previous.Close > twoBack.Close &&
                                   current.Open > previous.Open && previous.Open > twoBack.Open;

        if (threeWhiteSoldiers)
        {
            return "⚪⚪⚪ THREE WHITE SOLDIERS - Strong bullish continuation";
        }

        // Three Black Crows (strong bearish continuation)
        bool threeBlackCrows = twoBack.IsBearish && previous.IsBearish && current.IsBearish &&
                                twoBack.Close < twoBack.Open && previous.Close < previous.Open && current.Close < current.Open &&
                                current.Close < previous.Close && previous.Close < twoBack.Close &&
                                current.Open < previous.Open && previous.Open < twoBack.Open;

        if (threeBlackCrows)
        {
            return "🐦‍⬛🐦‍⬛🐦‍⬛ THREE BLACK CROWS - Strong bearish continuation";
        }

        // Abandoned Baby (rare, strong reversal)
        bool abandonedBaby = twoBack.IsBearish &&
                              previous.Body < (previous.High - previous.Low) * 0.1m &&
                              current.IsBullish &&
                              previous.Low < twoBack.Low && previous.Low < current.Low;

        if (abandonedBaby)
        {
            return "👶 ABANDONED BABY - Rare strong reversal signal";
        }

        return "";
    }

    private string DetectFourCandlePattern(Candle current, Candle previous, Candle twoBack, Candle threeBack, AnalysisResult result)
    {
        // Three Inside Up (bullish reversal)
        bool threeInsideUp = threeBack.IsBearish && twoBack.IsBullish && twoBack.Body < threeBack.Body * 0.5m &&
                              previous.IsBullish && previous.Close > threeBack.High && current.IsBullish;

        if (threeInsideUp)
        {
            return "📈 THREE INSIDE UP - Bullish reversal confirmed";
        }

        // Three Inside Down (bearish reversal)
        bool threeInsideDown = threeBack.IsBullish && twoBack.IsBearish && twoBack.Body < threeBack.Body * 0.5m &&
                                previous.IsBearish && previous.Close < threeBack.Low && current.IsBearish;

        if (threeInsideDown)
        {
            return "📉 THREE INSIDE DOWN - Bearish reversal confirmed";
        }

        return "";
    }

    public List<TradingSignal> GenerateSignals(List<Candle> candles1H, List<Candle> candles15M, List<Candle> candles5M)
    {
        var signals = new List<TradingSignal>();

        if (candles5M == null || candles5M.Count < 15) return signals;
        if (candles15M == null || candles15M.Count < 10) return signals;
        if (candles1H == null || candles1H.Count < 10) return signals;

        var last5m = candles5M.Last();
        var prev5m = candles5M[candles5M.Count - 2];
        var twoBack5m = candles5M[candles5M.Count - 3];
        var threeBack5m = candles5M.Count >= 4 ? candles5M[candles5M.Count - 4] : null;

        // Get support/resistance from 15M
        var support = candles15M.Skip(Math.Max(0, candles15M.Count - 20)).Select(c => c.Low).Min();
        var resistance = candles15M.Skip(Math.Max(0, candles15M.Count - 20)).Select(c => c.High).Max();
        var nearSupport = Math.Abs(last5m.Low - support) / support < 0.003m;
        var nearResistance = Math.Abs(last5m.High - resistance) / resistance < 0.003m;

        // Get trend from 1H
        int higherHighs = 0, higherLows = 0;
        for (int i = candles1H.Count - 8; i < candles1H.Count; i++)
        {
            if (i > 0)
            {
                if (candles1H[i].High > candles1H[i - 1].High) higherHighs++;
                if (candles1H[i].Low > candles1H[i - 1].Low) higherLows++;
            }
        }
        var isUptrend = higherHighs >= 4 && higherLows >= 4;
        var isDowntrend = higherHighs <= 2 && higherLows <= 2;

        var body = last5m.Body;
        var range = last5m.High - last5m.Low;
        var lowerWick = last5m.LowerWick;
        var upperWick = last5m.UpperWick;

        // ============ BUY SIGNALS ============

        // 1. Hammer at support (STRONG)
        if (lowerWick > body * 2 && body > 0 && last5m.IsBullish && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Hammer at Support",
                Message = $"🔨 Hammer candle at support {support:F4}. Strong reversal signal.",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 2. Bullish Engulfing at support (STRONG)
        if (prev5m.IsBearish && last5m.IsBullish &&
            last5m.Open < prev5m.Close && last5m.Close > prev5m.Open && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Bullish Engulfing",
                Message = $"🟢 Bullish engulfing at support {support:F4}. Strong buy signal.",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 3. Morning Star (VERY STRONG)
        if (twoBack5m.IsBearish && prev5m.Body < (prev5m.High - prev5m.Low) * 0.3m &&
            last5m.IsBullish && last5m.Close > twoBack5m.High && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Morning Star",
                Message = $"⭐ Morning star reversal pattern at support {support:F4}. Very strong buy!",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 4. Piercing Pattern at support
        if (prev5m.IsBearish && last5m.IsBullish &&
            last5m.Open < prev5m.Low &&
            last5m.Close > (prev5m.Open + prev5m.Close) / 2 &&
            last5m.Close < prev5m.Open && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Piercing Pattern",
                Message = $"📌 Piercing pattern at support {support:F4}. Bullish reversal.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 5. Tweezers Bottom at support
        if (threeBack5m != null && Math.Abs(last5m.Low - twoBack5m.Low) / last5m.Low < 0.001m &&
            last5m.IsBullish && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Tweezers Bottom",
                Message = $"✂️ Double bottom at support {support:F4}. Support is holding strong.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 6. Bullish Harami at support
        if (prev5m.IsBearish && last5m.IsBullish &&
            last5m.Open > prev5m.Close && last5m.Close < prev5m.Open &&
            last5m.Body < prev5m.Body * 0.5m && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Bullish Harami",
                Message = $"🤰 Bullish harami (pregnant woman) at support {support:F4}. Potential reversal.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 7. Doji with bullish follow-up at support
        bool isDoji = prev5m.Body < (prev5m.High - prev5m.Low) * 0.1m;
        if (isDoji && last5m.IsBullish && nearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Doji + Bullish Follow-up",
                Message = $"✚ Doji at support followed by bullish candle at {support:F4}.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 8. Inverted Hammer at support
        if (upperWick > body * 2 && body > 0 && last5m.IsBullish && nearSupport && !isUptrend)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Inverted Hammer",
                Message = $"⚡ Inverted hammer at support {support:F4}. Potential reversal after drop.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 9. Support bounce with volume
        decimal avgVolume = candles5M.Skip(Math.Max(0, candles5M.Count - 20)).Average(c => c.Volume);
        bool highVolume = last5m.Volume > avgVolume * 1.5m;
        if (nearSupport && last5m.IsBullish && highVolume)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Support Bounce with Volume",
                Message = $"📈 Price bouncing off support {support:F4} with {last5m.Volume / avgVolume:F1}x average volume.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // ============ SELL SIGNALS ============

        // 10. Shooting Star at resistance
        if (upperWick > body * 2 && body > 0 && last5m.IsBearish && nearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Shooting Star at Resistance",
                Message = $"💫 Shooting star at resistance {resistance:F4}. Bearish reversal signal.",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 11. Bearish Engulfing at resistance
        if (prev5m.IsBullish && last5m.IsBearish &&
            last5m.Open > prev5m.Close && last5m.Close < prev5m.Open && nearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Bearish Engulfing",
                Message = $"🔴 Bearish engulfing at resistance {resistance:F4}. Strong sell signal.",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 12. Evening Star (VERY STRONG)
        if (twoBack5m.IsBullish && prev5m.Body < (prev5m.High - prev5m.Low) * 0.3m &&
            last5m.IsBearish && last5m.Close < twoBack5m.Low && nearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Evening Star",
                Message = $"🌙 Evening star reversal pattern at resistance {resistance:F4}. Strong sell!",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 13. Dark Cloud Cover at resistance
        if (prev5m.IsBullish && last5m.IsBearish &&
            last5m.Open > prev5m.High &&
            last5m.Close < (prev5m.Open + prev5m.Close) / 2 &&
            last5m.Close > prev5m.Open && nearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Dark Cloud Cover",
                Message = $"☁️ Dark cloud cover at resistance {resistance:F4}. Bearish reversal.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 14. Hanging Man at resistance
        if (lowerWick > body * 2 && body > 0 && last5m.IsBearish && nearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Hanging Man",
                Message = $"🪢 Hanging man at resistance {resistance:F4}. Bearish reversal after uptrend.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // 15. Three Black Crows
        if (threeBack5m != null && threeBack5m.IsBearish && twoBack5m.IsBearish &&
            prev5m.IsBearish && last5m.IsBearish && nearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Three Black Crows",
                Message = $"🐦‍⬛ Three black crows at resistance {resistance:F4}. Strong bearish continuation.",
                Price = last5m.Close,
                Strength = 3,
                Time = DateTime.UtcNow
            });
        }

        // 16. Resistance rejection with volume
        if (nearResistance && last5m.IsBearish && highVolume)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Resistance Rejection",
                Message = $"📉 Price rejected from resistance {resistance:F4} with {last5m.Volume / avgVolume:F1}x volume.",
                Price = last5m.Close,
                Strength = 2,
                Time = DateTime.UtcNow
            });
        }

        // Sort signals by strength (highest first)
        return signals.OrderByDescending(s => s.Strength).ToList();
    }

    public async Task<List<Candle>> GetHistoricalCandlesAsync(string symbol, string interval, DateTime start, DateTime end)
    {
        var klineInterval = interval switch
        {
            "1H" => KlineInterval.OneHour,
            "15m" => KlineInterval.FifteenMinutes,
            "5m" => KlineInterval.FiveMinutes,
            _ => KlineInterval.OneHour
        };

        var allCandles = new List<Candle>();
        var currentStart = start;

        using var client = new BinanceRestClient();

        while (currentStart < end)
        {
            var result = await client.SpotApi.ExchangeData.GetKlinesAsync(symbol, klineInterval, currentStart, end, limit: 1000);
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
            currentStart = candles.Last().OpenTime.AddMinutes(GetMinutesForInterval(klineInterval));
            await Task.Delay(100);
        }

        return allCandles;
    }

    public async Task<BacktestMetrics> RunBacktestAsync(string symbol, DateTime startDate, DateTime endDate)
    {
        Console.WriteLine($"Running backtest for {symbol} from {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}");

        var candles5M = await GetHistoricalCandlesAsync(symbol, "5m", startDate, endDate);
        var candles15M = await GetHistoricalCandlesAsync(symbol, "15m", startDate.AddDays(-5), endDate);
        var candles1H = await GetHistoricalCandlesAsync(symbol, "1H", startDate.AddDays(-10), endDate);

        var historicalSignals = new List<HistoricalSignal>();
        var patternStats = new Dictionary<string, int>();
        var patternWins = new Dictionary<string, int>();

        // Simulate walking through time
        for (int i = 60; i < candles5M.Count; i++)
        {
            var currentTime = candles5M[i].OpenTime;
            var analysis = AnalyzeTimeframe(candles5M.Take(i + 1).ToList(), "5M");
            var support = analysis.Support;
            var resistance = analysis.Resistance;
            var nearSupport = analysis.NearSupport;
            var nearResistance = analysis.NearResistance;
            var pattern = analysis.Pattern;

            // Detect BUY signals
            if ((pattern.Contains("HAMMER") || pattern.Contains("DOJI") || pattern.Contains("ENGULFING") ||
                 pattern.Contains("MORNING STAR") || pattern.Contains("TWEEZERS BOTTOM")) && nearSupport)
            {
                var signalTime = currentTime;
                var entryPrice = candles5M[i].Close;

                // Simulate exit after 2 hours (24 candles on 5M) or at next resistance
                decimal exitPrice = entryPrice;
                int exitIndex = i + 24;
                bool hitTarget = false;

                for (int j = i + 1; j < Math.Min(i + 48, candles5M.Count); j++)
                {
                    if (candles5M[j].High >= resistance)
                    {
                        exitPrice = resistance;
                        exitIndex = j;
                        hitTarget = true;
                        break;
                    }
                    if (candles5M[j].Low <= support * 0.995m) // 0.5% stop
                    {
                        exitPrice = support * 0.995m;
                        exitIndex = j;
                        break;
                    }
                }

                decimal pnl = (exitPrice - entryPrice) / entryPrice * 100m;
                bool isWinner = pnl > 0;

                historicalSignals.Add(new HistoricalSignal
                {
                    Time = signalTime,
                    Type = "BUY",
                    Pattern = ExtractPatternName(pattern),
                    EntryPrice = entryPrice,
                    ExitPrice = exitPrice,
                    PnL = pnl,
                    PnLPercent = pnl,
                    IsWinner = isWinner,
                    Timeframe = "5M"
                });

                // Track pattern statistics
                string patternName = ExtractPatternName(pattern);
                if (!patternStats.ContainsKey(patternName)) patternStats[patternName] = 0;
                if (!patternWins.ContainsKey(patternName)) patternWins[patternName] = 0;
                patternStats[patternName]++;
                if (isWinner) patternWins[patternName]++;

                i = exitIndex;
            }

            // Detect SELL signals (Shooting Star, Evening Star, Bearish Engulfing at resistance)
            if ((pattern.Contains("SHOOTING STAR") || pattern.Contains("EVENING STAR") ||
                 pattern.Contains("BEARISH ENGULFING")) && nearResistance)
            {
                var signalTime = currentTime;
                var entryPrice = candles5M[i].Close;

                decimal exitPrice = entryPrice;
                for (int j = i + 1; j < Math.Min(i + 48, candles5M.Count); j++)
                {
                    if (candles5M[j].Low <= support)
                    {
                        exitPrice = support;
                        break;
                    }
                    if (candles5M[j].High >= resistance * 1.005m)
                    {
                        exitPrice = resistance * 1.005m;
                        break;
                    }
                }

                decimal pnl = (entryPrice - exitPrice) / entryPrice * 100m;
                bool isWinner = pnl > 0;

                historicalSignals.Add(new HistoricalSignal
                {
                    Time = signalTime,
                    Type = "SELL",
                    Pattern = ExtractPatternName(pattern),
                    EntryPrice = entryPrice,
                    ExitPrice = exitPrice,
                    PnL = pnl,
                    PnLPercent = pnl,
                    IsWinner = isWinner,
                    Timeframe = "5M"
                });
            }
        }

        // Calculate metrics
        var trades = historicalSignals;
        var winners = trades.Where(t => t.IsWinner).ToList();
        var losers = trades.Where(t => !t.IsWinner).ToList();

        decimal totalPnL = trades.Sum(t => t.PnL);
        decimal totalWinPnL = winners.Sum(w => w.PnL);
        decimal totalLossPnL = Math.Abs(losers.Sum(l => l.PnL));

        var bestPattern = patternStats.OrderByDescending(p => patternWins.GetValueOrDefault(p.Key, 0) / (double)p.Value)
                                      .FirstOrDefault();

        return new BacktestMetrics
        {
            TotalSignals = historicalSignals.Count,
            TotalTrades = historicalSignals.Count,
            WinningTrades = winners.Count,
            LosingTrades = losers.Count,
            WinRate = trades.Count > 0 ? (double)winners.Count / trades.Count * 100 : 0,
            TotalPnL = totalPnL,
            TotalPnLPercent = totalPnL,
            AvgWin = winners.Count > 0 ? totalWinPnL / winners.Count : 0,
            AvgLoss = losers.Count > 0 ? totalLossPnL / losers.Count : 0,
            ProfitFactor = totalLossPnL > 0 ? totalWinPnL / totalLossPnL : totalWinPnL > 0 ? 999 : 0,
            BestPattern = bestPattern.Key ?? "None",
            BestPatternWins = patternWins.GetValueOrDefault(bestPattern.Key, 0)
        };
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
        if (pattern.Contains("PIERCING")) return "Piercing";
        if (pattern.Contains("DARK CLOUD")) return "Dark Cloud";
        return "Pattern";
    }

    private int GetMinutesForInterval(KlineInterval interval) => interval switch
    {
        KlineInterval.FiveMinutes => 5,
        KlineInterval.FifteenMinutes => 15,
        KlineInterval.OneHour => 60,
        _ => 60
    };
}
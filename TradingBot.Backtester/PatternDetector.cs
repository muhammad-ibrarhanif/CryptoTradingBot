using TradingBot.Core.Models;

namespace TradingBot.Backtester;

public class CandlePattern
{
    public string Name { get; set; } = "";
    public bool Detected { get; set; }
    public string Description { get; set; } = "";
    public int Strength { get; set; }  // 1-5
    public string Type { get; set; } = ""; // Bullish, Bearish, Neutral
}

public static class PatternDetector
{
    public static List<CandlePattern> DetectAllPatterns(Candle current, Candle previous, Candle twoBack, Candle threeBack)
    {
        var patterns = new List<CandlePattern>();

        // ============================================================
        // BULLISH REVERSAL PATTERNS
        // ============================================================

        // 1. HAMMER
        bool isHammer = current.LowerWick > current.Body * 2 &&
                        current.Body > 0 &&
                        current.LowerWick > current.UpperWick;

        if (isHammer)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Hammer",
                Detected = true,
                Description = "Long lower wick, small body at top → Bullish reversal after drop",
                Strength = 3,
                Type = "Bullish"
            });
        }

        // 2. BULLISH ENGULFING
        bool isBullishEngulfing = previous.Close < previous.Open && // Previous red
                                   current.Open < previous.Close &&
                                   current.Close > previous.Open;

        if (isBullishEngulfing)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Bullish Engulfing",
                Detected = true,
                Description = "Green candle completely covers previous red → Strong bullish reversal",
                Strength = 4,
                Type = "Bullish"
            });
        }

        // 3. MORNING STAR (3 candles)
        bool isMorningStar = previous.Close < previous.Open && // Candle 1: Red
                              IsDoji(twoBack) && // Candle 2: Doji
                              current.Close > current.Open && // Candle 3: Green
                              current.Close > previous.High;

        if (isMorningStar)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Morning Star",
                Detected = true,
                Description = "Red → Doji → Green → Strong reversal after downtrend",
                Strength = 5,
                Type = "Bullish"
            });
        }

        // 4. PIERCING PATTERN
        bool isPiercing = previous.Close < previous.Open && // Previous red
                          current.Open < previous.Low && // Current opens below previous low
                          current.Close > (previous.Open + previous.Close) / 2 && // Closes above midpoint
                          current.Close < previous.Open; // Closes below previous open

        if (isPiercing)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Piercing Pattern",
                Detected = true,
                Description = "Green candle closes above midpoint of previous red → Bullish reversal",
                Strength = 4,
                Type = "Bullish"
            });
        }

        // 5. BULLISH HARAMI
        bool isBullishHarami = previous.Close < previous.Open && // Previous red
                                current.Body > 0 &&
                                current.Open > previous.Close &&
                                current.Close < previous.Open &&
                                current.Body < previous.Body * 0.5m; // Small body inside

        if (isBullishHarami)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Bullish Harami",
                Detected = true,
                Description = "Small green body inside previous red → Potential reversal",
                Strength = 3,
                Type = "Bullish"
            });
        }

        // 6. INVERTED HAMMER
        bool isInvertedHammer = current.UpperWick > current.Body * 2 &&
                                 current.Body > 0 &&
                                 current.UpperWick > current.LowerWick &&
                                 current.LowerWick < current.Body;

        if (isInvertedHammer)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Inverted Hammer",
                Detected = true,
                Description = "Long upper wick, small body at bottom → Possible reversal after drop",
                Strength = 3,
                Type = "Bullish"
            });
        }

        // ============================================================
        // BEARISH REVERSAL PATTERNS
        // ============================================================

        // 7. SHOOTING STAR
        bool isShootingStar = current.UpperWick > current.Body * 2 &&
                               current.Body > 0 &&
                               current.UpperWick > current.LowerWick &&
                               current.LowerWick < current.Body;

        if (isShootingStar)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Shooting Star",
                Detected = true,
                Description = "Long upper wick, small body at bottom → Bearish reversal after rise",
                Strength = 3,
                Type = "Bearish"
            });
        }

        // 8. BEARISH ENGULFING
        bool isBearishEngulfing = previous.Close > previous.Open && // Previous green
                                   current.Open > previous.Close &&
                                   current.Close < previous.Open;

        if (isBearishEngulfing)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Bearish Engulfing",
                Detected = true,
                Description = "Red candle completely covers previous green → Strong bearish reversal",
                Strength = 4,
                Type = "Bearish"
            });
        }

        // 9. EVENING STAR (3 candles)
        bool isEveningStar = previous.Close > previous.Open && // Candle 1: Green
                               IsDoji(twoBack) && // Candle 2: Doji
                               current.Close < current.Open && // Candle 3: Red
                               current.Close < previous.Low;

        if (isEveningStar)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Evening Star",
                Detected = true,
                Description = "Green → Doji → Red → Strong reversal after uptrend",
                Strength = 5,
                Type = "Bearish"
            });
        }

        // 10. DARK CLOUD COVER
        bool isDarkCloud = previous.Close > previous.Open && // Previous green
                            current.Open > previous.High && // Current opens above previous high
                            current.Close < (previous.Open + previous.Close) / 2 && // Closes below midpoint
                            current.Close > previous.Open; // Closes above previous open

        if (isDarkCloud)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Dark Cloud Cover",
                Detected = true,
                Description = "Red candle closes below midpoint of previous green → Bearish reversal",
                Strength = 4,
                Type = "Bearish"
            });
        }

        // 11. BEARISH HARAMI
        bool isBearishHarami = previous.Close > previous.Open && // Previous green
                                 current.Body > 0 &&
                                 current.Open < previous.Close &&
                                 current.Close > previous.Open &&
                                 current.Body < previous.Body * 0.5m; // Small body inside

        if (isBearishHarami)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Bearish Harami",
                Detected = true,
                Description = "Small red body inside previous green → Potential reversal down",
                Strength = 3,
                Type = "Bearish"
            });
        }

        // ============================================================
        // NEUTRAL / INDECISION PATTERNS
        // ============================================================

        // 12. DOJI
        if (IsDoji(current))
        {
            patterns.Add(new CandlePattern
            {
                Name = "Doji",
                Detected = true,
                Description = "Very small body, long wicks → Market indecision, possible reversal",
                Strength = 2,
                Type = "Neutral"
            });
        }

        // 13. SPINNING TOP
        bool isSpinningTop = current.Body < (current.High - current.Low) * 0.3m &&
                              current.Body > 0 &&
                              current.UpperWick > current.Body * 0.5m &&
                              current.LowerWick > current.Body * 0.5m;

        if (isSpinningTop)
        {
            patterns.Add(new CandlePattern
            {
                Name = "Spinning Top",
                Detected = true,
                Description = "Small body with wicks on both sides → Neutral, market uncertain",
                Strength = 1,
                Type = "Neutral"
            });
        }

        return patterns;
    }

    private static bool IsDoji(Candle candle)
    {
        decimal bodyPercent = candle.Body / (candle.High - candle.Low) * 100m;
        return bodyPercent < 10m && candle.Body > 0;
    }
}
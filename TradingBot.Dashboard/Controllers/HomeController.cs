using Microsoft.AspNetCore.Mvc;
using TradingBot.Core.Models;
using TradingBot.Dashboard.Models;
using TradingBot.Dashboard.Services;

namespace TradingBot.Dashboard.Controllers;

public class HomeController : Controller
{
    private readonly BinanceService _binanceService;

    public HomeController()
    {
        _binanceService = new BinanceService();
    }

    public async Task<IActionResult> Index(string symbol = "SOLUSDT", DateTime? startDate = null, DateTime? endDate = null, bool historical = false)
    {
        var viewModel = new ChartViewModel
        {
            Symbol = symbol,
            IsHistorical = historical,
            StartDate = startDate ?? DateTime.UtcNow.AddDays(-7),
            EndDate = endDate ?? DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow
        };

        try
        {
            if (historical && startDate.HasValue && endDate.HasValue)
            {
                // Historical mode - use consistent warmup
                var warmupStart = startDate.Value.AddDays(-7);

                viewModel.Candles1H = await _binanceService.GetHistoricalCandlesAsync(symbol, "1H", warmupStart, endDate.Value);
                viewModel.Candles15M = await _binanceService.GetHistoricalCandlesAsync(symbol, "15m", warmupStart, endDate.Value);
                viewModel.Candles5M = await _binanceService.GetHistoricalCandlesAsync(symbol, "5m", warmupStart, endDate.Value);

                // For display, filter to selected date range
                var analysisStart = startDate.Value;
                var analysisCandles1H = viewModel.Candles1H.Where(c => c.OpenTime >= analysisStart).ToList();
                var analysisCandles15M = viewModel.Candles15M.Where(c => c.OpenTime >= analysisStart).ToList();
                var analysisCandles5M = viewModel.Candles5M.Where(c => c.OpenTime >= analysisStart).ToList();

                viewModel.Analysis1H = _binanceService.AnalyzeTimeframe(analysisCandles1H, "1H");
                viewModel.Analysis15M = _binanceService.AnalyzeTimeframe(analysisCandles15M, "15M");
                viewModel.Analysis5M = _binanceService.AnalyzeTimeframe(analysisCandles5M, "5M");
                viewModel.Analysis1M = new AnalysisResult(); // Skip 1M

                // Run backtest on 5M
                viewModel.Metrics = await _binanceService.RunBacktestAsync(symbol, startDate.Value, endDate.Value);

                viewModel.Signals = GenerateTradingSignals(viewModel);
            }
            else
            {
                // Live mode - use 5M and 15M only
                viewModel.Candles1H = await _binanceService.GetCandlesAsync(symbol, "1H", 48);
                viewModel.Candles15M = await _binanceService.GetCandlesAsync(symbol, "15m", 24);
                viewModel.Candles5M = await _binanceService.GetCandlesAsync(symbol, "5m", 12);
                viewModel.Candles1M = new List<Candle>();

                viewModel.Analysis1H = _binanceService.AnalyzeTimeframe(viewModel.Candles1H, "1H");
                viewModel.Analysis15M = _binanceService.AnalyzeTimeframe(viewModel.Candles15M, "15M");
                viewModel.Analysis5M = _binanceService.AnalyzeTimeframe(viewModel.Candles5M, "5M");
                viewModel.Analysis1M = new AnalysisResult();

                viewModel.Signals = GenerateTradingSignals(viewModel);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> RunBacktest(string symbol, DateTime startDate, DateTime endDate)
    {
        return RedirectToAction("Index", new { symbol, startDate, endDate, historical = true });
    }

    [HttpPost]
    public IActionResult LiveMode(string symbol)
    {
        return RedirectToAction("Index", new { symbol, historical = false });
    }

    private List<TradingSignal> GenerateTradingSignals(ChartViewModel viewModel)
    {
        var signals = new List<TradingSignal>();

        // ============ STEP 1: IDENTIFY TREND FIRST (Human order) ============
        string trend1H = viewModel.Analysis1H.Trend;
        string trend15M = viewModel.Analysis15M.Trend;

        // Determine overall market direction
        bool is1HDowntrend = trend1H.Contains("DOWNTREND");
        bool is15MDowntrend = trend15M.Contains("DOWNTREND");
        bool is1HUptrend = trend1H.Contains("UPTREND");
        bool is15MUptrend = trend15M.Contains("UPTREND");

        // Overall bias (higher timeframe has more weight)
        bool isOverallBearish = is1HDowntrend || (is15MDowntrend && !is1HUptrend);
        bool isOverallBullish = is1HUptrend || (is15MUptrend && !is1HDowntrend);

        string marketBias = "NEUTRAL";
        if (isOverallBearish) marketBias = "BEARISH - Only look for SELL signals";
        if (isOverallBullish) marketBias = "BULLISH - Only look for BUY signals";

        // Add market bias as first signal (human looks at this first)
        signals.Add(new TradingSignal
        {
            Type = "INFO",
            Pattern = $"Market Bias: {marketBias}",
            Message = $"1H: {trend1H} | 15M: {trend15M}",
            Price = viewModel.Candles5M.Any() ? viewModel.Candles5M.Last().Close : 0,
            Strength = 1,
            Time = DateTime.UtcNow,
            Timeframe = "CONTEXT"
        });

        // ============ STEP 2: Get patterns (only after trend is known) ============
        var pattern5M = viewModel.Analysis5M.Pattern.ToUpper();
        var pattern15M = viewModel.Analysis15M.Pattern.ToUpper();
        var pattern1H = viewModel.Analysis1H.Pattern.ToUpper();

        bool nearSupport = viewModel.Analysis5M.NearSupport;
        bool nearResistance = viewModel.Analysis5M.NearResistance;

        // ============ STEP 3: Generate signals ONLY when pattern aligns with trend ============

        // BUY signals - ONLY in BULLISH market
        if (isOverallBullish)
        {
            // Hammer at support
            if (pattern5M.Contains("HAMMER") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Hammer at Support (5M)",
                    Message = $"Hammer at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Tweezers Bottom (Double Bottom)
            if (pattern5M.Contains("TWEEZERS BOTTOM") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Tweezers Bottom (5M)",
                    Message = $"Double bottom at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Bullish Engulfing
            if (pattern5M.Contains("BULLISH ENGULFING") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Bullish Engulfing (5M)",
                    Message = $"Bullish engulfing at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Morning Star
            if (pattern5M.Contains("MORNING STAR") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Morning Star (5M)",
                    Message = $"Morning star reversal at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Bullish Harami
            if (pattern5M.Contains("BULLISH HARAMI") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Bullish Harami (5M)",
                    Message = $"Bullish harami at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 2,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Piercing Pattern
            if (pattern5M.Contains("PIERCING") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Piercing Pattern (5M)",
                    Message = $"Piercing pattern at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 2,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Three White Soldiers
            if (pattern5M.Contains("THREE WHITE SOLDIERS") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Three White Soldiers (5M)",
                    Message = $"Three white soldiers at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Three Inside Up
            if (pattern5M.Contains("THREE INSIDE UP") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Three Inside Up (5M)",
                    Message = $"Three inside up at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Inverted Hammer
            if (pattern5M.Contains("INVERTED HAMMER") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Inverted Hammer (5M)",
                    Message = $"Inverted hammer at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 2,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Abandoned Baby
            if (pattern5M.Contains("ABANDONED BABY") && nearSupport)
            {
                signals.Add(new TradingSignal
                {
                    Type = "BUY",
                    Pattern = "Abandoned Baby (5M)",
                    Message = $"Abandoned baby reversal at support {viewModel.Analysis5M.Support:F4} [Trend aligns: BULLISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }
        }

        // SELL signals - ONLY in BEARISH market
        if (isOverallBearish)
        {
            // Shooting Star
            if (pattern5M.Contains("SHOOTING STAR") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Shooting Star (5M)",
                    Message = $"Shooting star at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Bearish Engulfing
            if (pattern5M.Contains("BEARISH ENGULFING") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Bearish Engulfing (5M)",
                    Message = $"Bearish engulfing at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Hanging Man
            if (pattern5M.Contains("HANGING MAN") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Hanging Man (5M)",
                    Message = $"Hanging man at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 2,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Evening Star
            if (pattern5M.Contains("EVENING STAR") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Evening Star (5M)",
                    Message = $"Evening star at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Bearish Harami
            if (pattern5M.Contains("BEARISH HARAMI") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Bearish Harami (5M)",
                    Message = $"Bearish harami at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 2,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Dark Cloud Cover
            if (pattern5M.Contains("DARK CLOUD") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Dark Cloud Cover (5M)",
                    Message = $"Dark cloud cover at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 2,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Three Black Crows
            if (pattern5M.Contains("THREE BLACK CROWS") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Three Black Crows (5M)",
                    Message = $"Three black crows at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Three Inside Down
            if (pattern5M.Contains("THREE INSIDE DOWN") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Three Inside Down (5M)",
                    Message = $"Three inside down at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }

            // Tweezers Top
            if (pattern5M.Contains("TWEEZERS TOP") && nearResistance)
            {
                signals.Add(new TradingSignal
                {
                    Type = "SELL",
                    Pattern = "Tweezers Top (5M)",
                    Message = $"Double top at resistance {viewModel.Analysis5M.Resistance:F4} [Trend aligns: BEARISH]",
                    Price = viewModel.Candles5M.Last().Close,
                    Strength = 3,
                    Time = DateTime.UtcNow,
                    Timeframe = "5M"
                });
            }
        }

        // ============ STEP 4: Add CAUTION signals for context (regardless of trend) ============

        // Three Black Crows on 15M
        if (pattern15M.Contains("THREE BLACK CROWS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Three Black Crows (15M)",
                Message = "Bearish continuation pattern on 15M - confirms downtrend",
                Price = viewModel.Candles15M.Any() ? viewModel.Candles15M.Last().Close : 0,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        // Three Black Crows on 1H
        if (pattern1H.Contains("THREE BLACK CROWS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Three Black Crows (1H)",
                Message = "Strong bearish continuation on 1H - confirms downtrend",
                Price = viewModel.Candles1H.Any() ? viewModel.Candles1H.Last().Close : 0,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "1H"
            });
        }

        // Three White Soldiers on 15M
        if (pattern15M.Contains("THREE WHITE SOLDIERS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Three White Soldiers (15M)",
                Message = "Bullish continuation pattern on 15M - confirms uptrend",
                Price = viewModel.Candles15M.Any() ? viewModel.Candles15M.Last().Close : 0,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        // Three White Soldiers on 1H
        if (pattern1H.Contains("THREE WHITE SOLDIERS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Three White Soldiers (1H)",
                Message = "Strong bullish continuation on 1H - confirms uptrend",
                Price = viewModel.Candles1H.Any() ? viewModel.Candles1H.Last().Close : 0,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "1H"
            });
        }

        // Evening Star on 15M
        if (pattern15M.Contains("EVENING STAR") && isOverallBearish)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Evening Star (15M)",
                Message = "Evening star reversal - confirms bearish trend",
                Price = viewModel.Candles15M.Any() ? viewModel.Candles15M.Last().Close : 0,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        // Morning Star on 15M
        if (pattern15M.Contains("MORNING STAR") && isOverallBullish)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Morning Star (15M)",
                Message = "Morning star reversal - confirms bullish trend",
                Price = viewModel.Candles15M.Any() ? viewModel.Candles15M.Last().Close : 0,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        // ============ STEP 5: Add warning when pattern contradicts trend ============

        // Bullish patterns in bearish market (warning)
        if (isOverallBearish && (pattern5M.Contains("HAMMER") || pattern5M.Contains("TWEEZERS BOTTOM") ||
            pattern5M.Contains("BULLISH ENGULFING") || pattern5M.Contains("MORNING STAR") ||
            pattern5M.Contains("BULLISH HARAMI") || pattern5M.Contains("PIERCING") ||
            pattern5M.Contains("THREE WHITE SOLDIERS") || pattern5M.Contains("INVERTED HAMMER")))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Pattern Ignored",
                Message = $"Bullish pattern detected but market is BEARISH. Wait for trend to reverse.",
                Price = viewModel.Candles5M.Last().Close,
                Strength = 1,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // Bearish patterns in bullish market (warning)
        if (isOverallBullish && (pattern5M.Contains("SHOOTING STAR") || pattern5M.Contains("BEARISH ENGULFING") ||
            pattern5M.Contains("HANGING MAN") || pattern5M.Contains("EVENING STAR") ||
            pattern5M.Contains("BEARISH HARAMI") || pattern5M.Contains("DARK CLOUD") ||
            pattern5M.Contains("THREE BLACK CROWS") || pattern5M.Contains("TWEEZERS TOP")))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Pattern Ignored",
                Message = $"Bearish pattern detected but market is BULLISH. Wait for trend to reverse.",
                Price = viewModel.Candles5M.Last().Close,
                Strength = 1,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // Remove duplicates
        var uniqueSignals = new List<TradingSignal>();
        foreach (var signal in signals)
        {
            if (!uniqueSignals.Any(s => s.Type == signal.Type && s.Pattern == signal.Pattern))
                uniqueSignals.Add(signal);
        }

        return uniqueSignals;
    }
}
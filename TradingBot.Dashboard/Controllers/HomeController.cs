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
                // Historical mode - load data for backtest
                viewModel.Candles1H = await _binanceService.GetHistoricalCandlesAsync(symbol, "1H", startDate.Value, endDate.Value);
                viewModel.Candles15M = await _binanceService.GetHistoricalCandlesAsync(symbol, "15m", startDate.Value.AddDays(-5), endDate.Value);
                viewModel.Candles5M = await _binanceService.GetHistoricalCandlesAsync(symbol, "5m", startDate.Value, endDate.Value);

                // Analyze each timeframe
                viewModel.Analysis1H = _binanceService.AnalyzeTimeframe(viewModel.Candles1H, "1H");
                viewModel.Analysis15M = _binanceService.AnalyzeTimeframe(viewModel.Candles15M, "15M");
                viewModel.Analysis5M = _binanceService.AnalyzeTimeframe(viewModel.Candles5M, "5M");

                // Run backtest
                viewModel.Metrics = await _binanceService.RunBacktestAsync(symbol, startDate.Value, endDate.Value);

                // Generate signals for display
                viewModel.Signals = GenerateSignalsFromAnalysis(viewModel);
            }
            else
            {
                // Live mode - get recent data
                viewModel.Candles1H = await _binanceService.GetCandlesAsync(symbol, "1H", 48);
                viewModel.Candles15M = await _binanceService.GetCandlesAsync(symbol, "15m", 24);
                viewModel.Candles5M = await _binanceService.GetCandlesAsync(symbol, "5m", 12);

                // Analyze each timeframe
                viewModel.Analysis1H = _binanceService.AnalyzeTimeframe(viewModel.Candles1H, "1H");
                viewModel.Analysis15M = _binanceService.AnalyzeTimeframe(viewModel.Candles15M, "15M");
                viewModel.Analysis5M = _binanceService.AnalyzeTimeframe(viewModel.Candles5M, "5M");

                // Generate signals
                viewModel.Signals = GenerateSignalsFromAnalysis(viewModel);
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

    private List<TradingSignal> GenerateSignalsFromAnalysis(ChartViewModel viewModel)
    {
        var signals = new List<TradingSignal>();

        if (!viewModel.Candles5M.Any()) return signals;

        var lastPrice5M = viewModel.Candles5M.Last().Close;
        var lastPrice15M = viewModel.Candles15M.Any() ? viewModel.Candles15M.Last().Close : lastPrice5M;
        var lastPrice1H = viewModel.Candles1H.Any() ? viewModel.Candles1H.Last().Close : lastPrice5M;

        // 5M SIGNALS
        var pattern5M = viewModel.Analysis5M.Pattern.ToUpper();

        // BUY: Tweezers Bottom at support
        if (pattern5M.Contains("TWEEZERS BOTTOM") && viewModel.Analysis5M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Tweezers Bottom (5M)",
                Message = $"Double bottom at support {viewModel.Analysis5M.Support:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // BUY: Doji at support
        if (pattern5M.Contains("DOJI") && viewModel.Analysis5M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Doji at Support (5M)",
                Message = $"Doji at support {viewModel.Analysis5M.Support:F4}",
                Price = lastPrice5M,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // BUY: Hammer at support
        if (pattern5M.Contains("HAMMER") && viewModel.Analysis5M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Hammer at Support (5M)",
                Message = $"Hammer at support {viewModel.Analysis5M.Support:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // BUY: Bullish Engulfing
        if (pattern5M.Contains("BULLISH ENGULFING") && viewModel.Analysis5M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Bullish Engulfing (5M)",
                Message = $"Bullish engulfing at support {viewModel.Analysis5M.Support:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // BUY: Morning Star
        if (pattern5M.Contains("MORNING STAR") && viewModel.Analysis5M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Morning Star (5M)",
                Message = $"Morning star at support {viewModel.Analysis5M.Support:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // SELL: Shooting Star at resistance
        if (pattern5M.Contains("SHOOTING STAR") && viewModel.Analysis5M.NearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Shooting Star (5M)",
                Message = $"Shooting star at resistance {viewModel.Analysis5M.Resistance:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // SELL: Bearish Engulfing at resistance
        if (pattern5M.Contains("BEARISH ENGULFING") && viewModel.Analysis5M.NearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Bearish Engulfing (5M)",
                Message = $"Bearish engulfing at resistance {viewModel.Analysis5M.Resistance:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // SELL: Evening Star at resistance
        if (pattern5M.Contains("EVENING STAR") && viewModel.Analysis5M.NearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Evening Star (5M)",
                Message = $"Evening star at resistance {viewModel.Analysis5M.Resistance:F4}",
                Price = lastPrice5M,
                Strength = 3,
                Time = DateTime.UtcNow,
                Timeframe = "5M"
            });
        }

        // 15M SIGNALS
        var pattern15M = viewModel.Analysis15M.Pattern.ToUpper();

        if (pattern15M.Contains("TWEEZERS BOTTOM") && viewModel.Analysis15M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Tweezers Bottom (15M)",
                Message = $"Double bottom on 15M at support {viewModel.Analysis15M.Support:F4}",
                Price = lastPrice15M,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        if (pattern15M.Contains("MORNING STAR") && viewModel.Analysis15M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Morning Star (15M)",
                Message = $"Morning star on 15M at support {viewModel.Analysis15M.Support:F4}",
                Price = lastPrice15M,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        if (pattern15M.Contains("THREE BLACK CROWS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Three Black Crows (15M)",
                Message = "Three black crows on 15M - bearish continuation",
                Price = lastPrice15M,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        if (pattern15M.Contains("EVENING STAR") && viewModel.Analysis15M.NearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = "Evening Star (15M)",
                Message = $"Evening star on 15M at resistance {viewModel.Analysis15M.Resistance:F4}",
                Price = lastPrice15M,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "15M"
            });
        }

        // 1H SIGNALS
        var pattern1H = viewModel.Analysis1H.Pattern.ToUpper();

        if (pattern1H.Contains("THREE BLACK CROWS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "CAUTION",
                Pattern = "Three Black Crows (1H)",
                Message = "Three black crows on 1H - strong bearish continuation",
                Price = lastPrice1H,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "1H"
            });
        }

        if (pattern1H.Contains("THREE WHITE SOLDIERS"))
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = "Three White Soldiers (1H)",
                Message = "Three white soldiers on 1H - strong bullish continuation",
                Price = lastPrice1H,
                Strength = 2,
                Time = DateTime.UtcNow,
                Timeframe = "1H"
            });
        }

        // Remove duplicates
        var uniqueSignals = new List<TradingSignal>();
        foreach (var signal in signals)
        {
            bool exists = false;
            foreach (var existing in uniqueSignals)
            {
                if (existing.Type == signal.Type && existing.Pattern == signal.Pattern)
                {
                    exists = true;
                    break;
                }
            }
            if (!exists)
            {
                uniqueSignals.Add(signal);
            }
        }

        // Sort: BUY first, then SELL, then CAUTION
        return uniqueSignals
            .OrderBy(s => s.Type == "BUY" ? 0 : (s.Type == "SELL" ? 1 : 2))
            .ThenByDescending(s => s.Strength)
            .ToList();
    }
}
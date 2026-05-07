using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using TradingBot.Core.Models;
using TradingBot.Core.Indicators;
using TradingBot.Dashboard.Hubs;
using TradingBot.Dashboard.Models;
using TradingBot.Dashboard.Services;

namespace TradingBot.Dashboard.Controllers;

public class SimulationController : Controller
{
    private readonly BinanceService _binanceService;
    private readonly IHubContext<SimulationHub> _hubContext;

    public SimulationController(IHubContext<SimulationHub> hubContext)
    {
        _binanceService = new BinanceService();
        _hubContext = hubContext;
    }

    public IActionResult Index()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> StartSimulation(string symbol, DateTime startDate, DateTime endDate, int speedMs = 100)
    {
        // For single day, set end date to start date + 1 day (full 24 hours)
        if (startDate.Date == endDate.Date)
        {
            endDate = startDate.AddDays(1);
        }

        // Start simulation in background
        _ = Task.Run(async () => await RunSimulationAsync(symbol, startDate, endDate, speedMs));

        return Ok(new { message = "Simulation started" });
    }

    private async Task RunSimulationAsync(string symbol, DateTime startDate, DateTime endDate, int speedMs)
    {
        try
        {
            // Ensure dates are valid
            // For single day simulation, add one day to end date
            if (startDate.Date == endDate.Date)
            {
                endDate = endDate.AddDays(1);
                await _hubContext.Clients.All.SendAsync("ReceiveProgress", new { current = 0, total = 0, message = $"Single day selected: {startDate:yyyy-MM-dd}. Running full 24 hours." });
            }

            // Ensure start date is before end date
            if (startDate >= endDate)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveError", new { message = "Start date must be before end date" });
                return;
            }

            // Limit date range to avoid excessive data
            var maxDays = (endDate - startDate).TotalDays;
            if (maxDays > 30)
            {
                await _hubContext.Clients.All.SendAsync("ReceiveError", new { message = "Date range cannot exceed 30 days for simulation" });
                return;
            }

            await _hubContext.Clients.All.SendAsync("ReceiveProgress", new { current = 0, total = 0, message = $"Fetching historical data for {symbol}..." });

            // Calculate warmup start (7 days before, but not before 2020)
            var warmupStart = startDate.AddDays(-7);
            if (warmupStart < new DateTime(2020, 1, 1))
                warmupStart = new DateTime(2020, 1, 1);

            // Fetch data with safe date ranges
            var allCandles1H = await _binanceService.GetHistoricalCandlesAsync(symbol, "1H", warmupStart, endDate);
            var allCandles15M = await _binanceService.GetHistoricalCandlesAsync(symbol, "15m", warmupStart, endDate);
            var allCandles5M = await _binanceService.GetHistoricalCandlesAsync(symbol, "5m", warmupStart, endDate);

            // Rest of the method remains the same...
        }
        catch (Exception ex)
        {
            await _hubContext.Clients.All.SendAsync("ReceiveError", new { message = ex.Message });
        }
    }


    //private async Task RunSimulationAsync(string symbol, DateTime startDate, DateTime endDate, int speedMs)
    //{
    //    try
    //    {
    //        await _hubContext.Clients.All.SendAsync("ReceiveProgress", new { current = 0, total = 0, message = $"Fetching historical data for {symbol}..." });

    //        // Fetch all historical data with warmup
    //        var warmupStart = startDate.AddDays(-7);
    //        var allCandles1H = await _binanceService.GetHistoricalCandlesAsync(symbol, "1H", warmupStart, endDate);
    //        var allCandles15M = await _binanceService.GetHistoricalCandlesAsync(symbol, "15m", warmupStart, endDate);
    //        var allCandles5M = await _binanceService.GetHistoricalCandlesAsync(symbol, "5m", warmupStart, endDate);

    //        // Filter to simulation range
    //        var candles5M = allCandles5M.Where(c => c.OpenTime >= startDate).ToList();
    //        var candles15M = allCandles15M.Where(c => c.OpenTime >= startDate).ToList();
    //        var candles1H = allCandles1H.Where(c => c.OpenTime >= startDate).ToList();

    //        int totalCandles = candles5M.Count;

    //        await _hubContext.Clients.All.SendAsync("ReceiveProgress", new { current = 0, total = totalCandles, message = $"Starting simulation. Total candles: {totalCandles}" });

    //        // Initialize state
    //        var state = new SimulationState
    //        {
    //            Symbol = symbol,
    //            StartDate = startDate,
    //            EndDate = endDate,
    //            TotalCandles = totalCandles,
    //            CurrentCandle = 0,
    //            IsRunning = true,
    //            StatusMessage = "Simulation running..."
    //        };

    //        // Store warmup candles for analysis
    //        var warmupCandles5M = allCandles5M.Where(c => c.OpenTime < startDate).ToList();
    //        var warmupCandles15M = allCandles15M.Where(c => c.OpenTime < startDate).ToList();
    //        var warmupCandles1H = allCandles1H.Where(c => c.OpenTime < startDate).ToList();

    //        // Start with warmup data
    //        state.Candles5M.AddRange(warmupCandles5M);
    //        state.Candles15M.AddRange(warmupCandles15M);
    //        state.Candles1H.AddRange(warmupCandles1H);

    //        // Simulate candle by candle
    //        for (int i = 0; i < candles5M.Count; i++)
    //        {
    //            var currentCandle = candles5M[i];
    //            var currentTime = currentCandle.OpenTime;

    //            // Add current candle
    //            state.Candles5M.Add(currentCandle);

    //            // Add 15M candle if matches current time
    //            var matching15M = candles15M.Where(c => c.OpenTime == currentTime).ToList();
    //            foreach (var candle in matching15M)
    //                state.Candles15M.Add(candle);

    //            // Add 1H candle if matches current time
    //            var matching1H = candles1H.Where(c => c.OpenTime == currentTime).ToList();
    //            foreach (var candle in matching1H)
    //                state.Candles1H.Add(candle);

    //            state.CurrentTime = currentTime;
    //            state.CurrentCandle = i + 1;

    //            // Analyze current state
    //            state.Analysis1H = _binanceService.AnalyzeTimeframe(state.Candles1H, "1H");
    //            state.Analysis15M = _binanceService.AnalyzeTimeframe(state.Candles15M, "15M");
    //            state.Analysis5M = _binanceService.AnalyzeTimeframe(state.Candles5M, "5M");

    //            // Generate signals
    //            var newSignals = GenerateSignals(state.Analysis1H, state.Analysis15M, state.Analysis5M, currentCandle.Close, currentTime);

    //            foreach (var signal in newSignals)
    //            {
    //                if (!state.Signals.Any(s => s.Time == signal.Time && s.Type == signal.Type))
    //                {
    //                    state.Signals.Add(signal);
    //                    await _hubContext.Clients.All.SendAsync("ReceiveSignal", signal);
    //                }
    //            }

    //            // Calculate progress
    //            state.ProgressPercent = (double)(i + 1) / totalCandles * 100;
    //            state.StatusMessage = $"Processing: {currentTime:HH:mm:ss} | Candle {i + 1}/{totalCandles}";

    //            // Send state update
    //            await _hubContext.Clients.All.SendAsync("ReceiveState", state);
    //            await _hubContext.Clients.All.SendAsync("ReceiveProgress", new { current = i + 1, total = totalCandles, message = state.StatusMessage });

    //            // Simulate real-time delay
    //            await Task.Delay(speedMs);
    //        }

    //        // Simulation complete
    //        state.IsRunning = false;
    //        state.IsComplete = true;
    //        state.StatusMessage = "Simulation complete!";

    //        // Calculate summary
    //        var buySignals = state.Signals.Where(s => s.Type == "BUY").ToList();
    //        var sellSignals = state.Signals.Where(s => s.Type == "SELL").ToList();

    //        var complete = new SimulationComplete
    //        {
    //            TotalTrades = state.Signals.Count,
    //            WinningTrades = state.Signals.Count(s => s.Type == "BUY"), // Simplified
    //            LosingTrades = state.Signals.Count(s => s.Type == "SELL"),
    //            WinRate = state.Signals.Count > 0 ? (double)state.Signals.Count(s => s.Type == "BUY") / state.Signals.Count * 100 : 0,
    //            AllSignals = state.Signals
    //        };

    //        await _hubContext.Clients.All.SendAsync("ReceiveComplete", complete);
    //        await _hubContext.Clients.All.SendAsync("ReceiveState", state);
    //    }
    //    catch (Exception ex)
    //    {
    //        await _hubContext.Clients.All.SendAsync("ReceiveError", new { message = ex.Message });
    //    }
    //}

    private List<TradingSignal> GenerateSignals(AnalysisResult analysis1H, AnalysisResult analysis15M, AnalysisResult analysis5M, decimal currentPrice, DateTime currentTime)
    {
        var signals = new List<TradingSignal>();

        bool isBullish = analysis15M.Trend.Contains("UPTREND") || analysis1H.Trend.Contains("UPTREND");
        bool isBearish = analysis15M.Trend.Contains("DOWNTREND") || analysis1H.Trend.Contains("DOWNTREND");

        var pattern5M = analysis5M.Pattern.ToUpper();

        // BUY signals
        if (isBullish && (pattern5M.Contains("HAMMER") || pattern5M.Contains("TWEEZERS BOTTOM") ||
                          pattern5M.Contains("BULLISH ENGULFING") || pattern5M.Contains("MORNING STAR")) && analysis5M.NearSupport)
        {
            signals.Add(new TradingSignal
            {
                Type = "BUY",
                Pattern = pattern5M,
                Message = $"{pattern5M} at support {analysis5M.Support:F4}",
                Price = currentPrice,
                Strength = 3,
                Time = currentTime,
                Timeframe = "5M"
            });
        }

        // SELL signals
        if (isBearish && (pattern5M.Contains("SHOOTING STAR") || pattern5M.Contains("BEARISH ENGULFING") ||
                          pattern5M.Contains("EVENING STAR") || pattern5M.Contains("HANGING MAN")) && analysis5M.NearResistance)
        {
            signals.Add(new TradingSignal
            {
                Type = "SELL",
                Pattern = pattern5M,
                Message = $"{pattern5M} at resistance {analysis5M.Resistance:F4}",
                Price = currentPrice,
                Strength = 3,
                Time = currentTime,
                Timeframe = "5M"
            });
        }

        return signals;
    }
}
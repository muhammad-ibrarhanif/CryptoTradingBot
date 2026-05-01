using TradingBot.Backtester.Models;

namespace TradingBot.Backtester;

public static class MultiCoinScalper
{
    public static async Task<Dictionary<string, BacktestResult>> RunAsync(
        DateTime start,
        DateTime end,
        decimal initialBalance = 3000m,
        decimal riskPerCoinPercent = 0.5m,
        int maxCoins = 3,
        int activeHourStart = 8,
        int activeHourEnd = 18)
    {
        Console.WriteLine($"\n=== MULTI-COIN SCALPING BACKTEST ===");
        Console.WriteLine($"Period: {start:yyyy-MM-dd} to {end:yyyy-MM-dd}");
        Console.WriteLine($"Active Hours: {activeHourStart:00}:00 - {activeHourEnd:00}:00 UTC");
        Console.WriteLine($"Max Coins: {maxCoins} | Risk per coin: {riskPerCoinPercent}%");

        var results = new Dictionary<string, BacktestResult>();
        decimal totalBalance = initialBalance;
        decimal initialTotal = initialBalance;

        var currentTime = new DateTime(start.Year, start.Month, start.Day, activeHourStart, 0, 0);
        if (currentTime < start) currentTime = currentTime.AddDays(1);

        while (currentTime < end)
        {
            if (currentTime.Hour < activeHourStart || currentTime.Hour >= activeHourEnd)
            {
                currentTime = currentTime.Date.AddDays(1).AddHours(activeHourStart);
                continue;
            }

            Console.WriteLine($"\n[Scanning at {currentTime:yyyy-MM-dd HH:mm}]");
            var trends = await TrendScanner.ScanTrendsAsync(currentTime);
            var topCoins = trends.Where(t => t.IsUptrend && t.Score > 30)
                                 .Take(maxCoins)
                                 .Select(t => t.Symbol)
                                 .ToList();

            if (!topCoins.Any())
            {
                Console.WriteLine("  No uptrend coins found");
                currentTime = currentTime.AddHours(1);
                continue;
            }

            Console.WriteLine($"  Trading: {string.Join(", ", topCoins)}");
            var coinBalance = totalBalance / topCoins.Count;

            foreach (var symbol in topCoins)
            {
                var windowEnd = currentTime.AddHours(4);
                if (windowEnd > end) windowEnd = end;

                var coinResult = await ScalpingStrategy.RunAsync(
                    symbol, currentTime, windowEnd, coinBalance,
                    riskPercent: 0.5m,
                    rsiOversold: 45,           // Much easier
                    useVolumeFilter: false,    // Disable volume filter
                    useEmaFilter: false,       // Disable EMA filter
                    useTrendFilter: true,
                    trendTimeframe: "1H",
                    verbose: false
                );

                if (!results.ContainsKey(symbol))
                {
                    results[symbol] = coinResult;
                }
                else
                {
                    var existing = results[symbol];
                    existing.EndingBalance = coinResult.EndingBalance;
                    existing.TotalTrades += coinResult.TotalTrades;
                    existing.WinningTrades += coinResult.WinningTrades;
                    existing.WinRate = existing.TotalTrades > 0
                        ? (double)existing.WinningTrades / existing.TotalTrades * 100
                        : 0;
                    existing.TotalReturnPercent = (existing.EndingBalance - existing.StartingBalance) / existing.StartingBalance * 100m;
                }

                totalBalance = totalBalance - coinBalance + coinResult.EndingBalance;
            }

            currentTime = currentTime.AddHours(4);
        }

        decimal totalReturn = (totalBalance - initialTotal) / initialTotal * 100m;
        decimal days = (end - start).Days;
        decimal dailyProfit = days > 0 ? (totalBalance - initialTotal) / days : 0;

        Console.WriteLine($"\n=== FINAL RESULTS ===");
        Console.WriteLine($"Start: ${initialTotal:F2} | End: ${totalBalance:F2}");
        Console.WriteLine($"Return: {totalReturn:F2}% | Daily: ${dailyProfit:F2}");
        Console.WriteLine($"Goal $10/day: {(dailyProfit >= 10 ? "ACHIEVED" : "NOT YET")}");

        return results;
    }
}
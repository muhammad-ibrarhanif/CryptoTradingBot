namespace TradingBot.Backtester;

internal class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== TRADINGVIEW STYLE VISUAL CHART ===\n");

        var startTime = new DateTime(2026, 5, 1, 12, 0, 0);
        var endTime = new DateTime(2026, 5, 1, 16, 0, 0);

        await VisualChart.DisplayAsync("SOLUSDT", startTime, endTime);

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}

//namespace TradingBot.Backtester;

//internal class Program
//{
//    static async Task Main(string[] args)
//    {
//        Console.WriteLine("=== CryptoTradingBot Chart Analyzer ===");

//        // Analyze the exact trade time
//        var tradeTime = new DateTime(2026, 5, 1, 14, 35, 0);

//        Console.WriteLine($"Analyzing SOLUSDT at trade time: {tradeTime:yyyy-MM-dd HH:mm} UTC\n");

//        await ChartAnalyzer.AnalyzeAsync("SOLUSDT", tradeTime);

//        Console.WriteLine("\n========================================");
//        Console.WriteLine("VERIFY WITH TRADINGVIEW:");
//        Console.WriteLine("========================================");
//        Console.WriteLine("1. On your 1H chart, look at 12:00-14:00 UTC");
//        Console.WriteLine("2. Are highs getting higher? Are lows getting higher?");
//        Console.WriteLine("3. That should say UPTREND ✓");
//        Console.WriteLine("\nPress any key to exit...");
//        Console.ReadKey();
//    }
//}


//namespace TradingBot.Backtester;

//internal class Program
//{
//    static async Task Main(string[] args)
//    {
//        Console.WriteLine("=== CryptoTradingBot Backtester ===");
//        Console.WriteLine("0 = Natural Scalper (5m, Human-like)");
//        Console.WriteLine("1 = Multi-Coin Scalping");
//        Console.WriteLine("2 = Single Coin Diagnostic");
//        var choice = Console.ReadLine();

//        if (choice == "0")
//        {
//            // Extend the backtest by 2 days so trades can complete
//            var result = await NaturalScalper.RunAsync(
//                "SOLUSDT",
//                new DateTime(2026, 5, 1, 0, 0, 0),
//                new DateTime(2026, 5, 3, 0, 0, 0),    // 2 days instead of 1
//                initialBalance: 3000m,
//                riskPercent: 1.0m
//            );

//            Console.WriteLine($"\n=== FINAL RESULT ===");
//            Console.WriteLine($"Trades: {result.TotalTrades}");
//            Console.WriteLine($"Wins: {result.WinningTrades}");
//            Console.WriteLine($"Win Rate: {result.WinRate:F1}%");
//            Console.WriteLine($"Return: {result.TotalReturnPercent:F2}%");
//            Console.WriteLine($"Profit: ${(result.EndingBalance - result.StartingBalance):F2}");
//            Console.WriteLine($"Daily Avg: ${(result.EndingBalance - result.StartingBalance) / 2:F2}");
//        }
//        else
//        if (choice == "1")
//        {
//            var start = new DateTime(2025, 1, 15);
//            var end = new DateTime(2025, 1, 16);

//            await MultiCoinScalper.RunAsync(start, end, 3000m, 0.5m, 3, 8, 18);
//        }
//        else if (choice == "2")
//        {
//            var result = await ScalpingStrategy.RunAsync(
//                "XRPUSDT",
//                new DateTime(2025, 1, 15, 8, 0, 0),
//                new DateTime(2025, 1, 15, 16, 0, 0),
//                initialBalance: 3000m,
//                riskPercent: 0.5m,
//                useTrendFilter: true,
//                trendTimeframe: "1H",
//                verbose: true
//            );

//            Console.WriteLine($"\nTrades: {result.TotalTrades} | Return: {result.TotalReturnPercent:F2}%");
//        }
//    }
//}
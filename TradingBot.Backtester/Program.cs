using Binance.Net.Enums;
using TradingBot.Backtester;

Console.WriteLine("Starting backtest...");

// Example: Fetch 7 days of 1‑minute data and 7 days of 1‑hour data
var start = DateTime.UtcNow.AddDays(-7);
var end = DateTime.UtcNow;

var oneMinCandles = await BinanceDataLoader.FetchKlinesAsync("SOLUSDT", KlineInterval.OneMinute, start, end);
var oneHourCandles = await BinanceDataLoader.FetchKlinesAsync("SOLUSDT", KlineInterval.OneHour, start, end);

Console.WriteLine($"Fetched {oneMinCandles.Count} 1m candles and {oneHourCandles.Count} 1h candles.");

// TODO: Run the detectors on the 1h candles to build structure/zones
// TODO: Walk through 1m candles, check for entry signals, simulate trades
// TODO: Print performance metrics

Console.WriteLine("Backtest complete.");
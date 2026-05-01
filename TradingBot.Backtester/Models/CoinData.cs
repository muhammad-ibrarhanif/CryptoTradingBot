using TradingBot.Core.Models;

namespace TradingBot.Backtester.Models;

public class CoinData
{
    public string Symbol { get; set; } = "";
    public decimal CurrentPrice { get; set; }
    public decimal TrendStrength { get; set; }
    public decimal Rsi { get; set; }
    public decimal VolumeRatio { get; set; }
    public bool IsUptrend { get; set; }
    public List<Candle> Candles1m { get; set; } = new();
    public List<Candle> Candles1H { get; set; } = new();
}
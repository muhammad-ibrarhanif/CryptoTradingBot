namespace TradingBot.Backtester.Models;

public class TrendScore
{
    public string Symbol { get; set; } = "";
    public decimal Score { get; set; }
    public bool IsUptrend { get; set; }
    public decimal PriceChangePercent { get; set; }
    public decimal Rsi { get; set; }
    public decimal VolumeMomentum { get; set; }
}
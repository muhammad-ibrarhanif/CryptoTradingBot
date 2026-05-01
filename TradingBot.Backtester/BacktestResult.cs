namespace TradingBot.Backtester;

public class BacktestResult
{
    public string StrategyName { get; set; } = "";
    public decimal StartingBalance { get; set; }
    public decimal EndingBalance { get; set; }
    public int TotalTrades { get; set; }
    public int WinningTrades { get; set; }
    public double WinRate { get; set; }
    public decimal TotalReturnPercent { get; set; }
    public decimal AvgReturnPerTrade { get; set; }
}
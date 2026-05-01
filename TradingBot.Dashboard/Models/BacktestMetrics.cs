namespace TradingBot.Dashboard.Models
{
    public class BacktestMetrics
    {
        public int TotalSignals { get; set; }
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double WinRate { get; set; }
        public decimal TotalPnL { get; set; }
        public decimal TotalPnLPercent { get; set; }
        public decimal AvgWin { get; set; }
        public decimal AvgLoss { get; set; }
        public decimal ProfitFactor { get; set; }
        public decimal MaxDrawdown { get; set; }
        public string BestPattern { get; set; } = "";
        public int BestPatternWins { get; set; }
    }
}

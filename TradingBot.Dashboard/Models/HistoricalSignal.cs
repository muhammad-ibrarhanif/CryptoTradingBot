namespace TradingBot.Dashboard.Models
{
    public class HistoricalSignal
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = ""; // BUY or SELL
        public string Pattern { get; set; } = "";
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal PnL { get; set; }
        public decimal PnLPercent { get; set; }
        public bool IsWinner { get; set; }
        public string Timeframe { get; set; } = "";
    }
}

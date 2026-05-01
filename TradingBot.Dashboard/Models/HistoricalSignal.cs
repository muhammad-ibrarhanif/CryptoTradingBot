namespace TradingBot.Dashboard.Models
{
    public class HistoricalSignal
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } = "";
        public string Pattern { get; set; } = "";
        public decimal Price { get; set; }
        public decimal Support { get; set; }
        public decimal Resistance { get; set; }
        public string Trend1H { get; set; } = "";
        public string Trend15M { get; set; } = "";
        public string Reason { get; set; } = "";
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal PnL { get; set; }
        public decimal PnLPercent { get; set; }
        public bool IsWinner { get; set; }
        public string Timeframe { get; set; } = "";
    }
}

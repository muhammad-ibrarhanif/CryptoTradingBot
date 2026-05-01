namespace TradingBot.Dashboard.Models
{
    public class TradingSignal
    {
        public string Type { get; set; } = "";      // BUY, SELL, CAUTION
        public string Pattern { get; set; } = "";   // Pattern name
        public string Message { get; set; } = "";   // Description
        public decimal Price { get; set; }          // Price at signal
        public int Strength { get; set; }           // 1, 2, or 3 stars
        public DateTime Time { get; set; }          // Signal time
        public string Timeframe { get; set; } = ""; // 5M, 15M, 1H
    }
}

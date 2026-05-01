namespace TradingBot.Dashboard.Models
{
    public class AnalysisResult
    {
        public string Trend { get; set; } = "Analyzing...";
        public decimal Support { get; set; }
        public decimal Resistance { get; set; }
        public bool NearSupport { get; set; }
        public bool NearResistance { get; set; }
        public string Pattern { get; set; } = "";
        public int HigherHighs { get; set; }
        public int HigherLows { get; set; }
    }
}

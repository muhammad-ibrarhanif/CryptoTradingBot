using TradingBot.Core.Models;

namespace TradingBot.Dashboard.Models
{
    public class ChartViewModel
    {
        public string Symbol { get; set; } = "SOLUSDT";
        public DateTime StartDate { get; set; } = DateTime.UtcNow.AddDays(-7);
        public DateTime EndDate { get; set; } = DateTime.UtcNow;
        public bool IsHistorical { get; set; } = false;

        public List<Candle> Candles1H { get; set; } = new();
        public List<Candle> Candles15M { get; set; } = new();
        public List<Candle> Candles5M { get; set; } = new();

        public List<TradingSignal> Signals { get; set; } = new();
        public List<HistoricalSignal> SignalHistory { get; set; } = new();
        public BacktestMetrics Metrics { get; set; } = new();

        public AnalysisResult Analysis1H { get; set; } = new();
        public AnalysisResult Analysis15M { get; set; } = new();
        public AnalysisResult Analysis5M { get; set; } = new();

        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
    }
}

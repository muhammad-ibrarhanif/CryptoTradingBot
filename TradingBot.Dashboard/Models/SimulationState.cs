using TradingBot.Core.Models;

namespace TradingBot.Dashboard.Models
{
    public class SimulationState
    {
        public string Symbol { get; set; } = "";
        public DateTime CurrentTime { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int TotalCandles { get; set; }
        public int CurrentCandle { get; set; }
        public double ProgressPercent { get; set; }

        public List<Candle> Candles1H { get; set; } = new();
        public List<Candle> Candles15M { get; set; } = new();
        public List<Candle> Candles5M { get; set; } = new();

        public List<TradingSignal> Signals { get; set; } = new();

        public AnalysisResult Analysis1H { get; set; } = new();
        public AnalysisResult Analysis15M { get; set; } = new();
        public AnalysisResult Analysis5M { get; set; } = new();

        public bool IsRunning { get; set; }
        public bool IsComplete { get; set; }
        public string StatusMessage { get; set; } = "";
    }

    public class SimulationComplete
    {
        public int TotalTrades { get; set; }
        public int WinningTrades { get; set; }
        public int LosingTrades { get; set; }
        public double WinRate { get; set; }
        public decimal TotalPnL { get; set; }
        public List<TradingSignal> AllSignals { get; set; } = new();
    }
}

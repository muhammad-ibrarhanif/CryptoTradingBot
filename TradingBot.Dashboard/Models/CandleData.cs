namespace TradingBot.Dashboard.Models;

public class CandleData
{
    public DateTime Time { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public decimal Volume { get; set; }
    public bool IsBullish => Close > Open;
    public decimal Body => Math.Abs(Close - Open);
    public decimal UpperWick => High - Math.Max(Open, Close);
    public decimal LowerWick => Math.Min(Open, Close) - Low;
}
namespace TradingBot.Core.Models;

public sealed class Candle
{
    public DateTime OpenTime { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
    public decimal Volume { get; init; }
    public DateTime CloseTime { get; init; }

    public bool IsBullish => Close > Open;
    public bool IsBearish => Close < Open;
    public decimal Body => Math.Abs(Close - Open);
    public decimal UpperWick => High - Math.Max(Open, Close);
    public decimal LowerWick => Math.Min(Open, Close) - Low;
    public decimal Range => High - Low;
}

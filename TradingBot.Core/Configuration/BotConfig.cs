namespace TradingBot.Core.Configuration;

/// <summary>
/// All bot configuration values sourced from appsettings.json BotConfig section.
/// No values are hardcoded — everything is configurable.
/// </summary>
public sealed class BotConfig
{
    /// <summary>Operating phase: PaperTrade, Testnet, or Live.</summary>
    public string Phase { get; set; } = "PaperTrade";

    /// <summary>Trading pair symbol, e.g. SOL/USDT.</summary>
    public string TradingPair { get; set; } = "SOL/USDT";

    /// <summary>Entry timeframe candle interval, e.g. 30m.</summary>
    public string EntryTimeframe { get; set; } = "30m";

    /// <summary>Structure timeframe used for swing point and market structure detection, e.g. 1H.</summary>
    public string StructureTimeframe { get; set; } = "1H";

    /// <summary>Regime timeframe used for broader trend context, e.g. 4H.</summary>
    public string RegimeTimeframe { get; set; } = "4H";

    /// <summary>Bias timeframe used for daily directional bias, e.g. 1D.</summary>
    public string BiasTimeframe { get; set; } = "1D";

    /// <summary>Risk percentage of account balance per individual scale entry (1 = 1%).</summary>
    public double RiskPerTradePct { get; set; } = 1.0;

    /// <summary>Maximum number of scale entries allowed per setup.</summary>
    public int MaxScales { get; set; } = 3;

    /// <summary>Maximum total risk percentage across all scales in one setup (3 = 3%).</summary>
    public double MaxTotalRiskPct { get; set; } = 3.0;

    /// <summary>Daily loss limit percentage — bot stops all trading when breached (3 = 3%).</summary>
    public double DailyLossLimitPct { get; set; } = 3.0;

    /// <summary>EMA period applied to the entry timeframe close prices.</summary>
    public int EmaPeriod { get; set; } = 20;

    /// <summary>RSI period applied to the entry timeframe close prices.</summary>
    public int RsiPeriod { get; set; } = 14;

    /// <summary>ATR period used for volatility calculations.</summary>
    public int AtrPeriod { get; set; } = 14;

    /// <summary>Number of candles to look back when scanning for swing points.</summary>
    public int SwingLookback { get; set; } = 100;

    /// <summary>Number of candles required on each side of a candidate to confirm a swing point.</summary>
    public int SwingConfirmCandles { get; set; } = 2;

    /// <summary>Percentage proximity within which two levels are merged into one zone (0.3 = 0.3%).</summary>
    public double ZoneClusterPct { get; set; } = 0.3;

    /// <summary>Minimum zone score for the zone to be considered active.</summary>
    public double ZoneMinScore { get; set; } = 5;

    /// <summary>Minimum zone score required before the bot will trade the zone.</summary>
    public double ZoneTradeMinScore { get; set; } = 8;

    /// <summary>Distance percentage within which price is considered approaching a zone (2 = 2%).</summary>
    public double ZoneProximityPct { get; set; } = 2.0;

    /// <summary>Percentage range that defines a zone as currently active around price (1 = 1%).</summary>
    public double ZoneActivePct { get; set; } = 1.0;

    /// <summary>Maximum candles after a break for a flip zone return to be valid before expiry.</summary>
    public int FlipZoneExpiry { get; set; } = 50;

    /// <summary>Minimum percentage break required to register a flip zone break (0.2 = 0.2%).</summary>
    public double FlipBreakMinPct { get; set; } = 0.2;

    /// <summary>Minimum wick extension percentage for a valid liquidity sweep (0.2 = 0.2%).</summary>
    public double LiquiditySweepMinPct { get; set; } = 0.2;

    /// <summary>Maximum candles after a swing point within which a liquidity sweep is valid.</summary>
    public int LiquiditySweepMaxCandles { get; set; } = 20;

    /// <summary>Minimum number of consecutive impulse candles required to define an Order Block.</summary>
    public int OrderBlockImpulseCandles { get; set; } = 3;

    /// <summary>Minimum total move percentage of the impulse to qualify as an Order Block (1.5 = 1.5%).</summary>
    public double OrderBlockImpulseMinPct { get; set; } = 1.5;

    /// <summary>Minimum gap percentage for a valid Fair Value Gap (0.2 = 0.2%).</summary>
    public double FairValueGapMinPct { get; set; } = 0.2;

    /// <summary>Minimum middle candle body percentage for a valid Fair Value Gap (0.5 = 0.5%).</summary>
    public double FairValueGapBodyMinPct { get; set; } = 0.5;

    /// <summary>Hard Stop Loss percentage from entry — bot exits 100% if hit (2 = 2%).</summary>
    public double HardStopPct { get; set; } = 2.0;

    /// <summary>Buffer added below zone bottom when placing the Stop Loss (0.1 = 0.1%).</summary>
    public double StopLossBufferPct { get; set; } = 0.1;

    /// <summary>Profit percentage at which the Stop Loss moves to breakeven (1 = 1%).</summary>
    public double BreakevenTriggerPct { get; set; } = 1.0;

    /// <summary>Number of candles after entry before the time-based stop triggers a 50% exit.</summary>
    public int TimeStopCandles { get; set; } = 12;

    /// <summary>RSI must be at or below this value at time of buy zone entry.</summary>
    public double RsiEntryMax { get; set; } = 40.0;

    /// <summary>RSI level that triggers a 25% exit (first RSI exit).</summary>
    public double RsiExit1 { get; set; } = 50.0;

    /// <summary>RSI level that triggers a 25% exit (second RSI exit).</summary>
    public double RsiExit2 { get; set; } = 60.0;

    /// <summary>RSI level that triggers a 50% exit (third RSI exit).</summary>
    public double RsiExit3 { get; set; } = 70.0;

    /// <summary>Percentage of position to exit when RSI crosses RsiExit1.</summary>
    public double RsiExit1Pct { get; set; } = 25.0;

    /// <summary>Percentage of position to exit when RSI crosses RsiExit2.</summary>
    public double RsiExit2Pct { get; set; } = 25.0;

    /// <summary>Percentage of position to exit when RSI crosses RsiExit3.</summary>
    public double RsiExit3Pct { get; set; } = 50.0;

    /// <summary>Minimum total confluence score required to open any trade.</summary>
    public double MinConfluenceScore { get; set; } = 8;

    /// <summary>Minimum confluence score required to add Scale 2 entry.</summary>
    public double Scale2MinScore { get; set; } = 11;

    /// <summary>Minimum confluence score required to add Scale 3 entry.</summary>
    public double Scale3MinScore { get; set; } = 14;

    /// <summary>Maximum candles after Scale 1 within which Scale 2 entry may be added.</summary>
    public int Scale2MaxCandles { get; set; } = 4;

    /// <summary>Maximum candles after Scale 1 within which Scale 3 entry may be added.</summary>
    public int Scale3MaxCandles { get; set; } = 4;

    /// <summary>UTC hour at which the dead zone session begins — no new entries allowed.</summary>
    public int SessionDeadZoneStart { get; set; } = 21;

    /// <summary>UTC hour at which the dead zone session ends.</summary>
    public int SessionDeadZoneEnd { get; set; } = 0;

    /// <summary>UTC hour at which the Asian session begins.</summary>
    public int SessionAsianStart { get; set; } = 0;

    /// <summary>UTC hour at which the Asian session ends.</summary>
    public int SessionAsianEnd { get; set; } = 8;

    /// <summary>UTC hour at which the London session begins.</summary>
    public int SessionLondonStart { get; set; } = 8;

    /// <summary>UTC hour at which the London session ends.</summary>
    public int SessionLondonEnd { get; set; } = 16;

    /// <summary>UTC hour at which the New York session begins.</summary>
    public int SessionNewYorkStart { get; set; } = 13;

    /// <summary>UTC hour at which the New York session ends.</summary>
    public int SessionNewYorkEnd { get; set; } = 21;

    /// <summary>When true, no new entries are placed after the weekend shutdown time.</summary>
    public bool WeekendShutdownEnabled { get; set; } = true;

    /// <summary>Day of week (0=Sunday, 5=Friday) for the weekend shutdown cutoff.</summary>
    public int WeekendShutdownDayUtc { get; set; } = 5;

    /// <summary>UTC hour on WeekendShutdownDayUtc at which entries are blocked until Monday.</summary>
    public int WeekendShutdownHourUtc { get; set; } = 20;

    /// <summary>Binance trading fee percentage per side (0.1 = 0.1%).</summary>
    public double BinanceFeePercent { get; set; } = 0.1;

    /// <summary>Slippage buffer added to all profit/loss calculations (0.05 = 0.05%).</summary>
    public double SlippageBufferPct { get; set; } = 0.05;

    /// <summary>Maximum number of retry attempts for failed API calls.</summary>
    public int ApiRetryMaxAttempts { get; set; } = 3;

    /// <summary>Delay in milliseconds between API retry attempts.</summary>
    public int ApiRetryDelayMs { get; set; } = 1000;

    /// <summary>When true, profits are fully reinvested monthly with no withdrawals.</summary>
    public bool CompoundingEnabled { get; set; } = true;

    /// <summary>Number of days between position size recalculations based on current balance.</summary>
    public int PositionSizeRecalcDays { get; set; } = 30;
}

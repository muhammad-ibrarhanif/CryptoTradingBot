# CryptoTradingBot — Master Prompt v2
> Read this file fully before making ANY changes.
> Never contradict it. Never add TODOs or placeholders.
> All terminology must use full names — see Section 26.

---

## CURRENT BUILD PHASE
> Phase 1 — Foundation only.
> Build in exact order listed in Section 27.
> Do NOT skip ahead.
> Do NOT build Worker, Api, or Dashboard yet.
> Prove Phase 1 profitable in backtest before moving to Phase 2.

---

## SECTION 1 — STRATEGY OBJECTIVE

Build a fully automated crypto scalping bot that:
- Trades SOL/USDT on Binance Spot
- Entry Timeframe:     30min candles (configurable)
- Structure Timeframe: 1H candles (configurable)
- Regime Timeframe:    4H candles (configurable)
- Bias Timeframe:      1D candles (configurable)
- Executes high-probability entries using confluence
  of market structure, key levels, and indicators
- Manages risk precisely with defined position sizing
- Compounds profits over 36 months

Core philosophy:
- Only trade WITH market structure — never against it
- Only enter at high-value zones — never in the middle
- Only enter when multiple factors confirm
- Protect capital first — profits follow naturally

What this bot does NOT do:
- Trade against the trend
- Enter without zone confirmation
- Enter on a single indicator signal
- Average down blindly
- Trade during news events
- Trade weekends
- Use market orders — limit orders only

---

## SECTION 2 — TRADING PARAMETERS

```
Exchange:             Binance Spot
Default Pair:         SOL/USDT (configurable)
Entry Timeframe:      30min (configurable)
Structure Timeframe:  1H    (configurable)
Regime Timeframe:     4H    (configurable)
Bias Timeframe:       1D    (configurable)

Account:
  Starting Balance:       $10,000
  Risk per scale entry:   1% of account
  Max scales:             3
  Max total risk:         3% per setup
  Daily loss limit:       3% → stop immediately

Order type:        Limit orders only
Execution:         On candle close only
Fees:              0.1% entry + 0.1% exit
Slippage buffer:   0.05%

Weekend blackout:
  No new entries after Friday 20:00 UTC
  Close all positions by Friday 20:00 UTC
  Resume Monday 00:00 UTC

News blackout:
  No entries 15 min before/after major events
```

---

## SECTION 3 — SWING POINT DETECTION
> This is the foundation. Everything else builds on this.

```
SWING HIGH:
  A candle with lower highs on BOTH sides
  Minimum 2 candles on each side
  Confirmed only after right side forms

SWING LOW:
  A candle with higher lows on BOTH sides
  Minimum 2 candles on each side
  Confirmed only after right side forms

DETECTION RULES:
  Scan Structure Timeframe (1H)
  Lookback: last 100 candles
  Confirmation: 2 candles after swing candle
  Store: price, time, type (high/low), strength

SWING STRENGTH:
  Small move  (<1%):  strength 1
  Medium move (1-3%): strength 2
  Large move  (>3%):  strength 3
```

---

## SECTION 4 — MARKET STRUCTURE

```
Built on top of: Swing Point Detection

UPTREND:
  Each swing high HIGHER than previous swing high
  Each swing low  HIGHER than previous swing low
  Higher High + Higher Low = Uptrend
  → Bot takes Buy signals only

DOWNTREND:
  Each swing high LOWER than previous swing high
  Each swing low  LOWER than previous swing low
  Lower High + Lower Low = Downtrend
  → Bot skips (Sell side in future version)

RANGING:
  No clear Higher High + Higher Low
  No clear Lower High + Lower Low
  → Bot takes Buy signals only at support
  → Never Sell in ranging market

STRUCTURE RULES:
  Compare last 3 swing highs + last 3 swing lows
  Checked on every Structure Timeframe candle close
  Uptrend   → Buy only
  Downtrend → Skip
  Ranging   → Buy at support only
```

---

## SECTION 5 — BREAK OF STRUCTURE AND
##             CHANGE OF CHARACTER

```
Built on top of: Market Structure

BREAK OF STRUCTURE:
  Confirms trend continues.

  Bullish Break of Structure:
    1H candle closes ABOVE previous swing high
    → Uptrend confirmed → continue Buy signals

  Bearish Break of Structure:
    1H candle closes BELOW previous swing low
    → Downtrend confirmed → continue Sell signals

CHANGE OF CHARACTER:
  Warns trend may be reversing.
  Detected when price breaks AGAINST current trend.

  On Change of Character:
    Bot immediately STOPS all new entries
    Status → PAUSED
    Waits for Break of Structure in new direction
    Resumes only after Break of Structure confirmed
    If not confirmed within 10 structure candles
    → resume previous trend bias

BOT STATES:
  ACTIVE   → taking signals normally
  PAUSED   → Change of Character detected
  REVERSED → Break of Structure confirmed
```

---

## SECTION 6 — SUPPORT AND RESISTANCE

```
Built on top of: Swing Point Detection

SOURCE:
  Every confirmed swing low  = Support level
  Every confirmed swing high = Resistance level
  Detected on Structure Timeframe (1H)

ZONE CONSTRUCTION:
  Support Zone:
    Bottom: swing low candle LOW
    Top:    swing low candle OPEN or CLOSE
            (whichever is higher)

  Resistance Zone:
    Bottom: swing high candle OPEN or CLOSE
            (whichever is lower)
    Top:    swing high candle HIGH

LEVEL SCORING:
  Touch count:
    2 touches  = score +1
    3 touches  = score +2
    4+ touches = score +3

  Timeframe:
    30min = score +1
    1H    = score +2
    4H    = score +3
    1D    = score +4

  Price reaction:
    Small bounce  = score +1
    Strong bounce = score +2
    Explosive     = score +3

  Recency:
    > 100 candles ago  = score +0
    50-100 candles ago = score +1
    20-50 candles ago  = score +2
    < 20 candles ago   = score +3

  Round number (within 0.5%):
    score +2

MINIMUM SCORE TO CONSIDER: 5 (configurable)
MINIMUM SCORE TO TRADE:    8 (configurable)

LEVEL VALIDITY:
  Fresh:      never entered
  Tested:     entered once held
  Mitigated:  closed through → remove

DYNAMIC MANAGEMENT:
  Activate:   score >= 5
  Deactivate: closed through OR
              score < 3 OR
              older than 200 candles no touch

PROXIMITY FILTER:
  Only evaluate levels price is approaching
  Approaching: within 2% AND moving toward level
```

---

## SECTION 7 — RESISTANCE BECOMES SUPPORT
##             SUPPORT BECOMES RESISTANCE

```
Built on top of: Support and Resistance

FLIP ZONE — 4 STEP DETECTION:

Step 1 — Original level active:
  Score >= 5, Status: Active

Step 2 — Break detected:
  Support broken:
    Candle closes BELOW support
    At least 0.2% below level
    Status → Broken

  Resistance broken:
    Candle closes ABOVE resistance
    At least 0.2% above level
    Status → Broken

Step 3 — Return detected:
  Price returns to broken level
  From OPPOSITE side of break
  Within 50 candles
  Within 0.3% of original level

Step 4 — Flip confirmed:
  Was support → now resistance
  Was resistance → now support
  Status → Flipped
  Score +2 flip bonus added

FLIP STRENGTH BONUSES:
  Original touches > 4:     score +1
  Move after break > 5%:    score +1
  Move after break > 10%:   score +2
  Move after break > 20%:   score +3
  Return within 5 candles:  score +1
  Return after 50 candles:  score -1

EXPIRY:
  No return within 50 candles
  Status → Expired → remove
```

---

## SECTION 8 — ZONES

```
Built on top of: Support and Resistance +
                 Flip Zone Detection

ZONE TYPES:
  Demand Zone  = Support area  → Buy zone
  Supply Zone  = Resistance area → Sell zone
  Flip Zone    = Broken and returned → strongest

TIMEFRAME CONFLUENCE:
  Levels from different timeframes
  within 0.3% of each other
  → merge into single confluence zone
  → add scores together

ZONE VALIDITY:
  Fresh:     never entered      → score +3
  Tested:    entered once held  → score +2
  Mitigated: closed through     → remove

ZONE SCORING:
  Fresh zone:                 score +3
  Tested once:                score +2
  Flip zone bonus:            score +2
  4H zone:                    score +3
  1D zone:                    score +4
  Multiple timeframe overlap: scores add

BOT RULES:
  Maintain active list of all valid zones
  Remove when price passes through
  Never enter outside a zone
  Prefer fresh over tested zones
  Track top 10 nearest valid zones only
```

---

## SECTION 9 — ORDER BLOCKS

```
BULLISH ORDER BLOCK:
  3+ consecutive green candles
  Total move > 1.5%
  Last RED candle before impulse =
  Bullish Order Block
  → Price returns here → Buy opportunity

BEARISH ORDER BLOCK:
  3+ consecutive red candles
  Total move > 1.5%
  Last GREEN candle before impulse =
  Bearish Order Block
  → Price returns here → Sell opportunity

ORDER BLOCK SCORING:
  Strong impulse (>3%):         score +3
  Medium impulse (1.5-3%):      score +2
  Order Block on 4H:            score +3
  Order Block on 1D:            score +4
  Inside zone:                  score +2 bonus
  Inside flip zone:             score +3 bonus

INVALIDATION:
  Price closes fully through Order Block → remove
```

---

## SECTION 10 — BREAKER BLOCKS

```
BULLISH BREAKER BLOCK:
  Bearish Order Block forms
  Price breaks back UP through it
  → Acts as support on next visit

BEARISH BREAKER BLOCK:
  Bullish Order Block forms
  Price breaks back DOWN through it
  → Acts as resistance on next visit

BREAKER BLOCK SCORING:
  Breaker Block:           score +3
  Inside zone:             score +2 bonus
  Inside flip zone:        score +3 bonus
  On 4H:                   score +3
  On 1D:                   score +4

INVALIDATION:
  Price passes through second time → remove
```

---

## SECTION 11 — LIQUIDITY AND LIQUIDITY SWEEPS

```
WHERE LIQUIDITY SITS:
  Above swing highs, below swing lows
  Above and below round numbers
  Equal highs and equal lows

BULLISH LIQUIDITY SWEEP:
  Wick extends BELOW swing low
  Candle closes back ABOVE level
  At least 0.2% back inside
  → Buy signal confirmed

BEARISH LIQUIDITY SWEEP:
  Wick extends ABOVE swing high
  Candle closes back BELOW level
  At least 0.2% back inside
  → Sell signal confirmed

DETECTION:
  Within 20 candles of swing point
  Never enter DURING sweep
  Only enter AFTER close confirmed

LIQUIDITY SWEEP SCORING:
  Sweep of swing low:       score +3
  Sweep of swing high:      score +3
  Round number:             score +2
  Near Order Block:         score +2 bonus
  On Structure Timeframe:   score +3
  On Entry Timeframe:       score +2
```

---

## SECTION 12 — SESSION WINDOWS

```
SESSIONS (UTC):
  Asian:      00:00-08:00  reduced size (0.5% risk)
  London:     08:00-16:00  full size (1% risk)
  New York:   13:00-21:00  full size (1% risk)
  Overlap:    13:00-16:00  preferred entries
  Dead Zone:  21:00-00:00  NO entries — blocked

SESSION SCORING:
  London:           score +1
  New York:         score +1
  London/NY overlap: score +2
  Asian:            score -1
  Dead zone:        BLOCKED

ASIAN RANGE:
  Record high and low during Asian session
  Watch for London sweep of Asian range
```

---

## SECTION 13 — FAIR VALUE GAPS

```
BULLISH FAIR VALUE GAP:
  Candle 3 low > Candle 1 high
  Gap >= 0.2%, middle body >= 0.5%
  → Price returns to fill → Buy opportunity

BEARISH FAIR VALUE GAP:
  Candle 3 high < Candle 1 low
  Gap >= 0.2%, middle body >= 0.5%
  → Price returns to fill → Sell opportunity

VALIDITY:
  Fresh → Partially filled → Mitigated (remove)

FAIR VALUE GAP SCORING:
  On Entry Timeframe:          score +2
  On Structure Timeframe:      score +3
  Inside zone:                 score +2 bonus
  Inside Order Block:          score +3 bonus
```

---

## SECTION 14 — MITIGATION

```
Full:    closes through zone → remove
Partial: enters closes back → score -1
         max 2 partials then remove
Wick:    wick only → score unchanged

Never enter fully mitigated zone.
Log every mitigation with timestamp.
```

---

## SECTION 15 — ENTRY PRECISION WITHIN ZONES

```
ENTRY LEVEL — PRIORITY ORDER:
  1. Order Block body inside zone
  2. Fair Value Gap inside zone
  3. Previous structure level
  4. 50% zone midpoint

STOP LOSS:
  Below zone bottom + 0.1% buffer
  Never inside the zone

If price skips entry → cancel → wait
```

---

## SECTION 16 — CANDLESTICK PATTERNS

```
BULLISH (at support or demand zone):
  Hammer:            lower wick 2x body  → score +2
  Bullish Engulfing: green engulfs red   → score +3
  Morning Star:      red→doji→green      → score +3
  Bullish Pin Bar:   lower wick 3x body  → score +2
  Inside Bar up:                         → score +1

BEARISH (at resistance or supply zone):
  Shooting Star:     upper wick 2x body  → score +2
  Bearish Engulfing: red engulfs green   → score +3
  Evening Star:      green→doji→red      → score +3
  Bearish Pin Bar:   upper wick 3x body  → score +2
  Inside Bar down:                       → score +1

Pattern on Structure Timeframe:  score +1 bonus
Pattern on Entry Timeframe:      score +2

Pattern outside zone = ignored
Minimum 1 pattern required for entry
Doji = wait for next candle
```

---

## SECTION 17 — INDICATORS (RSI + EMA) — LAST

```
Indicators are LAST — never first.
Never enter based on indicator alone.

RSI (period 14, close, Entry Timeframe):
  RSI < 30 at buy zone:       score +2
  RSI 30-40 at buy zone:      score +1
  RSI > 70 at sell zone:      score +2
  RSI 60-70 at sell zone:     score +1
  Bullish divergence:         score +3
  Bearish divergence:         score +3

EMA (period 20, close, Entry Timeframe):
  Price above EMA buy entry:      score +1
  EMA slope up buy entry:         score +1
  Price at EMA dynamic support:   score +2
  Price below EMA sell entry:     score +1
  EMA slope down sell entry:      score +1
  Price at EMA dynamic resist:    score +2
```

---

## SECTION 18 — CONFLUENCE SCORING SYSTEM

```
SCORE THRESHOLDS:
  < 8:   No trade
  8-10:  Scale 1 only    (1% risk)
  11-13: Scale 1+2       (2% risk)
  14+:   All 3 scales    (3% risk)

ALL COMPONENTS: (see Sections 4-17 for details)
  Market structure, zones, Order Blocks,
  Breaker Blocks, Fair Value Gaps,
  Liquidity sweeps, sessions, patterns,
  RSI, EMA — all contribute scores
```

---

## SECTION 19 — COMPLETE ENTRY CONDITIONS

```
Step 1:  1D bias check
Step 2:  4H regime check
Step 3:  1H structure check
         Change of Character active → skip
Step 4:  Valid zone nearby score >= 5
Step 5:  Order Block check → add score
Step 6:  Fair Value Gap check → add score
Step 7:  Liquidity sweep check → add score
Step 8:  Session check → dead zone blocked
Step 9:  Candlestick pattern required
Step 10: RSI + EMA confirmation → add score
Step 11: Total score determines scales allowed
Step 12: Calculate entry level (Section 15)
Step 13: Calculate stop loss
Step 14: Calculate position size
Step 15: Place limit order
```

---

## SECTION 20 — SCALING RULES

```
Scale 1: Score >= 8,  1% risk, immediate
Scale 2: Score >= 11, 1% risk, within 4 candles
         Only on red or doji candles
Scale 3: Score >= 14, 1% risk, within 4 candles
         Only on red or doji candles
         Requires RSI divergence confirmed

Never exceed 3% total risk per setup
Never add outside the zone
Never add on green candles
Maximum 1 active setup at a time
```

---

## SECTION 21 — RISK MANAGEMENT

```
Position size: (Balance × Risk%) / Stop Loss distance
Risk per scale: 1% (configurable 0.5%-2%)
Max total:      3% per setup
Recalculate:    every 30 days

Stop Loss: zone bottom + 0.1% buffer
Trailing:  move to breakeven at +1% profit
Daily limit: 3% → stop all trading

Fees: 0.1% entry + 0.1% exit
All calculations NET of fees
```

---

## SECTION 22 — EXIT RULES

```
Priority order (first triggers wins):

1. Hard Stop Hit              → exit 100%
2. Zone Fully Mitigated       → exit 100%
3. Change of Character on 1H  → exit 100%
4. Time Stop (12 candles)     → exit 50%
5. RSI crosses above 50       → exit 25%
6. RSI crosses above 60       → exit 25%
7. RSI crosses above 70       → exit 50%
8. Resistance Zone Reached    → exit 50%
9. Trailing Stop Hit          → exit 100%
```

---

## SECTION 23 — NEWS BLACKOUT

```
No entries 15 min before/after:
  FOMC, CPI, Fed speeches, major crypto events

If position open during news:
  Do NOT close, do NOT move stop loss
  Wait and resume normal management after

News times configured in appsettings.json
```

---

## SECTION 24 — COMPOUNDING PLAN

```
Reinvest 100% monthly, no withdrawals 36 months
Recalculate sizes every 30 days
3 losing months → pause 2 weeks and review

Targets (not guarantees):
  Year 1: $17,000-$31,000
  Year 2: $29,000-$96,000
  Year 3: $49,000-$298,000
```

---

## SECTION 25 — TRADING LOG

```
Every trade: entry/exit time+price, scale,
zone, confluence score, session, pattern,
RSI+EMA values, stop loss, size,
exit reason, fees, net profit, balance

Every rejection: time, what passed,
what failed, exact reason

Daily: trades, wins, losses, fees,
net profit, start/end balance, max drawdown
```

---

## SECTION 26 — TERMINOLOGY RULES

```
ALWAYS FULL NAMES — NEVER ABBREVIATE:

Order Block              — never "OB"
Break of Structure       — never "BOS"
Change of Character      — never "CHoCH"
Fair Value Gap           — never "FVG"
Support and Resistance   — never "S/R"
Support Becomes Resistance — never "SBR"
Resistance Becomes Support — never "RBS"
Higher High              — never "HH"
Higher Low               — never "HL"
Lower High               — never "LH"
Lower Low                — never "LL"
Take Profit              — never "TP"
Stop Loss                — never "SL"
```

---

## SECTION 27 — DEVELOPMENT PHASES

```
PHASE 1 — FOUNDATION:
  Step 1:  Swing point detection (1H)
  Step 2:  Market structure detection
  Step 3:  Support and Resistance zones
           (sourced from swing points)
  Step 4:  Resistance Becomes Support /
           Support Becomes Resistance
  Step 5:  Zone validity tracking
  Step 6:  Break of Structure detection
  Step 7:  Change of Character detection
  Step 8:  Basic entry:
           price in zone + RSI < 40 + score >= 8
  Step 9:  Basic exit:
           hard stop 2% + RSI > 60
  Step 10: Backtest
           Target: Win rate > 45%
                   Profit factor > 1.2
                   Max drawdown < 15%

PHASE 2 — SMART MONEY:
  Order Blocks, Fair Value Gaps,
  Liquidity sweeps, Breaker Blocks
  Backtest → measure improvement

PHASE 3 — CONFLUENCE:
  Full scoring, patterns, complete entry,
  scaling rules, final backtest

PHASE 4 — LIVE SYSTEM:
  Paper → Testnet → Live

RULE:
  Never move to next phase until:
  Win rate > 45%, Profit factor > 1.2,
  Max drawdown < 15%
```

---

## SECTION 28 — CODING RULES

```
Never hardcode values — all from appsettings.json
All indicators calculated from scratch
No third-party indicator libraries
Dependency injection throughout
XML comments on all public methods
Limit orders only — never market orders
Never execute mid-candle
All profit/loss NET of fees
No placeholders, no TODOs

Binance specific:
  BinanceApiCredentials not ApiCredentials
  KlineInterval.FourHour not FourHours
  No BinancePlacedOrder.Fills → use config fee

After every file:
  dotnet build → fix ALL errors → then next file
```

---

## SECTION 29 — APPSETTINGS.JSON

```json
{
  "BotConfig": {
    "Phase": "PaperTrade",
    "TradingPair": "SOL/USDT",
    "EntryTimeframe": "30m",
    "StructureTimeframe": "1H",
    "RegimeTimeframe": "4H",
    "BiasTimeframe": "1D",
    "RiskPerTradePct": 1.0,
    "MaxScales": 3,
    "MaxTotalRiskPct": 3.0,
    "DailyLossLimitPct": 3.0,
    "EmaPeriod": 20,
    "RsiPeriod": 14,
    "AtrPeriod": 14,
    "SwingLookback": 100,
    "SwingConfirmCandles": 2,
    "ZoneClusterPct": 0.3,
    "ZoneMinScore": 5,
    "ZoneTradeMinScore": 8,
    "ZoneProximityPct": 2.0,
    "ZoneActivePct": 1.0,
    "FlipZoneExpiry": 50,
    "FlipBreakMinPct": 0.2,
    "LiquiditySweepMinPct": 0.2,
    "LiquiditySweepMaxCandles": 20,
    "OrderBlockImpulseCandles": 3,
    "OrderBlockImpulseMinPct": 1.5,
    "FairValueGapMinPct": 0.2,
    "FairValueGapBodyMinPct": 0.5,
    "HardStopPct": 2.0,
    "StopLossBufferPct": 0.1,
    "BreakevenTriggerPct": 1.0,
    "TimeStopCandles": 12,
    "RsiEntryMax": 40.0,
    "RsiExit1": 50.0,
    "RsiExit2": 60.0,
    "RsiExit3": 70.0,
    "RsiExit1Pct": 25.0,
    "RsiExit2Pct": 25.0,
    "RsiExit3Pct": 50.0,
    "MinConfluenceScore": 8,
    "Scale2MinScore": 11,
    "Scale3MinScore": 14,
    "Scale2MaxCandles": 4,
    "Scale3MaxCandles": 4,
    "SessionDeadZoneStart": 21,
    "SessionDeadZoneEnd": 0,
    "SessionAsianStart": 0,
    "SessionAsianEnd": 8,
    "SessionLondonStart": 8,
    "SessionLondonEnd": 16,
    "SessionNewYorkStart": 13,
    "SessionNewYorkEnd": 21,
    "WeekendShutdownEnabled": true,
    "WeekendShutdownDayUtc": 5,
    "WeekendShutdownHourUtc": 20,
    "BinanceFeePercent": 0.1,
    "SlippageBufferPct": 0.05,
    "ApiRetryMaxAttempts": 3,
    "ApiRetryDelayMs": 1000,
    "CompoundingEnabled": true,
    "PositionSizeRecalcDays": 30
  },
  "Binance": {
    "ApiKey": "",
    "ApiSecret": "",
    "UseTestnet": true
  },
  "Serilog": {
    "MinimumLevel": "Information",
    "WriteTo": ["Console", "File"]
  }
}
```

---

## HOW TO START EACH SESSION

```
1. Read this file completely
2. Check CURRENT BUILD PHASE at top
3. Ask: what is the next uncompleted step?
4. Build only that step
5. Run dotnet build — fix all errors
6. Move to next step only when current compiles
7. Never skip steps
8. Never build ahead of current phase
```
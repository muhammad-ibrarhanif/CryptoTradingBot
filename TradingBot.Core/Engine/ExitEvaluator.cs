using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TradingBot.Core.Configuration;
using TradingBot.Core.Indicators;
using TradingBot.Core.Models;

namespace TradingBot.Core.Engine
{
    /// <summary>
    /// Phase 1 exit evaluator (Section 27 Step 9).
    ///
    /// Evaluates open positions on every candle close and triggers exits
    /// in the priority order defined in Section 22.
    ///
    /// Phase 1 exits implemented:
    ///   Priority 1 — Hard Stop Hit:        candle low crosses Stop Loss price.
    ///   Priority 2 — Zone Mitigated:       entry zone is no longer active.
    ///   Priority 3 — Change of Character:  structure signal bot state is Paused.
    ///   Priority 4 — Time Stop:            position open >= TimeStopCandles → exit 50%.
    ///   Priority 5 — RSI crosses 50:       exit 25%.
    ///   Priority 6 — RSI crosses 60:       exit 25%.
    ///   Priority 7 — RSI crosses 70:       exit 50%.
    ///
    /// Breakeven rule (Section 21):
    ///   When price moves >= BreakevenTriggerPct above entry, Stop Loss moves to entry price.
    ///
    /// All profit/loss is calculated net of fees (Section 21 / Section 28).
    /// </summary>
    public sealed class ExitEvaluator
    {
        private readonly BotConfig _config;

        /// <summary>Initialises the evaluator with bot configuration.</summary>
        public ExitEvaluator(BotConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Evaluates exit conditions for <paramref name="position"/> on the close of the
        /// candle at <paramref name="candleIndex"/>.
        ///
        /// Returns an ExitEvaluationResult describing the action to take.
        /// When HasAction is false, no exit is triggered and the position continues.
        /// </summary>
        /// <param name="candles">Entry-timeframe candle series, oldest first.</param>
        /// <param name="candleIndex">Index of the candle that just closed.</param>
        /// <param name="position">The open position to evaluate.</param>
        /// <param name="structureSignal">Latest structure signal from BreakOfStructureDetector.</param>
        /// <param name="zones">All currently tracked zones (used to check zone mitigation).</param>
        public ExitEvaluationResult Evaluate(
            IReadOnlyList<Candle> candles,
            int candleIndex,
            OpenPosition position,
            StructureSignal structureSignal,
            IReadOnlyList<Zone> zones)
        {
            if (candles is null) throw new ArgumentNullException(nameof(candles));
            if (position is null) throw new ArgumentNullException(nameof(position));
            if (structureSignal is null) throw new ArgumentNullException(nameof(structureSignal));
            if (zones is null) throw new ArgumentNullException(nameof(zones));

            var candle = candles[candleIndex];

            // Apply breakeven adjustment before checking stop (Section 21).
            ApplyBreakevenIfTriggered(position, candle);

            decimal rsi = RsiCalculator.Calculate(candles, _config.RsiPeriod, candleIndex);

            // ── Priority 1: Hard Stop Hit ─────────────────────────────────────────
            // Candle low touches or crosses the Stop Loss price.
            if (candle.Low <= position.StopLossPrice)
            {
                return FullExit(ExitReason.HardStopHit, position.StopLossPrice, candle.OpenTime);
            }

            // ── Priority 2: Zone Mitigated ────────────────────────────────────────
            var entryZone = zones.FirstOrDefault(z => z.Id == position.EntryZone.Id);
            if (entryZone is null || entryZone.Status == ZoneStatus.Mitigated)
            {
                return FullExit(ExitReason.ZoneMitigated, candle.Close, candle.OpenTime);
            }

            // ── Priority 3: Change of Character ──────────────────────────────────
            if (!structureSignal.AllowNewEntries)
            {
                return FullExit(ExitReason.ChangeOfCharacter, candle.Close, candle.OpenTime);
            }

            // ── Priority 4: Time Stop (50% exit) ─────────────────────────────────
            int candlesHeld = candleIndex - position.EntryCandleIndex;
            if (!position.TimeStopTaken && candlesHeld >= _config.TimeStopCandles)
            {
                position.TimeStopTaken = true;
                return PartialExit(ExitReason.TimeStop, 0.5m, candle.Close, candle.OpenTime);
            }

            // ── Priority 5: RSI crosses above 50 → exit 25% ──────────────────────
            if (!position.RsiExit1Taken && rsi >= (decimal)_config.RsiExit1)
            {
                position.RsiExit1Taken = true;
                return PartialExit(ExitReason.RsiCrossedExit1, (decimal)_config.RsiExit1Pct / 100m, candle.Close, candle.OpenTime);
            }

            // ── Priority 6: RSI crosses above 60 → exit 25% ──────────────────────
            if (!position.RsiExit2Taken && rsi >= (decimal)_config.RsiExit2)
            {
                position.RsiExit2Taken = true;
                return PartialExit(ExitReason.RsiCrossedExit2, (decimal)_config.RsiExit2Pct / 100m, candle.Close, candle.OpenTime);
            }

            // ── Priority 7: RSI crosses above 70 → exit 50% ──────────────────────
            if (!position.RsiExit3Taken && rsi >= (decimal)_config.RsiExit3)
            {
                position.RsiExit3Taken = true;
                return PartialExit(ExitReason.RsiCrossedExit3, (decimal)_config.RsiExit3Pct / 100m, candle.Close, candle.OpenTime);
            }

            return NoAction();
        }

        /// <summary>
        /// Calculates the net profit or loss for a closed fraction of a position,
        /// deducting entry and exit fees and slippage (Section 21 / Section 28).
        ///
        /// Formula:
        ///   grossPnl = (exitPrice - entryPrice) × units
        ///   fees     = (entryPrice × units × feeRate) + (exitPrice × units × feeRate)
        ///   slippage = exitPrice × units × slippageRate
        ///   netPnl   = grossPnl − fees − slippage
        /// </summary>
        /// <param name="entryPrice">Price at which the position was entered.</param>
        /// <param name="exitPrice">Price at which the position is being closed.</param>
        /// <param name="positionSize">Full position size in base currency units.</param>
        /// <param name="fractionClosed">Fraction of the position being closed (0 to 1).</param>
        public decimal CalculateNetPnl(
            decimal entryPrice,
            decimal exitPrice,
            decimal positionSize,
            decimal fractionClosed)
        {
            decimal units = positionSize * fractionClosed;
            decimal feeRate = (decimal)_config.BinanceFeePercent / 100m;
            decimal slipRate = (decimal)_config.SlippageBufferPct / 100m;

            decimal grossPnl = (exitPrice - entryPrice) * units;
            decimal fees = (entryPrice * units * feeRate) + (exitPrice * units * feeRate);
            decimal slippage = exitPrice * units * slipRate;

            return grossPnl - fees - slippage;
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Moves Stop Loss to entry price (breakeven) when the candle close is
        /// >= BreakevenTriggerPct above entry and breakeven has not yet been activated
        /// (Section 21).
        /// </summary>
        private void ApplyBreakevenIfTriggered(OpenPosition position, Candle candle)
        {
            if (position.BreakevenActivated) return;

            decimal triggerPrice = position.EntryPrice
                * (1m + (decimal)_config.BreakevenTriggerPct / 100m);

            if (candle.Close >= triggerPrice)
            {
                position.StopLossPrice = position.EntryPrice;
                position.BreakevenActivated = true;
            }
        }

        private static ExitEvaluationResult FullExit(ExitReason reason, decimal exitPrice, DateTime evaluatedAt) =>
            new()
            {
                HasAction = true,
                FullExit = true,
                FractionToClose = 1.0m,
                Reason = reason,
                ExitPrice = exitPrice,
                EvaluatedAt = evaluatedAt
            };

        private static ExitEvaluationResult PartialExit(ExitReason reason, decimal fraction, decimal exitPrice, DateTime evaluatedAt) =>
            new()
            {
                HasAction = true,
                FullExit = false,
                FractionToClose = fraction,
                Reason = reason,
                ExitPrice = exitPrice,
                EvaluatedAt = evaluatedAt
            };

        private static ExitEvaluationResult NoAction() =>
            new() { HasAction = false };
    }

}

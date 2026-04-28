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
    /// Phase 1 entry evaluator (Section 27 Step 8).
    ///
    /// Entry is allowed only when ALL of the following are satisfied on a candle close:
    ///   1. Bot structure state is not Paused (no active Change of Character).
    ///   2. Market structure allows Buy (Uptrend, or Ranging with support confirmation).
    ///   3. Current candle close is inside an active Demand or Flip zone
    ///      with score >= ZoneTradeMinScore (default 8).
    ///   4. RSI at the entry candle is <= RsiEntryMax (default 40).
    ///   5. Total confluence score >= MinConfluenceScore (default 8).
    ///
    /// Phase 1 confluence score:
    ///   Zone score (from ZoneTracker)    → primary component
    ///   RSI < 30 at buy zone             → +2
    ///   RSI 30–40 at buy zone            → +1
    ///   (Full scoring from Section 18 is added in Phase 3)
    ///
    /// Entry price:  candle close (limit order placed at close per Section 28).
    /// Stop Loss:    zone bottom minus StopLossBufferPct (Section 15 / Section 21).
    /// </summary>
    public sealed class EntryEvaluator
    {
        private readonly BotConfig _config;

        /// <summary>Initialises the evaluator with bot configuration.</summary>
        public EntryEvaluator(BotConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Evaluates whether a Buy entry should be opened on the close of the candle at
        /// <paramref name="candleIndex"/> in the entry-timeframe candle series.
        ///
        /// Call on every entry-timeframe candle close. Returns an EntryEvaluationResult
        /// with ShouldEnter=true when all Phase 1 conditions are met, or a rejection
        /// result with the first failing condition recorded.
        /// </summary>
        /// <param name="candles">Entry-timeframe candle series, oldest first.</param>
        /// <param name="candleIndex">Index of the candle that just closed.</param>
        /// <param name="structureSignal">Latest structure signal from BreakOfStructureDetector.</param>
        /// <param name="marketStructure">Latest market structure from MarketStructureDetector.</param>
        /// <param name="nearestZones">Active zones sorted by proximity (from ZoneTracker.GetTopNearestZones).</param>
        /// <param name="hasOpenPosition">True when a position is already open — prevents double entry.</param>
        public EntryEvaluationResult Evaluate(
            IReadOnlyList<Candle> candles,
            int candleIndex,
            StructureSignal structureSignal,
            MarketStructure marketStructure,
            IReadOnlyList<Zone> nearestZones,
            bool hasOpenPosition)
        {
            if (candles is null) throw new ArgumentNullException(nameof(candles));
            if (structureSignal is null) throw new ArgumentNullException(nameof(structureSignal));
            if (marketStructure is null) throw new ArgumentNullException(nameof(marketStructure));
            if (nearestZones is null) throw new ArgumentNullException(nameof(nearestZones));

            var candle = candles[candleIndex];

            // Only one active position at a time (Section 20).
            if (hasOpenPosition)
                return Reject(EntryRejectionReason.ChangeOfCharacterActive, candle.OpenTime, 0m, 0);

            // ── Condition 1: structure state must not be Paused ───────────────────
            if (!structureSignal.AllowNewEntries)
                return Reject(EntryRejectionReason.ChangeOfCharacterActive, candle.OpenTime, 0m, 0);

            // ── Condition 2: market structure must allow Buy ───────────────────────
            if (!marketStructure.AllowBuy)
                return Reject(EntryRejectionReason.StructureNotBullish, candle.OpenTime, 0m, 0);

            // ── Condition 3: price must be inside a qualifying Demand/Flip zone ────
            var zone = FindQualifyingZone(candle, nearestZones, marketStructure.RequireSupportConfirmation);

            if (zone is null)
                return Reject(EntryRejectionReason.NoQualifyingZone, candle.OpenTime, 0m, 0);

            // ── Condition 4: RSI must be <= RsiEntryMax ───────────────────────────
            decimal rsi = RsiCalculator.Calculate(candles, _config.RsiPeriod, candleIndex);

            if (rsi > (decimal)_config.RsiEntryMax)
                return Reject(EntryRejectionReason.RsiTooHigh, candle.OpenTime, rsi, zone.Score);

            // ── Condition 5: total confluence score >= MinConfluenceScore ──────────
            int score = CalculateConfluenceScore(zone, rsi);

            if (score < _config.MinConfluenceScore)
                return Reject(EntryRejectionReason.ScoreTooLow, candle.OpenTime, rsi, score);

            // ── All conditions met — build entry signal ────────────────────────────
            decimal entryPrice = candle.Close;
            decimal stopLossPrice = zone.Bottom * (1m - (decimal)_config.StopLossBufferPct / 100m);

            return new EntryEvaluationResult
            {
                ShouldEnter = true,
                TriggeringZone = zone,
                EntryPrice = entryPrice,
                StopLossPrice = stopLossPrice,
                RsiValue = rsi,
                ConfluenceScore = score,
                RejectionReason = null,
                EvaluatedAt = candle.OpenTime
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────────

        /// <summary>
        /// Finds the first active Demand or Flip zone whose boundaries contain the
        /// candle close, has score >= ZoneTradeMinScore, and satisfies the Ranging
        /// market support-confirmation requirement when applicable.
        /// </summary>
        private Zone? FindQualifyingZone(
            Candle candle,
            IReadOnlyList<Zone> nearestZones,
            bool requireSupportConfirmation)
        {
            foreach (var zone in nearestZones)
            {
                // Only Demand and Flip zones produce Buy signals (Section 8).
                if (zone.Category == ZoneCategory.Supply) continue;

                // In Ranging market only confirmed support zones qualify (Section 4).
                if (requireSupportConfirmation && zone.Category != ZoneCategory.Demand) continue;

                // Price must be inside the zone boundaries.
                if (candle.Close < zone.Bottom || candle.Close > zone.Top) continue;

                // Zone score must meet the trade threshold.
                if (zone.Score < _config.ZoneTradeMinScore) continue;

                return zone;
            }

            return null;
        }

        /// <summary>
        /// Phase 1 confluence score: zone score + RSI contribution (Section 17 / Section 18).
        /// Full scoring from Sections 9–16 is added in Phase 3.
        /// </summary>
        private static int CalculateConfluenceScore(Zone zone, decimal rsi)
        {
            int score = zone.Score;

            // RSI contribution at buy zone (Section 17).
            if (rsi < 30m) score += 2;
            else if (rsi <= 40m) score += 1;

            return score;
        }

        /// <summary>Builds a rejection result.</summary>
        private static EntryEvaluationResult Reject(
            EntryRejectionReason reason,
            DateTime evaluatedAt,
            decimal rsi,
            int score) =>
            new()
            {
                ShouldEnter = false,
                RejectionReason = reason,
                EvaluatedAt = evaluatedAt,
                RsiValue = rsi,
                ConfluenceScore = score
            };
    }
}

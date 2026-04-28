using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Core.Models
{
    /// <summary>
    /// Result returned by EntryEvaluator on every candle close.
    /// Either a valid entry signal or a rejection with the reason recorded.
    /// </summary>
    public sealed class EntryEvaluationResult
    {
        /// <summary>True when all Phase 1 entry conditions are satisfied.</summary>
        public bool ShouldEnter { get; init; }

        /// <summary>The zone that triggered the entry signal. Null when ShouldEnter is false.</summary>
        public Zone? TriggeringZone { get; init; }

        /// <summary>
        /// Calculated entry price: Stop Loss is placed at zone bottom minus StopLossBufferPct.
        /// Null when ShouldEnter is false.
        /// </summary>
        public decimal? EntryPrice { get; init; }

        /// <summary>
        /// Stop Loss price: zone bottom minus StopLossBufferPct buffer (Section 15 / Section 21).
        /// Null when ShouldEnter is false.
        /// </summary>
        public decimal? StopLossPrice { get; init; }

        /// <summary>RSI value at the time of evaluation.</summary>
        public decimal RsiValue { get; init; }

        /// <summary>Confluence score at the time of evaluation.</summary>
        public int ConfluenceScore { get; init; }

        /// <summary>Rejection reason when ShouldEnter is false. Null when ShouldEnter is true.</summary>
        public EntryRejectionReason? RejectionReason { get; init; }

        /// <summary>Candle open time at which this evaluation was performed.</summary>
        public DateTime EvaluatedAt { get; init; }
    }
}

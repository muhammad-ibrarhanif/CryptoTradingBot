using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Core.Models
{
    /// <summary>
    /// Result of a single exit evaluation on a candle close.
    /// May carry multiple partial exit actions in one evaluation.
    /// </summary>
    public sealed class ExitEvaluationResult
    {
        /// <summary>True when at least one exit action was triggered.</summary>
        public bool HasAction { get; init; }

        /// <summary>True when the position should be fully closed after this candle.</summary>
        public bool FullExit { get; init; }

        /// <summary>
        /// Fraction of the remaining position to close on this candle (0 to 1).
        /// 1.0 for full exits; 0.25 or 0.5 for partial RSI exits.
        /// </summary>
        public decimal FractionToClose { get; init; }

        /// <summary>The primary reason that triggered this exit evaluation.</summary>
        public ExitReason? Reason { get; init; }

        /// <summary>Exit price used for profit/loss calculation (candle close of the triggering candle).</summary>
        public decimal ExitPrice { get; init; }

        /// <summary>Candle open time at which the exit was evaluated.</summary>
        public DateTime EvaluatedAt { get; init; }
    }
}

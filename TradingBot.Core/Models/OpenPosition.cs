using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Core.Models
{
    /// <summary>
    /// Represents an open position tracked by the backtester and exit evaluator.
    /// </summary>
    public sealed class OpenPosition
    {
        /// <summary>Unique identifier for this position.</summary>
        public Guid Id { get; init; } = Guid.NewGuid();

        /// <summary>Price at which the position was entered.</summary>
        public decimal EntryPrice { get; init; }

        /// <summary>Hard Stop Loss price (zone bottom minus buffer).</summary>
        public decimal StopLossPrice { get; set; }

        /// <summary>The zone that triggered the entry.</summary>
        public Zone EntryZone { get; init; } = null!;

        /// <summary>Candle index at which the position was opened.</summary>
        public int EntryCandleIndex { get; init; }

        /// <summary>Candle open time at which the position was opened.</summary>
        public DateTime EntryTime { get; init; }

        /// <summary>Total position size in base currency units.</summary>
        public decimal PositionSize { get; init; }

        /// <summary>
        /// Remaining fraction of the original position still open (1.0 = 100%, 0.5 = 50%, etc.).
        /// Reduced by partial exits from RSI and time stop rules.
        /// </summary>
        public decimal RemainingFraction { get; set; } = 1.0m;

        /// <summary>True once Stop Loss has been moved to breakeven (triggered at BreakevenTriggerPct).</summary>
        public bool BreakevenActivated { get; set; }

        /// <summary>True once the RSI Exit 1 (50) partial exit has been taken.</summary>
        public bool RsiExit1Taken { get; set; }

        /// <summary>True once the RSI Exit 2 (60) partial exit has been taken.</summary>
        public bool RsiExit2Taken { get; set; }

        /// <summary>True once the RSI Exit 3 (70) partial exit has been taken.</summary>
        public bool RsiExit3Taken { get; set; }

        /// <summary>True once the time stop 50% exit has been taken.</summary>
        public bool TimeStopTaken { get; set; }
    }
}

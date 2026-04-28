using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Core.Models
{
    /// <summary>
    /// A completed trade record written to the trading log (Section 25).
    /// </summary>
    public sealed class CompletedTrade
    {
        /// <summary>Unique identifier matching the OpenPosition that was closed.</summary>
        public Guid PositionId { get; init; }

        /// <summary>Entry price.</summary>
        public decimal EntryPrice { get; init; }

        /// <summary>Exit price.</summary>
        public decimal ExitPrice { get; init; }

        /// <summary>Candle open time at entry.</summary>
        public DateTime EntryTime { get; init; }

        /// <summary>Candle open time at exit.</summary>
        public DateTime ExitTime { get; init; }

        /// <summary>Fraction of the original position that was closed in this exit.</summary>
        public decimal FractionClosed { get; init; }

        /// <summary>Position size in base currency units at time of exit.</summary>
        public decimal PositionSize { get; init; }

        /// <summary>Gross profit or loss before fees.</summary>
        public decimal GrossPnl { get; init; }

        /// <summary>Total fees paid (entry + exit, both at BinanceFeePercent).</summary>
        public decimal Fees { get; init; }

        /// <summary>Net profit or loss after fees (Section 21: all calculations net of fees).</summary>
        public decimal NetPnl { get; init; }

        /// <summary>The reason the position was closed.</summary>
        public ExitReason ExitReason { get; init; }

        /// <summary>Confluence score at time of entry.</summary>
        public int EntryConfluenceScore { get; init; }

        /// <summary>RSI value at time of entry.</summary>
        public decimal EntryRsi { get; init; }

        /// <summary>Zone category that triggered the entry.</summary>
        public ZoneCategory ZoneCategory { get; init; }
    }
}

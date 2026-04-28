using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Core.Models
{
    /// <summary>
    /// The reason an entry signal was rejected.
    /// Used in the rejection log (Section 25).
    /// </summary>
    public enum EntryRejectionReason
    {
        /// <summary>Bot state is Paused due to an active Change of Character (Section 5).</summary>
        ChangeOfCharacterActive,

        /// <summary>Market structure does not allow Buy entries (Downtrend).</summary>
        StructureNotBullish,

        /// <summary>No active zone with score >= ZoneTradeMinScore near current price.</summary>
        NoQualifyingZone,

        /// <summary>RSI is above RsiEntryMax at the candidate zone.</summary>
        RsiTooHigh,

        /// <summary>Total confluence score is below MinConfluenceScore.</summary>
        ScoreTooLow
    }
}

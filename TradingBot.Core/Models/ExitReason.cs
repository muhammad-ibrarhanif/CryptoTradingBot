using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TradingBot.Core.Models
{
    /// <summary>
    /// The reason the exit evaluator decided to close all or part of a position.
    /// Priority order matches Section 22.
    /// </summary>
    public enum ExitReason
    {
        /// <summary>Price hit the hard Stop Loss (zone bottom minus buffer) (Section 22 priority 1).</summary>
        HardStopHit,

        /// <summary>The entry zone was fully mitigated (Section 22 priority 2).</summary>
        ZoneMitigated,

        /// <summary>Change of Character detected on the Structure Timeframe (Section 22 priority 3).</summary>
        ChangeOfCharacter,

        /// <summary>Time stop: position open for TimeStopCandles without hitting a target (Section 22 priority 4).</summary>
        TimeStop,

        /// <summary>RSI crossed above RsiExit1 (50) — exit 25% (Section 22 priority 5).</summary>
        RsiCrossedExit1,

        /// <summary>RSI crossed above RsiExit2 (60) — exit 25% (Section 22 priority 6).</summary>
        RsiCrossedExit2,

        /// <summary>RSI crossed above RsiExit3 (70) — exit 50% (Section 22 priority 7).</summary>
        RsiCrossedExit3
    }
}

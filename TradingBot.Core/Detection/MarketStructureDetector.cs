using TradingBot.Core.Models;

namespace TradingBot.Core.Detection;

/// <summary>
/// Determines the current market structure (Uptrend, Downtrend, or Ranging)
/// by comparing the last 3 confirmed swing highs and last 3 confirmed swing lows
/// on the Structure Timeframe. Called on every Structure Timeframe candle close
/// (Section 4 of Master Prompt).
///
/// Rules:
///   Uptrend   — each swing high is higher than the previous swing high AND
///               each swing low is higher than the previous swing low.
///   Downtrend — each swing high is lower than the previous swing high AND
///               each swing low is lower than the previous swing low.
///   Ranging   — no clear pattern of Higher Highs + Higher Lows or
///               Lower Highs + Lower Lows.
/// </summary>
public sealed class MarketStructureDetector
{
    /// <summary>
    /// Analyses the provided confirmed swing points and returns the current
    /// market structure as of <paramref name="detectedAt"/>.
    /// Swing points must all be confirmed (IsConfirmed == true).
    /// </summary>
    /// <param name="confirmedSwingPoints">
    /// All confirmed swing points for the Structure Timeframe, oldest first.
    /// </param>
    /// <param name="detectedAt">
    /// The open time of the candle on which this analysis is being performed.
    /// </param>
    public MarketStructure Detect(
        IReadOnlyList<SwingPoint> confirmedSwingPoints,
        DateTime detectedAt)
    {
        if (confirmedSwingPoints is null)
            throw new ArgumentNullException(nameof(confirmedSwingPoints));

        var swingHighs = confirmedSwingPoints
            .Where(sp => sp.Type == SwingPointType.High && sp.IsConfirmed)
            .OrderBy(sp => sp.Time)
            .TakeLast(3)
            .ToList();

        var swingLows = confirmedSwingPoints
            .Where(sp => sp.Type == SwingPointType.Low && sp.IsConfirmed)
            .OrderBy(sp => sp.Time)
            .TakeLast(3)
            .ToList();

        MarketTrend trend = DetermineTrend(swingHighs, swingLows);

        return new MarketStructure
        {
            Trend         = trend,
            LastSwingHighs = swingHighs,
            LastSwingLows  = swingLows,
            DetectedAt     = detectedAt
        };
    }

    // ── private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Applies the Section 4 comparison rules to the last 3 swing highs and
    /// last 3 swing lows to produce a single MarketTrend value.
    /// Requires at least 2 swing highs AND 2 swing lows for a directional
    /// determination; fewer than that returns Ranging.
    /// </summary>
    private static MarketTrend DetermineTrend(
        IReadOnlyList<SwingPoint> swingHighs,
        IReadOnlyList<SwingPoint> swingLows)
    {
        if (swingHighs.Count < 2 || swingLows.Count < 2)
            return MarketTrend.Ranging;

        bool higherHighs = AreConsecutivelyHigher(swingHighs);
        bool higherLows  = AreConsecutivelyHigher(swingLows);
        bool lowerHighs  = AreConsecutivelyLower(swingHighs);
        bool lowerLows   = AreConsecutivelyLower(swingLows);

        if (higherHighs && higherLows) return MarketTrend.Uptrend;
        if (lowerHighs  && lowerLows)  return MarketTrend.Downtrend;

        return MarketTrend.Ranging;
    }

    /// <summary>
    /// Returns true when every consecutive pair in the list has a strictly
    /// higher price than the previous — confirming Higher Highs or Higher Lows.
    /// List must be ordered oldest first.
    /// </summary>
    private static bool AreConsecutivelyHigher(IReadOnlyList<SwingPoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].Price <= points[i - 1].Price)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Returns true when every consecutive pair in the list has a strictly
    /// lower price than the previous — confirming Lower Highs or Lower Lows.
    /// List must be ordered oldest first.
    /// </summary>
    private static bool AreConsecutivelyLower(IReadOnlyList<SwingPoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].Price >= points[i - 1].Price)
                return false;
        }

        return true;
    }
}

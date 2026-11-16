using System;

namespace music.Core.Services
{
    public readonly record struct MarqueeScrollMetrics(
        bool ShouldScroll,
        double TravelDistance,
        TimeSpan InitialPause,
        TimeSpan ScrollDuration,
        TimeSpan TotalDuration);

    /// <summary>
    /// Pure calculation policy for single-line marquee text overflow detection and timing.
    /// </summary>
    public static class MarqueeLayoutPolicy
    {
        public const double DefaultGap = 36.0;
        public const double DefaultSpeedPxPerSecond = 32.0;
        public const double DefaultInitialPauseSeconds = 1.5;
        public const double OverflowEpsilon = 0.5;

        public static bool ShouldScroll(
            double textWidth,
            double viewportWidth,
            double epsilon = OverflowEpsilon)
        {
            if (double.IsNaN(textWidth) || double.IsInfinity(textWidth) ||
                double.IsNaN(viewportWidth) || double.IsInfinity(viewportWidth))
            {
                return false;
            }

            return viewportWidth > 0 && textWidth > viewportWidth + epsilon;
        }

        public static MarqueeScrollMetrics ComputeMetrics(
            double textWidth,
            double viewportWidth,
            double gap = DefaultGap,
            double speedPxPerSecond = DefaultSpeedPxPerSecond,
            double initialPauseSeconds = DefaultInitialPauseSeconds)
        {
            if (!ShouldScroll(textWidth, viewportWidth))
            {
                return new MarqueeScrollMetrics(
                    false,
                    0,
                    TimeSpan.Zero,
                    TimeSpan.Zero,
                    TimeSpan.Zero);
            }

            var safeGap = gap > 0 ? gap : DefaultGap;
            var safeSpeed = speedPxPerSecond > 0 ? speedPxPerSecond : DefaultSpeedPxPerSecond;
            var safePauseSeconds = initialPauseSeconds >= 0 ? initialPauseSeconds : DefaultInitialPauseSeconds;

            var travelDistance = textWidth + safeGap;
            var initialPause = TimeSpan.FromSeconds(safePauseSeconds);
            var scrollDuration = TimeSpan.FromSeconds(travelDistance / safeSpeed);
            return new MarqueeScrollMetrics(
                true,
                travelDistance,
                initialPause,
                scrollDuration,
                initialPause + scrollDuration);
        }
    }
}

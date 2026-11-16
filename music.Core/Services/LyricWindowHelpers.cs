using System;
using System.Collections.Generic;
using music.Core.Models;

namespace music.Core.Services
{
    /// <summary>
    /// Pure helper algorithms for <see cref="music.LyricsWindow"/> so lookup and spring math
    /// can be unit-tested independently of WinUI / Win2D.
    /// </summary>
    public static class LyricWindowHelpers
    {
        /// <summary>
        /// Finds the active lyric line index for <paramref name="positionMs"/> using an O(1)
        /// adjacent-line fast path around <paramref name="hintIndex"/> and O(log N) binary search fallback.
        /// Matches reverse-scan semantics when duplicate timestamps exist (returns the last matching line).
        /// </summary>
        public static int FindActiveLyricIndex(
            IReadOnlyList<LyricLine> lyrics,
            double positionMs,
            int hintIndex = -1)
        {
            int count = lyrics.Count;
            if (count == 0) return -1;

            if (positionMs < lyrics[0].Time.TotalMilliseconds)
                return -1;

            // Fast path: playback usually stays on hintIndex or advances to hintIndex + 1.
            if (hintIndex >= 0 && hintIndex < count)
            {
                double curMs = lyrics[hintIndex].Time.TotalMilliseconds;
                if (positionMs >= curMs)
                {
                    int next = hintIndex + 1;
                    while (next < count && lyrics[next].Time.TotalMilliseconds <= positionMs)
                    {
                        if (next - hintIndex > 2)
                            return BinarySearchLastLeq(lyrics, positionMs, next, count - 1);
                        hintIndex = next;
                        next++;
                    }
                    return hintIndex;
                }
            }

            return BinarySearchLastLeq(lyrics, positionMs, 0, count - 1);
        }

        private static int BinarySearchLastLeq(IReadOnlyList<LyricLine> lyrics, double positionMs, int low, int high)
        {
            int result = -1;
            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (lyrics[mid].Time.TotalMilliseconds <= positionMs)
                {
                    result = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return result;
        }

        /// <summary>
        /// Binary-searches monotonically increasing line offsets for the line containing <paramref name="contentY"/>.
        /// Returns -1 if no line spans <paramref name="contentY"/>.
        /// </summary>
        public static int HitTestLyricLine(
            IReadOnlyList<float> offsets,
            IReadOnlyList<float> heights,
            float contentY)
        {
            int count = Math.Min(offsets.Count, heights.Count);
            if (count == 0 || contentY < offsets[0]) return -1;

            int low = 0;
            int high = count - 1;
            int candidate = -1;

            while (low <= high)
            {
                int mid = low + ((high - low) >> 1);
                if (offsets[mid] <= contentY)
                {
                    candidate = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            if (candidate >= 0 && contentY <= offsets[candidate] + heights[candidate])
                return candidate;

            return -1;
        }

        /// <summary>
        /// Advances a critically damped spring toward <paramref name="target"/> using its closed-form solution.
        /// </summary>
        public static (float Value, float Velocity, bool Settled) StepCriticallyDampedSpring(
            float current,
            float target,
            float velocity,
            float omega,
            float dt,
            float posEpsilon,
            float velEpsilon)
        {
            float x = current - target;
            float exp = MathF.Exp(-omega * dt);
            float nx = (x + (velocity + omega * x) * dt) * exp;
            float nv = (velocity - (velocity + omega * x) * omega * dt) * exp;

            if (MathF.Abs(nx) < posEpsilon && MathF.Abs(nv) < velEpsilon)
                return (target, 0f, true);

            return (target + nx, nv, false);
        }
    }
}

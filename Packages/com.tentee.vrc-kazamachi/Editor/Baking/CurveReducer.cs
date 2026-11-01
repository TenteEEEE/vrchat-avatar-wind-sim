using System;
using System.Collections.Generic;
using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor.Baking
{
    /// <summary>Curves for one baked target.</summary>
    public sealed class BakedTarget
    {
        public string Path;
        public AnimationCurve[] Curves;
        public int KeyCount { get { return Curves[0].length; } }
    }

    /// <summary>Fits periodic quaternion curves to sampled rotations with as few keys as the tolerance allows.</summary>
    public static class CurveReducer
    {
        // Maximum angular error at the analysis samples, in degrees. 0.25 keeps the error between
        // samples below the 0.2.0 curves (bake-check sweep) while storing roughly a quarter of the keys.
        public const float DefaultToleranceDegrees = 0.25f;

        public static BakedTarget Reduce(string path, Quaternion[] samples, float duration,
            float toleranceDegrees = DefaultToleranceDegrees)
        {
            var signal = new PeriodicSignal(samples, duration);
            var keys = SelectKeys(signal, toleranceDegrees, true);
            var curves = new AnimationCurve[4];
            for (var c = 0; c < 4; c++)
            {
                var frames = new Keyframe[keys.Count];
                for (var k = 0; k < keys.Count; k++)
                {
                    var index = keys[k] % signal.Count;
                    var time = keys[k] == signal.Count ? duration : duration * ((float)keys[k] / signal.Count);
                    var slope = signal.Slopes[c, index];
                    frames[k] = new Keyframe(time, signal.Values[c, index], slope, slope);
                    frames[k].weightedMode = WeightedMode.None;
                }
                curves[c] = new AnimationCurve(frames);
            }
            return new BakedTarget { Path = path, Curves = curves };
        }

        /// <summary>
        /// Greedy key selection: start with the loop ends, then insert the analysis sample with the worst
        /// error until every sample is within tolerance. Returned indices run 0..samples.Length, where the
        /// last index is the loop end (same value and slope as index 0). The uncached path rescans every
        /// segment each step and exists so the bake check can prove the cache does not change the result.
        /// </summary>
        public static List<int> SelectKeys(Quaternion[] samples, float duration, float toleranceDegrees,
            bool useSegmentCache)
        {
            return SelectKeys(new PeriodicSignal(samples, duration), toleranceDegrees, useSegmentCache);
        }

        private static List<int> SelectKeys(PeriodicSignal signal, double toleranceDegrees, bool useSegmentCache)
        {
            var keys = new List<int> { 0, signal.Count };
            // Keyed by segment start; only the two segments created by a split need recomputing.
            var cache = new Dictionary<int, SegmentWorst>();
            while (true)
            {
                var worstError = toleranceDegrees;
                var worstIndex = -1;
                for (var segment = 0; segment + 1 < keys.Count; segment++)
                {
                    var start = keys[segment];
                    var end = keys[segment + 1];
                    SegmentWorst worst;
                    if (!useSegmentCache || !cache.TryGetValue(start, out worst) || worst.End != end)
                    {
                        worst = signal.FindWorst(start, end);
                        cache[start] = worst;
                    }
                    if (worst.Error > worstError)
                    {
                        worstError = worst.Error;
                        worstIndex = worst.Index;
                    }
                }
                if (worstIndex < 0) return keys;
                keys.Insert(~keys.BinarySearch(worstIndex), worstIndex);
            }
        }

        private struct SegmentWorst
        {
            public int End;
            public int Index;
            public double Error;
        }

        private sealed class PeriodicSignal
        {
            public readonly int Count;
            public readonly float[,] Values;
            public readonly float[,] Slopes;
            private readonly float _dt;

            public PeriodicSignal(Quaternion[] samples, float duration)
            {
                Count = samples.Length;
                _dt = duration / Count;
                Values = new float[4, Count];
                Slopes = new float[4, Count];
                for (var i = 0; i < Count; i++)
                {
                    Values[0, i] = samples[i].x;
                    Values[1, i] = samples[i].y;
                    Values[2, i] = samples[i].z;
                    Values[3, i] = samples[i].w;
                }
                // Central difference that wraps around the loop seam, in value per second.
                for (var c = 0; c < 4; c++)
                {
                    for (var i = 0; i < Count; i++)
                    {
                        var next = Values[c, (i + 1) % Count];
                        var previous = Values[c, (i + Count - 1) % Count];
                        Slopes[c, i] = (next - previous) / (2f * _dt);
                    }
                }
            }

            public SegmentWorst FindWorst(int start, int end)
            {
                var worst = new SegmentWorst { End = end, Index = -1 };
                for (var i = start + 1; i < end; i++)
                {
                    var error = ErrorDegrees(start, end, i);
                    if (error > worst.Error)
                    {
                        worst.Error = error;
                        worst.Index = i;
                    }
                }
                return worst;
            }

            // Angle between the analysis sample and the normalized cubic Hermite reconstruction
            // (Unity's non-weighted curve evaluation) of the segment start..end.
            private double ErrorDegrees(int start, int end, int index)
            {
                var span = (end - start) * (double)_dt;
                var u = (double)(index - start) / (end - start);
                var u2 = u * u;
                var u3 = u2 * u;
                var h00 = 2 * u3 - 3 * u2 + 1;
                var h10 = u3 - 2 * u2 + u;
                var h01 = -2 * u3 + 3 * u2;
                var h11 = u3 - u2;
                var a = start % Count;
                var b = end % Count;
                double dot = 0;
                double norm = 0;
                double sampleNorm = 0;
                for (var c = 0; c < 4; c++)
                {
                    var value = h00 * Values[c, a] + h10 * span * Slopes[c, a] +
                                h01 * Values[c, b] + h11 * span * Slopes[c, b];
                    var sample = (double)Values[c, index];
                    dot += value * sample;
                    norm += value * value;
                    sampleNorm += sample * sample;
                }
                var cosine = norm > 0 && sampleNorm > 0
                    ? Math.Min(1.0, Math.Abs(dot) / Math.Sqrt(norm * sampleNorm))
                    : 0.0;
                return 2 * Math.Acos(cosine) * 180 / Math.PI;
            }
        }
    }
}

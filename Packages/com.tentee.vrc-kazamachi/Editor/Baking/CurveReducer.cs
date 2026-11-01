using System;
using System.Collections.Generic;
using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor.Baking
{
    public enum RotationCurveFormat { Quaternion, Euler }

    /// <summary>Curves for one baked target.</summary>
    public sealed class BakedTarget
    {
        public string Path;
        public AnimationCurve[] Curves;
        public string[] Properties;
        public int KeyCount { get { return Curves[0].length; } }
        public int StoredKeys { get { return KeyCount * Curves.Length; } }
    }

    /// <summary>Fits periodic rotation curves to sampled rotations with as few keys as the tolerance allows.</summary>
    public static class CurveReducer
    {
        // At 0.5 degrees a 30 cm strand tip moves about 2.6 mm; 1 degree saves only about 12% more.
        public const float DefaultToleranceDegrees = 0.5f;

        public static string[] PropertiesFor(RotationCurveFormat format)
        {
            return format == RotationCurveFormat.Euler
                ? new[] { "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z" }
                : new[] { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        }

        public static BakedTarget Reduce(string path, Quaternion[] samples, float duration,
            float toleranceDegrees = DefaultToleranceDegrees, RotationCurveFormat format = RotationCurveFormat.Quaternion)
        {
            var signal = new PeriodicSignal(samples, duration, format);
            var keys = SelectKeys(signal, toleranceDegrees, true);
            var curves = new AnimationCurve[signal.ChannelCount];
            for (var c = 0; c < signal.ChannelCount; c++)
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
            return new BakedTarget { Path = path, Curves = curves, Properties = PropertiesFor(format) };
        }

        /// <summary>
        /// Greedy key selection: start with the loop ends, then insert the analysis sample with the worst
        /// error until every sample is within tolerance. Returned indices run 0..samples.Length, where the
        /// last index is the loop end (same value and slope as index 0). The uncached path rescans every
        /// segment each step and exists so the bake check can prove the cache does not change the result.
        /// </summary>
        public static List<int> SelectKeys(Quaternion[] samples, float duration, float toleranceDegrees,
            bool useSegmentCache, RotationCurveFormat format = RotationCurveFormat.Quaternion)
        {
            return SelectKeys(new PeriodicSignal(samples, duration, format), toleranceDegrees, useSegmentCache);
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
            public readonly int ChannelCount;
            private readonly RotationCurveFormat _format;
            private readonly Quaternion[] _samples;
            public readonly float[,] Values;
            public readonly float[,] Slopes;
            private readonly float _dt;

            public PeriodicSignal(Quaternion[] samples, float duration, RotationCurveFormat format)
            {
                Count = samples.Length;
                _dt = duration / Count;
                _format = format;
                _samples = samples;
                ChannelCount = format == RotationCurveFormat.Euler ? 3 : 4;
                Values = new float[ChannelCount, Count];
                Slopes = new float[ChannelCount, Count];
                for (var i = 0; i < Count; i++)
                {
                    if (format == RotationCurveFormat.Quaternion)
                    {
                        Values[0, i] = samples[i].x;
                        Values[1, i] = samples[i].y;
                        Values[2, i] = samples[i].z;
                        Values[3, i] = samples[i].w;
                    }
                    else
                    {
                        var angles = samples[i].eulerAngles;
                        for (var c = 0; c < ChannelCount; c++)
                        {
                            var value = angles[c];
                            if (value > 180f) value -= 360f;
                            if (i > 0)
                            {
                                while (value - Values[c, i - 1] > 180f) value -= 360f;
                                while (value - Values[c, i - 1] < -180f) value += 360f;
                            }
                            Values[c, i] = value;
                        }
                        // Pivots stay within 80 degrees of identity, away from the ZXY singularity and seam drift.
                        if (Math.Abs(Values[0, i]) >= 89f)
                            throw new InvalidOperationException("Euler X angle is too close to the ZXY singularity.");
                    }
                }
                if (format == RotationCurveFormat.Euler)
                {
                    for (var c = 0; c < ChannelCount; c++)
                        if (Math.Abs(Values[c, Count - 1] - Values[c, 0]) > 180f)
                            throw new InvalidOperationException("Euler angles drift across the loop seam.");
                }
                // Central difference that wraps around the loop seam, in value per second.
                for (var c = 0; c < ChannelCount; c++)
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
                if (_format == RotationCurveFormat.Euler)
                {
                    // Quaternion.Euler applies Z, then X, then Y (Y * X * Z).
                    var x = (h00 * Values[0, a] + h10 * span * Slopes[0, a] +
                             h01 * Values[0, b] + h11 * span * Slopes[0, b]) * Math.PI / 360;
                    var y = (h00 * Values[1, a] + h10 * span * Slopes[1, a] +
                             h01 * Values[1, b] + h11 * span * Slopes[1, b]) * Math.PI / 360;
                    var z = (h00 * Values[2, a] + h10 * span * Slopes[2, a] +
                             h01 * Values[2, b] + h11 * span * Slopes[2, b]) * Math.PI / 360;
                    var sx = Math.Sin(x);
                    var cx = Math.Cos(x);
                    var sy = Math.Sin(y);
                    var cy = Math.Cos(y);
                    var sz = Math.Sin(z);
                    var cz = Math.Cos(z);
                    var qx = cy * sx * cz + sy * cx * sz;
                    var qy = sy * cx * cz - cy * sx * sz;
                    var qz = cy * cx * sz - sy * sx * cz;
                    var qw = cy * cx * cz + sy * sx * sz;
                    var sample = _samples[index];
                    var eulerDot = qx * sample.x + qy * sample.y + qz * sample.z + qw * sample.w;
                    var sampleLength = Math.Sqrt((double)sample.x * sample.x + (double)sample.y * sample.y +
                        (double)sample.z * sample.z + (double)sample.w * sample.w);
                    return 2 * Math.Acos(Math.Min(1, Math.Abs(eulerDot) / sampleLength)) * 180 / Math.PI;
                }
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

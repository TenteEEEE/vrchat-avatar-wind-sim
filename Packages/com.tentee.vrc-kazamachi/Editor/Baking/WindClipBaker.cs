using System.Collections.Generic;
using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor.Baking
{
    /// <summary>Samples and fits wind clips for a set of targets.</summary>
    public static class WindClipBaker
    {
        // Error checks and tangents use a finer grid than Sample Rate so gust peaks are not skipped.
        public const int Oversample = 4;
        public static List<BakedTarget> Bake(WindBakeSettings settings, IList<WindTarget> targets,
            Vector3 direction, float elevation, float turbulence,
            float toleranceDegrees = CurveReducer.DefaultToleranceDegrees)
        {
            int n = settings.SampleCount * Oversample;
            var dense = new Quaternion[targets.Count][];
            for (int t = 0; t < targets.Count; t++) dense[t] = new Quaternion[n];
            var sampler = new WindSampler(settings, direction, elevation, turbulence);
            for (int i = 0; i < n; i++)
            {
                var sample = sampler.At((float)i / n);
                for (int t = 0; t < targets.Count; t++)
                {
                    dense[t][i] = sampler.Evaluate(targets[t], sample);
                }
            }
            var result = new List<BakedTarget>();
            for (int t = 0; t < targets.Count; t++)
            {
                result.Add(CurveReducer.Reduce(targets[t].Path, dense[t], settings.Duration, toleranceDegrees));
            }
            return result;
        }
        // Two constant keys preserve the rest pose for blends without relying on default-value handling.
        public static List<BakedTarget> BakeNeutral(WindBakeSettings settings, IList<WindTarget> targets)
        {
            var result = new List<BakedTarget>();
            foreach (var target in targets)
            {
                var q = target.LocalRotation;
                var values = new[] { q.x, q.y, q.z, q.w };
                var curves = new AnimationCurve[4];
                for (int i = 0; i < 4; i++)
                    curves[i] = new AnimationCurve(new Keyframe(0f, values[i], 0f, 0f),
                        new Keyframe(settings.Duration, values[i], 0f, 0f));
                result.Add(new BakedTarget { Path = target.Path, Curves = curves });
            }
            return result;
        }
    }
}

using UnityEngine;
using UnityEditor;
using TenteEEEE.Kazamachi.Editor.Baking;

// Copied from 0.2.0; only Transform/settings inputs are replaced with plain values.
public static class LegacySampler
{
        public static Quaternion CalculateRotation(WindTarget target, WindBakeSettings wind, float time,
            Vector3 directionOverride, float elevationDegrees, float turbulence)
        {
            var restDirectionAvatar = target.RestDirection;
            var direction = directionOverride.normalized;
            var side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < 0.000001f) side = Vector3.Cross(Vector3.forward, direction);
            side.Normalize();
            var otherSide = Vector3.Cross(direction, side).normalized;

            var duration = Mathf.Clamp(wind.Duration, 2f, 30f);
            var sampleRate = Mathf.Clamp(wind.SampleRate, 10, 60);
            var baseCycles = ComputeBaseCycles(wind.MaxWindSpeed, sampleRate, duration, out _);
            var flutterNoise = PeriodicNoise(time, wind.Seed + 17, baseCycles);
            var directionNoise = PeriodicNoise(time, wind.Seed + 53, baseCycles);
            var verticalNoise = PeriodicNoise(time, wind.Seed + 97, baseCycles);
            var gustCount = Mathf.Max(1, Mathf.RoundToInt(wind.GustFrequency * duration));
            var gustPhase = Hash01(wind.Seed + 131);
            var gustWave = wind.GustFrequency <= 0f
                ? 0f
                : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (gustCount * time + gustPhase))), 6f);

            var variedDirection = (direction + side * (turbulence * 0.525f * directionNoise) +
                                   otherSide * (turbulence * 0.225f * verticalNoise)).normalized;
            // Steady lean is 55%; gusts add 45% at strength 1. Flutter and stronger gusts may overshoot.
            var leanUnits = 0.55f + 0.45f * wind.GustStrength * gustWave + turbulence * 0.375f * flutterNoise;
            var maxLeanAngle = Mathf.Clamp(wind.MaxLeanAngle, 0f, 60f);
            var leanAngle = Mathf.Clamp(maxLeanAngle * leanUnits, 0f, 80f) * Mathf.Deg2Rad;

            // Strand equilibrium: rest direction acts as unit "gravity", horizontal wind pushes
            // sideways with tan(lean) so a level wind leans exactly leanAngle. The vertical
            // component works against or with that gravity term: an updraft on hanging hair
            // shrinks the restoring term and lifts it well past leanAngle, a downdraft
            // flattens it, and on a horizontal strand (tail, ear) it simply pushes up or down.
            var elevation = Mathf.Clamp(elevationDegrees, -80f, 80f) * Mathf.Deg2Rad;
            var windForce = Mathf.Tan(leanAngle);
            var lateralWind = Vector3.ProjectOnPlane(variedDirection, restDirectionAvatar);
            var horizontalPush = Vector3.zero;
            if (lateralWind.sqrMagnitude > 0.000001f)
            {
                horizontalPush = lateralWind.normalized * windForce * Mathf.Cos(elevation) *
                                 Mathf.Min(1f, lateralWind.magnitude);
            }
            var verticalWind = Vector3.up * (windForce * Mathf.Sin(elevation));
            var alongRest = Vector3.Dot(verticalWind, restDirectionAvatar);
            var verticalPush = verticalWind - restDirectionAvatar * alongRest;
            // Floor keeps a gravity-cancelling updraft from buckling the strand sideways.
            var restoring = Mathf.Max(0.35f, 1f + alongRest);
            var desiredDirection = (restDirectionAvatar * restoring + horizontalPush + verticalPush).normalized;
            if (desiredDirection.sqrMagnitude < 0.5f) desiredDirection = restDirectionAvatar;
            var maxTotalAngle = Mathf.Min(80f, maxLeanAngle * 2.2f) * Mathf.Deg2Rad;
            desiredDirection = Vector3.RotateTowards(restDirectionAvatar, desiredDirection, maxTotalAngle, 0f);

            var windRotationAvatar = Quaternion.FromToRotation(restDirectionAvatar, desiredDirection);
            var parentInAvatar = target.ParentRotation;
            var windRotationParent = Quaternion.Inverse(parentInAvatar) * windRotationAvatar * parentInAvatar;
            return windRotationParent * target.LocalRotation;
        }

        private static float PeriodicNoise(float time, int seed, int baseCycles)
        {
            var value = 0f;
            var weight = 0f;
            for (var harmonic = 1; harmonic <= 4; harmonic++)
            {
                var amplitude = 1f / harmonic;
                value += Mathf.Sin(2f * Mathf.PI *
                    (harmonic * baseCycles * time + Hash01(seed + harmonic * 977))) * amplitude;
                weight += amplitude;
            }
            return value / weight;
        }

        private static float Hash01(int value)
        {
            unchecked
            {
                uint x = (uint)value;
                x ^= x >> 16;
                x *= 0x7feb352dU;
                x ^= x >> 15;
                x *= 0x846ca68bU;
                x ^= x >> 16;
                return (x & 0x00ffffffU) / 16777216f;
            }
        }

        public static int ComputeBaseCycles(
            float maxWindSpeed, int sampleRate, float duration, out int requestedCycles)
        {
            requestedCycles = Mathf.Max(1, Mathf.RoundToInt(duration *
                (0.12f + 0.045f * Mathf.Max(0f, maxWindSpeed))));
            var maxBaseCycles = Mathf.Max(1, Mathf.FloorToInt(sampleRate * duration / 16f));
            return Mathf.Min(requestedCycles, maxBaseCycles);
        }
    public static AnimationCurve[] BuildCurves(WindTarget target, WindBakeSettings wind,
        Vector3 direction, float elevation, float turbulence)
    {
        int samples = wind.SampleCount;
        var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
        var previous = Quaternion.identity;
        var first = Quaternion.identity;
        for (var i = 0; i <= samples; i++)
        {
            var normalizedTime = i == samples ? 0f : (float)i / samples;
            var quaternion = CalculateRotation(target, wind, normalizedTime, direction, elevation, turbulence);
            if (i == 0) first = quaternion;
            if (i == samples) quaternion = first;
            if (i > 0 && Quaternion.Dot(previous, quaternion) < 0f)
                quaternion = new Quaternion(-quaternion.x, -quaternion.y, -quaternion.z, -quaternion.w);
            var time = i == samples ? wind.Duration : wind.Duration * normalizedTime;
            curves[0].AddKey(time, quaternion.x);
            curves[1].AddKey(time, quaternion.y);
            curves[2].AddKey(time, quaternion.z);
            curves[3].AddKey(time, quaternion.w);
            previous = quaternion;
        }
        for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
            for (var keyIndex = 0; keyIndex < curves[curveIndex].length; keyIndex++)
            {
                AnimationUtility.SetKeyLeftTangentMode(curves[curveIndex], keyIndex, AnimationUtility.TangentMode.Auto);
                AnimationUtility.SetKeyRightTangentMode(curves[curveIndex], keyIndex, AnimationUtility.TangentMode.Auto);
            }
        return curves;
    }
}

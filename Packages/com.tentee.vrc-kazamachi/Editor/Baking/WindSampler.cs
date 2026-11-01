using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor.Baking
{
    /// <summary>Evaluates periodic wind rotations.</summary>
    public sealed class WindSampler
    {
        private readonly WindBakeSettings settings;
        private readonly Vector3 direction;
        private readonly Vector3 side;
        private readonly Vector3 otherSide;
        private readonly float turbulence;
        private readonly float elevationCos;
        private readonly float elevationSin;
        private readonly float maxTotalAngle;
        private readonly float gustPhase;
        private readonly int baseCycles;
        private readonly int gustCount;

        /// <summary>Time-dependent wind shared by all targets.</summary>
        public struct Sample
        {
            public Vector3 VariedDirection;
            public float WindForce;
        }
        public WindSampler(WindBakeSettings settings, Vector3 direction, float elevationDegrees, float turbulence)
        {
            this.settings = settings;
            this.direction = direction.normalized;
            side = Vector3.Cross(Vector3.up, this.direction);
            if (side.sqrMagnitude < 0.000001f) side = Vector3.Cross(Vector3.forward, this.direction);
            side.Normalize();
            otherSide = Vector3.Cross(this.direction, side).normalized;
            this.turbulence = turbulence;
            var elevation = Mathf.Clamp(elevationDegrees, -80f, 80f) * Mathf.Deg2Rad;
            elevationCos = Mathf.Cos(elevation);
            elevationSin = Mathf.Sin(elevation);
            maxTotalAngle = Mathf.Min(80f, settings.MaxLeanAngle * 2.2f) * Mathf.Deg2Rad;
            baseCycles = WindBakeSettings.ComputeBaseCycles(settings.MaxWindSpeed, settings.SampleRate, settings.Duration, out _);
            gustCount = Mathf.Max(1, Mathf.RoundToInt(settings.GustFrequency * settings.Duration));
            gustPhase = Hash01(settings.Seed + 131);
        }
        public Sample At(float time)
        {
            var flutterNoise = PeriodicNoise(time, settings.Seed + 17, baseCycles);
            var directionNoise = PeriodicNoise(time, settings.Seed + 53, baseCycles);
            var verticalNoise = PeriodicNoise(time, settings.Seed + 97, baseCycles);
            var gustWave = settings.GustFrequency <= 0f ? 0f :
                Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (gustCount * time + gustPhase))), 6f);
            var variedDirection = (direction + side * (turbulence * 0.525f * directionNoise) +
                                   otherSide * (turbulence * 0.225f * verticalNoise)).normalized;
            // Steady lean is 55%; gusts add 45% at strength 1. Flutter and stronger gusts may overshoot.
            var leanUnits = 0.55f + 0.45f * settings.GustStrength * gustWave + turbulence * 0.375f * flutterNoise;
            var leanAngle = Mathf.Clamp(settings.MaxLeanAngle * leanUnits, 0f, 80f) * Mathf.Deg2Rad;
            return new Sample { VariedDirection = variedDirection, WindForce = Mathf.Tan(leanAngle) };
        }
        public Quaternion Evaluate(WindTarget target, Sample sample)
        {
            return EvaluatePivot(target, sample) * target.LocalRotation;
        }
        public Quaternion EvaluatePivot(WindTarget target, Sample sample)
        {
            var restDirectionAvatar = target.RestDirection;
            var variedDirection = sample.VariedDirection;
            var windForce = sample.WindForce;
            // Strand equilibrium: rest direction acts as unit "gravity", horizontal wind pushes
            // sideways with tan(lean) so a level wind leans exactly leanAngle. The vertical
            // component works against or with that gravity term: an updraft on hanging hair
            // shrinks the restoring term and lifts it well past leanAngle, a downdraft
            // flattens it, and on a horizontal strand (tail, ear) it simply pushes up or down.
            var lateralWind = Vector3.ProjectOnPlane(variedDirection, restDirectionAvatar);
            var horizontalPush = Vector3.zero;
            if (lateralWind.sqrMagnitude > 0.000001f)
            {
                horizontalPush = lateralWind.normalized * windForce * elevationCos *
                                 Mathf.Min(1f, lateralWind.magnitude);
            }
            var verticalWind = Vector3.up * (windForce * elevationSin);
            var alongRest = Vector3.Dot(verticalWind, restDirectionAvatar);
            var verticalPush = verticalWind - restDirectionAvatar * alongRest;
            // Floor keeps a gravity-cancelling updraft from buckling the strand sideways.
            var restoring = Mathf.Max(0.35f, 1f + alongRest);
            var desiredDirection = (restDirectionAvatar * restoring + horizontalPush + verticalPush).normalized;
            if (desiredDirection.sqrMagnitude < 0.5f) desiredDirection = restDirectionAvatar;
            desiredDirection = Vector3.RotateTowards(restDirectionAvatar, desiredDirection, maxTotalAngle, 0f);
            var windRotationAvatar = Quaternion.FromToRotation(restDirectionAvatar, desiredDirection);
            var windRotationParent = Quaternion.Inverse(target.ParentRotation) * windRotationAvatar * target.ParentRotation;
            return windRotationParent;
        }
        public Quaternion Evaluate(WindTarget target, float time) { return Evaluate(target, At(time)); }
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
    }
}

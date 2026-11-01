using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor.Baking
{
    /// <summary>Clamped inputs and resolution for wind baking.</summary>
    public struct WindBakeSettings
    {
        public float Duration;
        public float MaxWindSpeed;
        public float MaxLeanAngle;
        public float GustStrength;
        public float GustFrequency;
        public float MaxElevation;
        public int SampleRate;
        public int Seed;
        public static WindBakeSettings Create(float duration, int sampleRate, float maxWindSpeed,
            float maxLeanAngle, float gustStrength, float gustFrequency, int seed, float maxElevation)
        {
            return new WindBakeSettings
            {
                Duration = Mathf.Clamp(duration, 2f, 30f),
                SampleRate = Mathf.Clamp(sampleRate, 10, 60),
                MaxWindSpeed = Mathf.Max(0f, maxWindSpeed),
                MaxLeanAngle = Mathf.Clamp(maxLeanAngle, 0f, 60f),
                GustStrength = gustStrength,
                GustFrequency = gustFrequency,
                Seed = seed,
                MaxElevation = Mathf.Clamp(maxElevation, 0f, 80f)
            };
        }
        public static int ComputeBaseCycles(float maxWindSpeed, int sampleRate, float duration, out int requestedCycles)
        {
            requestedCycles = Mathf.Max(1, Mathf.RoundToInt(duration *
                (0.12f + 0.045f * Mathf.Max(0f, maxWindSpeed))));
            var maxBaseCycles = Mathf.Max(1, Mathf.FloorToInt(sampleRate * duration / 16f));
            return Mathf.Min(requestedCycles, maxBaseCycles);
        }
        public int SampleCount { get { return Mathf.Max(2, Mathf.CeilToInt(Duration * SampleRate)); } }
    }
}

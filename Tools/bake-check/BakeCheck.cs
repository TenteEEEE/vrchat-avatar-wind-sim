using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using TenteEEEE.Kazamachi.Editor.Baking;

public static class BakeCheck
{
    [Serializable]
    private sealed class Result
    {
        public string config;
        public float tolerance;
        public double bakeMilliseconds;
        public int direction, elevation, turbulence, target;
        public double maxComponentDelta, newMaxAngle, legacyMaxAngle;
        public double newSeamValue, newSeamSlope, legacySeamValue, legacySeamSlope;
        public int newKeys, legacyKeys;
    }
    [Serializable]
    private sealed class Report
    {
        public double maxComponentDelta, newMaxAngle, legacyMaxAngle;
        public long newKeys, legacyKeys;
        public List<Result> cases = new List<Result>();
        public List<Summary> summaries = new List<Summary>();
    }
    [Serializable]
    private sealed class Summary
    {
        public string config;
        public float tolerance;
        public int turbulence;
        public long newKeys;
        public long legacyKeys;
        public double newMaxAngle;
        public double legacyMaxAngle;
        public double bakeMilliseconds;
    }
    private static WindTarget Target(Vector3 rest, Quaternion parent, Quaternion local, string path)
    {
        return new WindTarget { RestDirection = rest.normalized, ParentRotation = parent, LocalRotation = local, Path = path };
    }
    private static double Angle(Quaternion a, Quaternion b)
    {
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
        double na = (double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w;
        double nb = (double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w;
        return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(na * nb))) * 180 / Math.PI;
    }
    private static Quaternion Evaluate(AnimationCurve[] curves, float time)
    {
        return new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time),
            curves[2].Evaluate(time), curves[3].Evaluate(time));
    }
    private static void Seam(AnimationCurve[] curves, float duration, out double value, out double slope)
    {
        value = 0;
        slope = 0;
        foreach (var curve in curves)
        {
            value = Math.Max(value, Math.Abs(curve.Evaluate(0) - curve.Evaluate(duration)));
            var keys = curve.keys;
            slope = Math.Max(slope, Math.Abs(keys[0].outTangent - keys[keys.Length - 1].inTangent));
        }
    }
    public static void Run()
    {
        try
        {
            var targets = new List<WindTarget>
            {
                Target(Vector3.down, Quaternion.identity, Quaternion.identity, "down"),
                Target(new Vector3(0, -1, 1), Quaternion.Euler(10, 45, 20), Quaternion.Euler(15, 0, 10), "forward"),
                Target(Vector3.right, Quaternion.Euler(0, 80, 35), Quaternion.Euler(0, 20, 0), "tail"),
                Target(new Vector3(.2f, 1, .1f), Quaternion.Euler(20, -30, 15), Quaternion.Euler(5, 5, 5), "ear"),
                Target(Vector3.down, Quaternion.Euler(30, 40, 50), Quaternion.Euler(0, 0, 179.999f), "halfturn"),
                Target(new Vector3(-1, -.3f, .2f), Quaternion.Euler(-40, 22, 8), Quaternion.Euler(25, 60, -10), "side")
            };
            var names = new[] { "defaults", "no gust", "maximum lean and gust", "long high rate", "short low rate", "maximum speed" };
            var settings = new[]
            {
                WindBakeSettings.Create(8, 30, 22.5f, 45, 1, .3f, 1987, 60),
                WindBakeSettings.Create(8, 30, 22.5f, 45, 1, 0, 1987, 60),
                WindBakeSettings.Create(8, 30, 22.5f, 60, 1.5f, .3f, 1987, 60),
                WindBakeSettings.Create(30, 60, 22.5f, 45, 1, .3f, 1987, 60),
                WindBakeSettings.Create(2, 10, 22.5f, 45, 1, .3f, 1987, 60),
                WindBakeSettings.Create(8, 30, 45, 45, 1, .3f, 1987, 60)
            };
            var report = new Report();
            var tolerances = new[] { 0.1f, 0.25f, 0.5f };
            for (int c = 0; c < settings.Length; c++)
            foreach (float tolerance in tolerances)
            for (int turbulence = 0; turbulence <= 1; turbulence++)
            {
            var summary = new Summary { config = names[c], tolerance = tolerance, turbulence = turbulence };
            for (int d = 0; d < 8; d++)
            for (int e = -1; e <= 1; e++)
            {
                var setting = settings[c];
                float radians = d * (2f * Mathf.PI / 8);
                var direction = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
                float elevation = e * setting.MaxElevation;
                var sampler = new WindSampler(setting, direction, elevation, turbulence);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var baked = WindClipBaker.Bake(setting, targets, direction, elevation, turbulence, tolerance);
                watch.Stop();
                summary.bakeMilliseconds += watch.Elapsed.TotalMilliseconds;
                if (d == 0 && e == 0 && c < 3)
                {
                    int count = setting.SampleCount * WindClipBaker.Oversample;
                    var samples = new Quaternion[count];
                    for (int i = 0; i < count; i++) samples[i] = sampler.Evaluate(targets[0], (float)i / count);
                    var naive = CurveReducer.SelectKeys(samples, setting.Duration, tolerance, false);
                    var cached = CurveReducer.SelectKeys(samples, setting.Duration, tolerance, true);
                    if (naive.Count != cached.Count) throw new Exception("Reducer key count differs from naive greedy");
                    for (int i = 0; i < naive.Count; i++)
                        if (naive[i] != cached[i]) throw new Exception("Reducer keys differ from naive greedy");
                }
                for (int t = 0; t < targets.Count; t++)
                {
                    var legacy = LegacySampler.BuildCurves(targets[t], setting, direction, elevation, turbulence);
                    var curves = baked[t].Curves;
                    var result = new Result { config = names[c], direction = d, elevation = e, turbulence = turbulence, target = t,
                        tolerance = tolerance, bakeMilliseconds = watch.Elapsed.TotalMilliseconds,
                        newKeys = baked[t].KeyCount * 4, legacyKeys = legacy[0].length * 4 };
                    for (int i = 0; i < setting.SampleCount * 8; i++)
                    {
                        float normalizedTime = (float)i / (setting.SampleCount * 8);
                        var truth = sampler.Evaluate(targets[t], normalizedTime);
                        if (i % 8 == 0)
                        {
                            var old = LegacySampler.CalculateRotation(targets[t], setting, normalizedTime, direction, elevation, turbulence);
                            result.maxComponentDelta = Math.Max(result.maxComponentDelta, Math.Abs(truth.x - old.x));
                            result.maxComponentDelta = Math.Max(result.maxComponentDelta, Math.Abs(truth.y - old.y));
                            result.maxComponentDelta = Math.Max(result.maxComponentDelta, Math.Abs(truth.z - old.z));
                            result.maxComponentDelta = Math.Max(result.maxComponentDelta, Math.Abs(truth.w - old.w));
                        }
                        float time = setting.Duration * normalizedTime;
                        result.newMaxAngle = Math.Max(result.newMaxAngle, Angle(truth, Evaluate(curves, time)));
                        result.legacyMaxAngle = Math.Max(result.legacyMaxAngle, Angle(truth, Evaluate(legacy, time)));
                    }
                    Seam(curves, setting.Duration, out result.newSeamValue, out result.newSeamSlope);
                    Seam(legacy, setting.Duration, out result.legacySeamValue, out result.legacySeamSlope);
                    report.maxComponentDelta = Math.Max(report.maxComponentDelta, result.maxComponentDelta);
                    report.newMaxAngle = Math.Max(report.newMaxAngle, result.newMaxAngle);
                    report.legacyMaxAngle = Math.Max(report.legacyMaxAngle, result.legacyMaxAngle);
                    report.newKeys += result.newKeys;
                    report.legacyKeys += result.legacyKeys;
                    report.cases.Add(result);
                    summary.newKeys += result.newKeys;
                    summary.legacyKeys += result.legacyKeys;
                    summary.newMaxAngle = Math.Max(summary.newMaxAngle, result.newMaxAngle);
                    summary.legacyMaxAngle = Math.Max(summary.legacyMaxAngle, result.legacyMaxAngle);
                }
            }
            report.summaries.Add(summary);
            }
            var table = new System.Text.StringBuilder("BakeCheck: config | tolerance | turbulence | keys% | maxErr new | maxErr legacy | bake ms\n");
            foreach (var row in report.summaries)
                table.AppendFormat("{0} | {1:F2} | {2} | {3:F1}% | {4:F3} | {5:F3} | {6:F1}\n",
                    row.config, row.tolerance, row.turbulence, 100.0 * row.newKeys / row.legacyKeys,
                    row.newMaxAngle, row.legacyMaxAngle, row.bakeMilliseconds);
            Debug.Log(table.ToString());
            string output = "_work/bake-report.json";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-bakeCheckOut") output = args[i + 1];
            var directory = Path.GetDirectoryName(Path.GetFullPath(output));
            Directory.CreateDirectory(directory);
            File.WriteAllText(output, JsonUtility.ToJson(report, true), new System.Text.UTF8Encoding(false));
            Debug.Log("BakeCheck: max component delta " + report.maxComponentDelta + ", max angle new/legacy " +
                report.newMaxAngle + "/" + report.legacyMaxAngle + ", keys new/legacy " + report.newKeys + "/" + report.legacyKeys);
            EditorApplication.Exit(report.maxComponentDelta > 1e-6 ? 1 : 0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }
}

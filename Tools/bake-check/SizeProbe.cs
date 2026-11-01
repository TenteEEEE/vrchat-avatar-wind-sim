using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using TenteEEEE.Kazamachi.Editor.Baking;
using Debug = UnityEngine.Debug;

// Probe: tolerance sweep and quaternion (4 curves) vs localEulerAnglesRaw (3 curves).
// Reports stored keys, analytic error, low-pass (PhysBone-like) error, built AssetBundle size,
// and the Animator-evaluated difference between the euler and quaternion controllers.
public static class SizeProbe
{
    private const int RootCount = 136;
    private const int FilterTargets = 16;
    private const string Root = "Assets/SizeProbe";

    private sealed class Variant
    {
        public string Name;
        public bool Euler;
        public float Tol;
        public long Keys;
        public double MaxErr;
        public double FilteredErr;
        public double BakeMs;
        public AnimatorController Controller;
    }

    public static void Run()
    {
        try
        {
            var settings = WindBakeSettings.Create(8, 30, 22.5f, 45, 1, .3f, 1987, 60);
            var random = new System.Random(1234);
            var targets = new List<WindTarget>();
            for (var i = 0; i < RootCount; i++)
            {
                var rest = new Vector3((float)random.NextDouble() - .5f, -1f, (float)random.NextDouble() - .5f).normalized;
                var parent = Quaternion.Euler((float)random.NextDouble() * 60 - 30, (float)random.NextDouble() * 360, (float)random.NextDouble() * 60 - 30);
                // Preserve the random sequence used by the original probe targets.
                random.NextDouble();
                random.NextDouble();
                random.NextDouble();
                targets.Add(new WindTarget { Path = "root_" + i, RestDirection = rest, ParentRotation = parent });
            }
            var variants = new List<Variant>();
            foreach (var tol in new[] { 0.25f, 0.5f, 1.0f })
            {
                variants.Add(new Variant { Name = "q" + (int)(tol * 100), Euler = false, Tol = tol });
                variants.Add(new Variant { Name = "e" + (int)(tol * 100), Euler = true, Tol = tol });
            }

            if (AssetDatabase.IsValidFolder(Root)) AssetDatabase.DeleteAsset(Root);
            AssetDatabase.CreateFolder("Assets", "SizeProbe");
            int n = settings.SampleCount * WindClipBaker.Oversample;
            foreach (var v in variants)
            {
                AssetDatabase.CreateFolder(Root, v.Name);
                var folder = Root + "/" + v.Name;
                var format = v.Euler ? RotationCurveFormat.Euler : RotationCurveFormat.Quaternion;
                var neutralCurves = WindClipBaker.BakeNeutral(settings, targets, format: format);
                var neutral = SaveClip(folder + "/neutral.anim", neutralCurves, v);
                var calm = new AnimationClip[8];
                var turb = new AnimationClip[8];
                for (var d = 0; d < 8; d++)
                {
                    var radians = d * (2f * Mathf.PI / 8);
                    var direction = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
                    for (var tb = 0; tb <= 1; tb++)
                    {
                        var sampler = new WindSampler(settings, direction, 0f, tb);
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        var curves = WindClipBaker.Bake(settings, targets, direction, 0f, tb, v.Tol,
                            format: format);
                        watch.Stop();
                        v.BakeMs += watch.Elapsed.TotalMilliseconds;
                        // Error against the analytic truth on a grid 2x finer than the analysis grid.
                        int m = n * 2;
                        for (var t = 0; t < targets.Count; t++)
                        {
                            var truth = new Quaternion[m];
                            var approx = new Quaternion[m];
                            for (var i = 0; i < m; i++)
                            {
                                float u = (float)i / m;
                                truth[i] = sampler.Evaluate(targets[t], sampler.At(u));
                                approx[i] = Eval(curves[t].Curves, u * settings.Duration, v.Euler);
                                v.MaxErr = Math.Max(v.MaxErr, Angle(truth[i], approx[i]));
                            }
                            if (t < FilterTargets) v.FilteredErr = Math.Max(v.FilteredErr, FilteredError(truth, approx, settings.Duration));
                        }
                        var clip = SaveClip(folder + "/" + (tb == 0 ? "calm" : "turb") + d + ".anim", curves, v);
                        if (tb == 0) calm[d] = clip;
                        else turb[d] = clip;
                    }
                }
                v.Controller = BuildController(folder + "/wind.controller", neutral, calm, turb);
                AssetDatabase.SaveAssets();
                foreach (var path in Directory.GetFiles(folder))
                {
                    if (path.EndsWith(".meta")) continue;
                    AssetImporter.GetAtPath(path.Replace('\\', '/')).assetBundleName = "probe_" + v.Name;
                }
            }
            AssetDatabase.SaveAssets();

            var lines = new List<string>();
            var sizes = new Dictionary<string, long>();
            foreach (var mode in new[] { "uncompressed", "lzma" })
            {
                var output = "ProbeOut/" + mode;
                if (Directory.Exists(output)) Directory.Delete(output, true);
                Directory.CreateDirectory(output);
                var options = mode == "uncompressed" ? BuildAssetBundleOptions.UncompressedAssetBundle : BuildAssetBundleOptions.None;
                BuildPipeline.BuildAssetBundles(output, options, BuildTarget.StandaloneWindows64);
                foreach (var v in variants) sizes[mode + v.Name] = new FileInfo(Path.Combine(output, "probe_" + v.Name)).Length;
            }
            lines.Add("variant | keys | maxErr deg | filteredErr deg | uncompressed MB | lzma MB | bake ms | anim diff vs q same tol (deg)");
            foreach (var v in variants)
            {
                string diff = "-";
                if (v.Euler)
                {
                    var q = variants.Find(x => !x.Euler && x.Tol == v.Tol);
                    diff = AnimatorDiff(q.Controller, v.Controller).ToString("F3");
                }
                lines.Add(string.Format("{0} | {1} | {2:F3} | {3:F3} | {4:F2} | {5:F2} | {6:F0} | {7}", v.Name, v.Keys, v.MaxErr,
                    v.FilteredErr, sizes["uncompressed" + v.Name] / 1e6, sizes["lzma" + v.Name] / 1e6, v.BakeMs, diff));
            }
            foreach (var line in lines) Debug.Log("SizeProbe: " + line);
            File.WriteAllLines("size-probe-report.txt", lines);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    private static Quaternion Eval(AnimationCurve[] curves, float time, bool euler)
    {
        if (euler) return Quaternion.Euler(curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time));
        var q = new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time), curves[2].Evaluate(time), curves[3].Evaluate(time));
        var norm = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
        return new Quaternion(q.x / norm, q.y / norm, q.z / norm, q.w / norm);
    }

    // Second-order low-pass (2 Hz, damping 0.5) on the rotation vector, a rough stand-in for a
    // PhysBone chain following its root. Three loops warm up, the fourth is compared.
    private static double FilteredError(Quaternion[] truth, Quaternion[] approx, float duration)
    {
        var a = Filter(truth, duration);
        var b = Filter(approx, duration);
        double worst = 0;
        for (var i = 0; i < a.Length; i++) worst = Math.Max(worst, (a[i] - b[i]).magnitude);
        return worst;
    }

    private static Vector3[] Filter(Quaternion[] input, float duration)
    {
        int m = input.Length;
        float dt = duration / m;
        float w = 2f * Mathf.PI * 2f;
        float zeta = 0.5f;
        var x = Vector3.zero;
        var vel = Vector3.zero;
        var output = new Vector3[m];
        for (var loop = 0; loop < 4; loop++)
            for (var i = 0; i < m; i++)
            {
                var u = RotationVector(input[i]);
                var acc = w * w * (u - x) - 2f * zeta * w * vel;
                vel += acc * dt;
                x += vel * dt;
                if (loop == 3) output[i] = x;
            }
        return output;
    }

    private static Vector3 RotationVector(Quaternion q)
    {
        if (q.w < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
        float angle;
        Vector3 axis;
        q.ToAngleAxis(out angle, out axis);
        if (float.IsNaN(axis.x) || angle < 1e-6f) return Vector3.zero;
        if (angle > 180f) angle -= 360f;
        return axis * angle;
    }

    private static double Angle(Quaternion a, Quaternion b)
    {
        double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
        double na = (double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w;
        double nb = (double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w;
        return 2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(na * nb))) * 180 / Math.PI;
    }

    private static AnimationClip SaveClip(string path, List<BakedTarget> curves, Variant v)
    {
        var clip = new AnimationClip { frameRate = 30, wrapMode = WrapMode.Loop };
        for (var t = 0; t < curves.Count; t++)
            for (var c = 0; c < curves[t].Curves.Length; c++)
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(curves[t].Path, typeof(Transform), curves[t].Properties[c]), curves[t].Curves[c]);
                v.Keys += curves[t].Curves[c].length;
            }
        var cs = AnimationUtility.GetAnimationClipSettings(clip);
        cs.loopTime = true;
        AnimationUtility.SetAnimationClipSettings(clip, cs);
        AssetDatabase.CreateAsset(clip, path);
        return clip;
    }

    private static AnimatorController BuildController(string path, AnimationClip neutral, AnimationClip[] calm, AnimationClip[] turbulent)
    {
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Strength", AnimatorControllerParameterType.Float);
        controller.AddParameter("Direction", AnimatorControllerParameterType.Float);
        controller.AddParameter("Turbulence", AnimatorControllerParameterType.Float);
        BlendTree strength;
        var state = controller.CreateBlendTreeInController("Wind", out strength, 0);
        state.speedParameterActive = true;
        state.speedParameter = "Strength";
        strength.blendType = BlendTreeType.Simple1D;
        strength.blendParameter = "Strength";
        strength.useAutomaticThresholds = false;
        strength.AddChild(neutral, 0f);
        var direction = strength.CreateBlendTreeChild(1f);
        direction.blendType = BlendTreeType.Simple1D;
        direction.blendParameter = "Direction";
        direction.useAutomaticThresholds = false;
        BlendTree first = null;
        for (var d = 0; d < 8; d++)
        {
            var turbulence = direction.CreateBlendTreeChild(d / 8f);
            turbulence.blendType = BlendTreeType.Simple1D;
            turbulence.blendParameter = "Turbulence";
            turbulence.useAutomaticThresholds = false;
            turbulence.AddChild(calm[d], 0f);
            turbulence.AddChild(turbulent[d], 1f);
            if (d == 0) first = turbulence;
        }
        direction.AddChild(first, 1f);
        return controller;
    }

    // Max angle between root rotations driven by the two controllers, over several parameter sets.
    private static double AnimatorDiff(AnimatorController a, AnimatorController b)
    {
        double worst = 0;
        var parameterSets = new[] { new[] { 1f, 0f, 0f }, new[] { 1f, .3f, .5f }, new[] { .55f, .3f, .5f }, new[] { 1f, .9375f, 1f }, new[] { .2f, .6f, .8f } };
        foreach (var p in parameterSets)
        {
            var ra = Drive(a, p);
            var rb = Drive(b, p);
            for (var i = 0; i < ra.Count; i++) worst = Math.Max(worst, Angle(ra[i], rb[i]));
        }
        return worst;
    }

    private static List<Quaternion> Drive(AnimatorController controller, float[] p)
    {
        var avatar = new GameObject("avatar");
        var roots = new List<Transform>();
        for (var i = 0; i < RootCount; i++)
        {
            var t = new GameObject("root_" + i).transform;
            t.SetParent(avatar.transform, false);
            roots.Add(t);
        }
        var animator = avatar.AddComponent<Animator>();
        var graph = PlayableGraph.Create("probe");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        var playable = AnimatorControllerPlayable.Create(graph, controller);
        output.SetSourcePlayable(playable);
        playable.SetFloat("Strength", p[0]);
        playable.SetFloat("Direction", p[1]);
        playable.SetFloat("Turbulence", p[2]);
        graph.Play();
        var result = new List<Quaternion>();
        for (var f = 0; f < 240; f++)
        {
            graph.Evaluate(1f / 30f);
            if (f % 8 == 0) foreach (var r in roots) result.Add(r.localRotation);
        }
        graph.Destroy();
        UnityEngine.Object.DestroyImmediate(avatar);
        return result;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using TenteEEEE.Kazamachi.Editor.Baking;

// Writes every key (time, value, tangents as raw bits) of the plugin's bakes so a refactor can be
// proven byte-identical: run before and after, then compare dump files.
public static class DumpCheck
{
    public static void Run()
    {
        try
        {
            var output = "dump.txt";
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-dumpOut") output = args[i + 1];
            var random = new System.Random(99);
            var targets = new List<WindTarget>();
            for (var i = 0; i < 24; i++)
            {
                var rest = new Vector3((float)random.NextDouble() - .5f, (float)random.NextDouble() * 2 - 1.5f, (float)random.NextDouble() - .5f).normalized;
                var parent = Quaternion.Euler((float)random.NextDouble() * 60 - 30, (float)random.NextDouble() * 360, (float)random.NextDouble() * 60 - 30);
                targets.Add(new WindTarget { Path = "root_" + i, RestDirection = rest, ParentRotation = parent });
            }
            var settings = new[]
            {
                WindBakeSettings.Create(8, 30, 22.5f, 45, 1, .3f, 1987, 60),
                WindBakeSettings.Create(8, 30, 22.5f, 60, 1.5f, .3f, 7, 80),
                WindBakeSettings.Create(2, 10, 45, 45, 1, 0, 3, 60)
            };
            var sb = new StringBuilder();
            foreach (var s in settings)
            {
                Write(sb, "neutral", WindClipBaker.BakeNeutral(s, targets, format: RotationCurveFormat.Euler));
                for (var d = 0; d < 8; d++)
                {
                    var radians = d * (2f * Mathf.PI / 8);
                    var direction = new Vector3(Mathf.Sin(radians), 0, Mathf.Cos(radians));
                    for (var e = -1; e <= 1; e++)
                        for (var t = 0; t <= 1; t++)
                            Write(sb, "d" + d + "e" + e + "t" + t,
                                WindClipBaker.Bake(s, targets, direction, e * s.MaxElevation, t, CurveReducer.DefaultToleranceDegrees, format: RotationCurveFormat.Euler));
                }
            }
            File.WriteAllText(output, sb.ToString(), new UTF8Encoding(false));
            Debug.Log("DumpCheck: wrote " + output + " lines=" + sb.Length);
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    private static void Write(StringBuilder sb, string name, List<BakedTarget> baked)
    {
        foreach (var target in baked)
        {
            sb.Append(name).Append(' ').Append(target.Path);
            for (var c = 0; c < target.Curves.Length; c++)
            {
                sb.Append(' ').Append(target.Properties[c]);
                foreach (var k in target.Curves[c].keys)
                    sb.Append(' ').Append(Bits(k.time)).Append(',').Append(Bits(k.value)).Append(',')
                      .Append(Bits(k.inTangent)).Append(',').Append(Bits(k.outTangent));
            }
            sb.Append('\n');
        }
    }

    private static string Bits(float value)
    {
        return BitConverter.ToInt32(BitConverter.GetBytes(value), 0).ToString("x8");
    }
}

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using TenteEEEE.Kazamachi.Editor.Baking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(TenteEEEE.Kazamachi.Editor.KazamachiBuildPlugin))]

namespace TenteEEEE.Kazamachi.Editor
{
    public sealed class KazamachiBuildPlugin : Plugin<KazamachiBuildPlugin>
    {
        private static readonly string[] LocalRotationProperties =
        {
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w"
        };
        // Any of these on a root means another animation already rotates it.
        private static readonly string[] RotationProperties =
        {
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z",
            "localEulerAnglesBaked.x", "localEulerAnglesBaked.y", "localEulerAnglesBaked.z",
            "localEulerAngles.x", "localEulerAngles.y", "localEulerAngles.z"
        };
        private const string KazamachiIconGuid = "efaaa1d7f0314fd6af710a974d678a66";
        private const string WindIconGuid = "c2002acd2ac14bd28246f5030b4188c4";
        private const string DirectionIconGuid = "387adb1b1bb445edbdb8eb6eb907cdbd";
        private const string StrengthIconGuid = "5b6b5d46786e43b5a5f03a3d0a306cba";
        private const string TurbulenceIconGuid = "6d44e52dd3ca486398d10927c832cea6";
        private const string ElevationIconGuid = "6357f96426374d3bab0507aaed990cda";
        private sealed class BakeStats
        {
            public int Clips;
            public long StoredKeys;
            // Keys 0.2.0 stored for the same clips (one key per Sample Rate sample), for the log ratio.
            public long LegacyKeys;
        }

        private sealed class RuntimeControlState
        {
            public VRCAvatarDescriptor Descriptor;
            public VRCExpressionParameters Parameters;
            public List<VRCExpressionParameters.Parameter> ParameterList;
            public VRCExpressionsMenu RootMenu;
        }

        private sealed class GeneratingWind
        {
            public KazamachiWind Wind;
            public List<TargetInfo> Targets;
        }

        // Names and start values of one generated wind's parameters; Elevation is null without Vertical Control.
        private sealed class WindControls
        {
            public string Name;
            public string Enabled;
            public string Direction;
            public string Strength;
            public string Turbulence;
            public string Elevation;
            public float StartDirection;
            public float StartStrength;
            public float StartTurbulence;
            public float StartElevation;
        }

        private sealed class MenuIcons
        {
            public Texture2D Kazamachi = LoadIcon(KazamachiIconGuid);
            public Texture2D Wind = LoadIcon(WindIconGuid);
            public Texture2D Direction = LoadIcon(DirectionIconGuid);
            public Texture2D Strength = LoadIcon(StrengthIconGuid);
            public Texture2D Turbulence = LoadIcon(TurbulenceIconGuid);
            public Texture2D Elevation = LoadIcon(ElevationIconGuid);
        }

        private sealed class TargetInfo
        {
            public string Path;
            public Transform Root;
            public Vector3 RestDirection;
        }

        public override string QualifiedName { get { return "tenteeeee.kazamachi"; } }
        public override string DisplayName { get { return "Kazamachi"; } }

        protected override void Configure()
        {
            InPhase(BuildPhase.Transforming)
                .WithRequiredExtension(typeof(AnimatorServicesContext), sequence =>
                    sequence.AfterPlugin("nadena.dev.modular-avatar")
                        .Run("Generate PhysBone wind animation", Generate));
        }

        private static void Generate(BuildContext context)
        {
            var stats = new BakeStats();
            var allSettings = context.AvatarRootObject
                .GetComponentsInChildren<KazamachiWind>(true);
            var settings = allSettings.Where(item => item != null && item.isActiveAndEnabled).ToArray();
            if (settings.Length == 0)
            {
                Cleanup(allSettings, context.AvatarRootTransform);
                return;
            }

            var animatorServices = context.Extension<AnimatorServicesContext>();
            VirtualAnimatorController fx;
            if (!animatorServices.ControllerContext.Controllers.TryGetValue(
                    VRCAvatarDescriptor.AnimLayerType.FX, out fx) || fx == null)
            {
                Debug.LogWarning("[Kazamachi] FX Animatorが見つからないため、風を生成できませんでした。");
                Cleanup(allSettings, context.AvatarRootTransform);
                return;
            }

            var avatarRoot = context.AvatarRootTransform;
            var humanoidBones = CollectHumanoidBones(context.AvatarRootObject);
            // Decide before any wind layer is added so the vote only covers the avatar's own layers.
            var writeDefaults = ResolveWriteDefaults(fx);
            var processedRoots = new HashSet<Transform>();
            var generatedLayerCount = 0;
            var skippedCount = 0;
            var hiddenPhysBones = new HashSet<VRCPhysBoneBase>();
            var needsHiddenSkip = settings.Any(item => item.skipNeverActivePhysBones &&
                item.targetSelection != KazamachiWind.TargetSelection.IncludeOnly);
            var hasVrcFury = needsHiddenSkip && context.AvatarRootObject.GetComponentsInChildren<Component>(true)
                .Any(component => component != null &&
                    ((component.GetType().FullName ?? string.Empty).StartsWith("VF.", StringComparison.Ordinal) ||
                     component.GetType().Name.IndexOf("VRCFury", StringComparison.Ordinal) >= 0));
            if (hasVrcFury)
                Debug.Log("[Kazamachi] VRCFuryが見つかったため非表示PhysBoneの除外を無効にしました。");
            var controls = CreateRuntimeControlState(context);
            var icons = new MenuIcons();
            var generating = new List<GeneratingWind>();

            for (var windIndex = 0; windIndex < settings.Length; windIndex++)
            {
                var wind = settings[windIndex];
                if (wind.maxLeanAngle <= 0f)
                {
                    Debug.LogWarning("[Kazamachi] 最大傾斜角がゼロの設定をスキップしました。", wind);
                    continue;
                }

                var settingRootCount = 0;
                var targetInfos = new List<TargetInfo>();
                var groupedTargets = ResolveTargets(wind, avatarRoot, animatorServices, hasVrcFury,
                        hiddenPhysBones, ref skippedCount)
                    .GroupBy(ResolveRoot)
                    .Where(group => group.Key != null)
                    .OrderBy(group => Depth(group.Key))
                    .ToArray();

                foreach (var group in groupedTargets)
                {
                    var root = group.Key;
                    var physBones = group.ToArray();

                    if (!IsSafeRoot(root, avatarRoot, humanoidBones))
                    {
                        skippedCount++;
                        Debug.LogWarning(
                            "[Kazamachi] AvatarルートまたはHumanoid本体を動かすPhysBoneを安全のため除外しました: " +
                            RelativePath(avatarRoot, root), wind);
                        continue;
                    }

                    if (processedRoots.Contains(root))
                    {
                        foreach (var pb in physBones) pb.isAnimated = true;
                        continue;
                    }

                    if (processedRoots.Any(existing => root.IsChildOf(existing) || existing.IsChildOf(root)))
                    {
                        skippedCount++;
                        Debug.LogWarning(
                            "[Kazamachi] 二重に風が掛かる入れ子PhysBoneルートを除外しました: " +
                            RelativePath(avatarRoot, root), wind);
                        continue;
                    }

                    var virtualPath = animatorServices.ObjectPathRemapper.GetVirtualPathForObject(root);
                    if (wind.skipRootsWithAnimation && HasRotationAnimation(animatorServices, virtualPath))
                    {
                        skippedCount++;
                        Debug.LogWarning(
                            "[Kazamachi] 既存の回転アニメーションと競合するため除外しました: " +
                            RelativePath(avatarRoot, root), wind);
                        continue;
                    }

                    var representative = physBones[0];
                    var restDirection = GetRestDirectionInAvatarSpace(avatarRoot, root, representative);
                    targetInfos.Add(new TargetInfo
                    {
                        Path = virtualPath,
                        Root = root,
                        RestDirection = restDirection
                    });

                    foreach (var pb in physBones) pb.isAnimated = true;
                    processedRoots.Add(root);
                    settingRootCount++;
                }

                if (settingRootCount > 0) generating.Add(new GeneratingWind { Wind = wind, Targets = targetInfos });
            }

            foreach (var entry in generating)
            {
                var wind = entry.Wind;
                var targetInfos = entry.Targets;
                var number = generating.Count == 1 ? "" : (generatedLayerCount + 1).ToString();
                var suffix = number.Length == 0 ? "" : " " + number;
                var values = CreateWindControls(wind, "Kazamachi" + suffix, "TenteEEEE/Kazamachi" + number);
                AddAnimatorParameter(fx, values.Enabled, 0f, AnimatorControllerParameterType.Bool);
                AddAnimatorParameter(fx, values.Direction, values.StartDirection);
                AddAnimatorParameter(fx, values.Strength, values.StartStrength);
                AddAnimatorParameter(fx, values.Turbulence, values.StartTurbulence);
                if (values.Elevation != null) AddAnimatorParameter(fx, values.Elevation, values.StartElevation);

                var bakeSettings = CreateBakeSettings(wind);
                var bakeTargets = targetInfos.Select(target => new WindTarget
                {
                    Path = target.Path,
                    RestDirection = target.RestDirection,
                    ParentRotation = Quaternion.Inverse(avatarRoot.rotation) * target.Root.parent.rotation,
                    LocalRotation = target.Root.localRotation
                }).ToList();
                var neutral = CreateNeutralClip(bakeTargets, bakeSettings, stats, "Wind Neutral" + suffix);
                var motion = CreateRuntimeMotion(bakeTargets, bakeSettings, stats, neutral, values, suffix);
                var layer = fx.AddLayer(LayerPriority.Default, values.Name);
                layer.DefaultWeight = 1f;
                layer.BlendingMode = AnimatorLayerBlendingMode.Override;

                // Off/Wind are separate states switched by the Bool. The cross-fade
                // keeps ON/OFF from snapping.
                var offState = layer.StateMachine.AddState("Wind Off");
                offState.WriteDefaultValues = writeDefaults;
                offState.Motion = neutral;
                var windState = layer.StateMachine.AddState("Wind");
                windState.WriteDefaultValues = writeDefaults;
                windState.SpeedParameter = values.Strength;
                windState.Speed = 1f;
                windState.Motion = motion;
                offState.Transitions = ImmutableList.Create(
                    CreateToggleTransition(windState, values.Enabled, true));
                windState.Transitions = ImmutableList.Create(
                    CreateToggleTransition(offState, values.Enabled, false));
                layer.StateMachine.DefaultState = offState;
                InstallRuntimeControls(context, controls, values, icons);
                generatedLayerCount++;
            }

            if (generatedLayerCount > 0)
            {
                FinalizeRuntimeControls(context, controls);
                Debug.Log("[Kazamachi] " + processedRoots.Count +
                          " 個のPhysBoneルートへ風を生成しました。除外: " + skippedCount +
                          "、生成クリップ: " + stats.Clips +
                          "、保存キー " + stats.StoredKeys.ToString("N0") +
                          "（0.2.0相当 " + stats.LegacyKeys.ToString("N0") + "、" +
                          (100.0 * stats.StoredKeys / stats.LegacyKeys).ToString("F1") + "%）");
            }
            else
            {
                Debug.LogWarning("[Kazamachi] 安全に処理できるPhysBoneルートがありませんでした。");
            }

            Cleanup(allSettings, context.AvatarRootTransform);
        }

        private static WindControls CreateWindControls(KazamachiWind wind, string name, string parameterPrefix)
        {
            return new WindControls
            {
                Name = name,
                Enabled = parameterPrefix + "/Enabled",
                Direction = parameterPrefix + "/Direction",
                Strength = parameterPrefix + "/Strength",
                Turbulence = parameterPrefix + "/Turbulence",
                Elevation = wind.verticalControl ? parameterPrefix + "/Elevation" : null,
                StartDirection = Mathf.Clamp(wind.initialDirectionAngle, 0f, 360f) / 360f,
                StartStrength = Mathf.Clamp01(wind.initialStrength),
                StartTurbulence = Mathf.Clamp01(wind.initialTurbulence),
                StartElevation = Mathf.Clamp01(wind.initialElevation)
            };
        }

        private static IEnumerable<VRCPhysBoneBase> ResolveTargets(KazamachiWind wind, Transform avatarRoot,
            AnimatorServicesContext services, bool hasVrcFury, HashSet<VRCPhysBoneBase> hiddenPhysBones,
            ref int skippedCount)
        {
            IEnumerable<VRCPhysBoneBase> targets;
            if (wind.targetSelection == KazamachiWind.TargetSelection.IncludeOnly)
            {
                targets = wind.includedPhysBones ?? Enumerable.Empty<VRCPhysBoneBase>();
            }
            else
            {
                var excluded = new HashSet<VRCPhysBoneBase>(
                    (wind.excludedPhysBones ?? Enumerable.Empty<VRCPhysBoneBase>()).Where(pb => pb != null));
                targets = avatarRoot.GetComponentsInChildren<VRCPhysBoneBase>(true)
                    .Where(pb => pb != null && !excluded.Contains(pb));
                if (wind.targetSelection == KazamachiWind.TargetSelection.Auto)
                {
                    targets = targets.Where(pb => !IsAutoExcluded(pb, wind, avatarRoot));
                }
            }

            var result = targets.Where(pb => pb != null && pb.transform.IsChildOf(avatarRoot)).Distinct().ToList();
            if (wind.targetSelection != KazamachiWind.TargetSelection.IncludeOnly &&
                wind.skipNeverActivePhysBones && !hasVrcFury)
            {
                foreach (var pb in result.ToArray())
                {
                    if (!IsNeverActive(pb, avatarRoot, services)) continue;
                    if (hiddenPhysBones.Add(pb))
                    {
                        Debug.Log("[Kazamachi] 常に非表示のため除外: " + RelativePath(avatarRoot, pb.transform), pb);
                        skippedCount++;
                    }
                    result.Remove(pb);
                }
            }
            return result;
        }

        private static bool IsNeverActive(VRCPhysBoneBase pb, Transform avatarRoot, AnimatorServicesContext services)
        {
            var path = services.ObjectPathRemapper.GetVirtualPathForObject(pb.transform);
            if (!pb.enabled && !services.AnimationIndex.GetClipsForBinding(
                    EditorCurveBinding.FloatCurve(path, pb.GetType(), "m_Enabled")).Any())
                return true;
            for (var current = pb.transform; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf)
                {
                    path = services.ObjectPathRemapper.GetVirtualPathForObject(current);
                    if (!services.AnimationIndex.GetClipsForBinding(
                            EditorCurveBinding.FloatCurve(path, typeof(GameObject), "m_IsActive")).Any())
                        return true;
                }
                if (current == avatarRoot) break;
            }
            return false;
        }

        // Auto only looks at the component's own GameObject and its root. Walking up the
        // hierarchy would also catch accessories parented under a breast bone.
        private static bool IsAutoExcluded(VRCPhysBoneBase physBone, KazamachiWind wind, Transform avatarRoot)
        {
            if (wind.autoExcludeNameKeywords == null) return false;
            var names = new[] { physBone.gameObject.name, ResolveRoot(physBone)?.name ?? string.Empty };
            foreach (var keyword in wind.autoExcludeNameKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                foreach (var name in names)
                {
                    if (name.IndexOf(keyword.Trim(), StringComparison.OrdinalIgnoreCase) < 0) continue;
                    Debug.Log("[Kazamachi] Auto除外: " + RelativePath(avatarRoot, physBone.transform) +
                              " (キーワード '" + keyword + "')", physBone);
                    return true;
                }
            }
            return false;
        }

        private static Transform ResolveRoot(VRCPhysBoneBase physBone)
        {
            return physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
        }

        private static bool IsSafeRoot(Transform root, Transform avatarRoot, HashSet<Transform> humanoidBones)
        {
            return root != null && root != avatarRoot && root.parent != null &&
                   root.IsChildOf(avatarRoot) && !humanoidBones.Contains(root);
        }

        // Match the avatar's existing Write Defaults convention: WD On only when every counted
        // state is WD On. Mixing WD On/Off states in one controller makes Unity stop resetting
        // properties the WD On layers rely on, which shows up as stuck facial expressions.
        // Both wind states bake every rotation they touch, so either setting is safe here;
        // a mixed or empty controller falls back to WD Off.
        private static bool ResolveWriteDefaults(VirtualAnimatorController fx)
        {
            var states = fx.Layers
                .Where(layer => layer.StateMachine != null)
                .Where(layer => !IsWriteDefaultsRequiredLayer(layer))
                .SelectMany(layer => layer.StateMachine.AllStates())
                .Select(state => state.WriteDefaultValues)
                .Distinct()
                .ToArray();
            return states.Length == 1 && states[0];
        }

        // Additive layers and single-state direct blend tree layers must be WD On regardless of
        // the avatar's convention, so they don't count toward the vote.
        private static bool IsWriteDefaultsRequiredLayer(VirtualLayer layer)
        {
            if (layer.BlendingMode == AnimatorLayerBlendingMode.Additive) return true;
            var stateMachine = layer.StateMachine;
            if (stateMachine == null) return false;
            if (stateMachine.StateMachines.Count != 0) return false;
            if (stateMachine.States.Count != 1) return false;
            if (stateMachine.AnyStateTransitions.Count != 0) return false;
            if (stateMachine.DefaultState?.Transitions?.Count != 0) return false;
            if (!(stateMachine.DefaultState.Motion is VirtualBlendTree)) return false;

            return stateMachine.DefaultState.Motion.AllReachableNodes()
                .OfType<VirtualBlendTree>()
                .Any(tree => tree.BlendType == BlendTreeType.Direct);
        }

        private static HashSet<Transform> CollectHumanoidBones(GameObject avatar)
        {
            var result = new HashSet<Transform>();
            var animator = avatar.GetComponent<Animator>();
            if (animator == null || !animator.isHuman) return result;

            for (var i = 0; i < (int)HumanBodyBones.LastBone; i++)
            {
                var bone = animator.GetBoneTransform((HumanBodyBones)i);
                if (bone != null) result.Add(bone);
            }
            return result;
        }

        private static bool HasRotationAnimation(AnimatorServicesContext services, string path)
        {
            return RotationProperties.Any(property => services.AnimationIndex
                .GetClipsForBinding(EditorCurveBinding.FloatCurve(path, typeof(Transform), property)).Any());
        }

        private static Vector3 GetRestDirectionInAvatarSpace(
            Transform avatarRoot, Transform root, VRCPhysBoneBase physBone)
        {
            var worldDirection = Vector3.zero;
            foreach (Transform child in root)
            {
                if (physBone.ignoreTransforms != null && physBone.ignoreTransforms.Contains(child)) continue;
                var delta = child.position - root.position;
                if (delta.sqrMagnitude > 0.000001f) worldDirection += delta.normalized;
            }

            if (worldDirection.sqrMagnitude < 0.000001f && physBone.endpointPosition.sqrMagnitude > 0.000001f)
            {
                worldDirection = root.TransformDirection(physBone.endpointPosition.normalized);
            }
            if (worldDirection.sqrMagnitude < 0.000001f)
            {
                worldDirection = avatarRoot.TransformDirection(Vector3.down);
            }

            return avatarRoot.InverseTransformDirection(worldDirection).normalized;
        }

        private static VirtualMotion CreateRuntimeMotion(
            IList<WindTarget> targets,
            WindBakeSettings bakeSettings,
            BakeStats stats,
            VirtualClip neutral,
            WindControls controls,
            string suffix)
        {
            VirtualMotion direction;
            if (controls.Elevation == null)
            {
                direction = CreateDirectionMotion(targets, bakeSettings, stats, 0f, controls, "", suffix);
            }
            else
            {
                // Three elevation bakes (down / level / up); the radial blends between them.
                var maxElevation = bakeSettings.MaxElevation;
                var down = CreateDirectionMotion(targets, bakeSettings, stats, -maxElevation, controls, " Down", suffix);
                var level = CreateDirectionMotion(targets, bakeSettings, stats, 0f, controls, " Level", suffix);
                var up = CreateDirectionMotion(targets, bakeSettings, stats, maxElevation, controls, " Up", suffix);
                direction = Simple1D("Wind Elevation" + suffix, controls.Elevation,
                    ImmutableList.Create(Child(down, 0f), Child(level, 0.5f), Child(up, 1f)));
            }
            return Simple1D("Wind Strength" + suffix, controls.Strength,
                ImmutableList.Create(Child(neutral, 0f), Child(direction, 1f)));
        }

        private static VirtualBlendTree.VirtualChildMotion Child(VirtualMotion motion, float threshold)
        {
            return new VirtualBlendTree.VirtualChildMotion { Motion = motion, Threshold = threshold, TimeScale = 1f };
        }

        private static VirtualBlendTree Simple1D(string name, string parameter,
            ImmutableList<VirtualBlendTree.VirtualChildMotion> children)
        {
            var tree = VirtualBlendTree.Create(name);
            tree.BlendType = BlendTreeType.Simple1D;
            tree.BlendParameter = parameter;
            tree.UseAutomaticThresholds = false;
            tree.MinThreshold = 0f;
            tree.MaxThreshold = 1f;
            tree.Children = children;
            return tree;
        }

        private static VirtualStateTransition CreateToggleTransition(
            VirtualState destination, string boolParameter, bool whenTrue)
        {
            var transition = VirtualStateTransition.Create();
            transition.SetDestination(destination);
            transition.ExitTime = null;
            transition.HasFixedDuration = true;
            transition.Duration = 0.5f;
            transition.CanTransitionToSelf = false;
            transition.Conditions = ImmutableList.Create(new AnimatorCondition
            {
                mode = whenTrue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot,
                parameter = boolParameter,
                threshold = 0f
            });
            return transition;
        }

        private static VirtualMotion CreateDirectionMotion(
            IList<WindTarget> targets,
            WindBakeSettings bakeSettings,
            BakeStats stats,
            float elevationDegrees,
            WindControls controls,
            string tag,
            string suffix)
        {
            var children = ImmutableList.CreateBuilder<VirtualBlendTree.VirtualChildMotion>();

            // Eight compass directions, clockwise from +Z. Adjacent clips blend linearly, and
            // with only four directions the diagonal lean would drop to ~70% of the cardinal
            // lean; eight keeps that dip under 10%.
            const int directionCount = 8;
            var directions = new Vector3[directionCount];
            for (var index = 0; index < directionCount; index++)
            {
                var radians = index * (2f * Mathf.PI / directionCount);
                directions[index] = new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians));
            }

            for (var index = 0; index < directions.Length; index++)
            {
                var calm = CreateWindClip(targets, bakeSettings, stats, directions[index], elevationDegrees, 0f,
                    "Wind Calm" + tag + " " + index + suffix);
                var turbulent = CreateWindClip(targets, bakeSettings, stats, directions[index], elevationDegrees, 1f,
                    "Wind Turbulent" + tag + " " + index + suffix);
                var turbulenceTree = Simple1D("Wind Turbulence" + tag + " " + index + suffix,
                    controls.Turbulence, ImmutableList.Create(Child(calm, 0f), Child(turbulent, 1f)));
                children.Add(Child(turbulenceTree, (float)index / directionCount));
            }

            // Threshold 1.0 reuses the +Z motion so the radial wraps seamlessly. Amplitude
            // blending is linear; the Strength speed parameter supplies the tempo change.
            children.Add(Child(children[0].Motion, 1f));
            return Simple1D("Wind Direction" + tag + suffix, controls.Direction, children.ToImmutable());
        }

        private static WindBakeSettings CreateBakeSettings(KazamachiWind wind)
        {
            return WindBakeSettings.Create(wind.loopDuration, wind.sampleRate, wind.maxWindSpeed,
                wind.maxLeanAngle, wind.gustStrength, wind.gustFrequency, wind.seed, wind.maxElevationAngle);
        }

        private static VirtualClip CreateWindClip(IList<WindTarget> targets, WindBakeSettings settings,
            BakeStats stats, Vector3 direction, float elevationDegrees, float turbulence, string name)
        {
            return CreateBakedClip(WindClipBaker.Bake(settings, targets, direction, elevationDegrees, turbulence),
                settings, stats, name, targets.Count * 4L * (settings.SampleCount + 1));
        }

        private static VirtualClip CreateNeutralClip(IList<WindTarget> targets, WindBakeSettings settings,
            BakeStats stats, string name)
        {
            return CreateBakedClip(WindClipBaker.BakeNeutral(settings, targets), settings, stats, name,
                targets.Count * 8L);
        }

        private static VirtualClip CreateBakedClip(IList<BakedTarget> targets, WindBakeSettings settings,
            BakeStats stats, string name, long legacyKeys)
        {
            var clip = VirtualClip.Create(name);
            stats.Clips++;
            stats.LegacyKeys += legacyKeys;
            foreach (var target in targets)
            {
                stats.StoredKeys += target.KeyCount * 4L;
                for (var i = 0; i < 4; i++)
                {
                    var binding = EditorCurveBinding.FloatCurve(target.Path, typeof(Transform), LocalRotationProperties[i]);
                    clip.SetFloatCurve(binding, target.Curves[i]);
                }
            }
            ApplyLoopSettings(clip, settings.SampleRate);
            return clip;
        }

        private static void ApplyLoopSettings(VirtualClip clip, int sampleRate)
        {
            clip.FrameRate = sampleRate;
            clip.WrapMode = WrapMode.Loop;
            var clipSettings = clip.Settings;
            clipSettings.loopTime = true;
            clipSettings.loopBlend = true;
            clip.Settings = clipSettings;
        }

        private static void AddAnimatorParameter(
            VirtualAnimatorController controller,
            string name,
            float value,
            AnimatorControllerParameterType type = AnimatorControllerParameterType.Float)
        {
            controller.SetParameter(name, new AnimatorControllerParameter
            {
                name = name,
                type = type,
                defaultFloat = value,
                defaultInt = Mathf.RoundToInt(value),
                defaultBool = value >= 0.5f
            });
        }

        private static Texture2D LoadIcon(string guid)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        private static RuntimeControlState CreateRuntimeControlState(BuildContext context)
        {
            var descriptor = context.AvatarRootObject.GetComponent<VRCAvatarDescriptor>();
            if (descriptor == null) return null;

            var parameters = descriptor.expressionParameters != null
                ? Object.Instantiate(descriptor.expressionParameters)
                : ScriptableObject.CreateInstance<VRCExpressionParameters>();
            parameters.name = "Kazamachi Parameters";
            var rootMenu = descriptor.expressionsMenu != null
                ? Object.Instantiate(descriptor.expressionsMenu)
                : ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            rootMenu.name = "Kazamachi Menu";
            if (rootMenu.controls == null) rootMenu.controls = new List<VRCExpressionsMenu.Control>();

            return new RuntimeControlState
            {
                Descriptor = descriptor,
                Parameters = parameters,
                ParameterList = (parameters.parameters ?? Array.Empty<VRCExpressionParameters.Parameter>()).ToList(),
                RootMenu = rootMenu
            };
        }

        private static VRCExpressionsMenu.Control Radial(string name, Texture2D icon, string parameter)
        {
            return new VRCExpressionsMenu.Control
            {
                name = name,
                icon = icon,
                type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                subParameters = new[] { new VRCExpressionsMenu.Control.Parameter { name = parameter } }
            };
        }

        private static void InstallRuntimeControls(BuildContext context, RuntimeControlState state,
            WindControls values, MenuIcons icons)
        {
            if (state == null) return;
            AddExpressionParameter(state.ParameterList, values.Enabled, 0f, VRCExpressionParameters.ValueType.Bool);
            AddExpressionParameter(state.ParameterList, values.Direction, values.StartDirection);
            AddExpressionParameter(state.ParameterList, values.Strength, values.StartStrength);
            AddExpressionParameter(state.ParameterList, values.Turbulence, values.StartTurbulence);
            if (values.Elevation != null) AddExpressionParameter(state.ParameterList, values.Elevation, values.StartElevation);

            var windMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            windMenu.name = values.Name;
            windMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Wind",
                    icon = icons.Wind,
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = values.Enabled },
                    value = 1f
                },
                Radial("Direction", icons.Direction, values.Direction)
            };
            if (values.Elevation != null) windMenu.controls.Add(Radial("Elevation", icons.Elevation, values.Elevation));
            windMenu.controls.Add(Radial("Strength", icons.Strength, values.Strength));
            windMenu.controls.Add(Radial("Turbulence", icons.Turbulence, values.Turbulence));
            context.AssetSaver.SaveAsset(windMenu);

            // Full menus are the norm on finished avatars, so walk down any trailing "More"
            // page (Modular Avatar's overflow convention) and split again if that is full too.
            var targetMenu = state.RootMenu;
            while (targetMenu.controls.Count >= VRCExpressionsMenu.MAX_CONTROLS)
            {
                var last = targetMenu.controls[targetMenu.controls.Count - 1];
                var isMorePage = last != null && last.type == VRCExpressionsMenu.Control.ControlType.SubMenu &&
                                 last.subMenu != null && last.name == "More";
                if (!isMorePage) break;
                var page = Object.Instantiate(last.subMenu);
                page.name = last.subMenu.name;
                if (page.controls == null) page.controls = new List<VRCExpressionsMenu.Control>();
                last.subMenu = page;
                context.AssetSaver.SaveAsset(page);
                targetMenu = page;
            }
            targetMenu.controls.Add(new VRCExpressionsMenu.Control
            {
                name = windMenu.name,
                icon = icons.Kazamachi,
                type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                subMenu = windMenu
            });
            SplitOverflow(context, targetMenu);
        }

        private static void FinalizeRuntimeControls(BuildContext context, RuntimeControlState state)
        {
            if (state == null) return;
            state.Parameters.parameters = state.ParameterList.ToArray();
            context.AssetSaver.SaveAsset(state.Parameters);
            state.Descriptor.expressionParameters = state.Parameters;
            context.AssetSaver.SaveAsset(state.RootMenu);
            state.Descriptor.expressionsMenu = state.RootMenu;
            state.Descriptor.customExpressions = true;
        }

        private static void SplitOverflow(BuildContext context, VRCExpressionsMenu menu)
        {
            while (menu.controls.Count > VRCExpressionsMenu.MAX_CONTROLS)
            {
                const int keepCount = VRCExpressionsMenu.MAX_CONTROLS - 1;
                var page = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
                page.name = "More";
                page.controls = menu.controls.Skip(keepCount).ToList();
                menu.controls.RemoveRange(keepCount, menu.controls.Count - keepCount);
                menu.controls.Add(new VRCExpressionsMenu.Control
                {
                    name = "More",
                    type = VRCExpressionsMenu.Control.ControlType.SubMenu,
                    subMenu = page
                });
                context.AssetSaver.SaveAsset(page);
                menu = page;
            }
        }

        private static void AddExpressionParameter(
            ICollection<VRCExpressionParameters.Parameter> parameters, string name, float defaultValue,
            VRCExpressionParameters.ValueType valueType = VRCExpressionParameters.ValueType.Float)
        {
            var parameter = parameters.FirstOrDefault(item => item != null && item.name == name);
            if (parameter == null)
            {
                parameter = new VRCExpressionParameters.Parameter { name = name };
                parameters.Add(parameter);
            }
            parameter.valueType = valueType;
            parameter.defaultValue = defaultValue;
            parameter.saved = false;
            parameter.networkSynced = true;
        }

        private static int Depth(Transform transform)
        {
            var result = 0;
            while (transform != null)
            {
                result++;
                transform = transform.parent;
            }
            return result;
        }

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;
            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }
            return string.Join("/", names.ToArray());
        }

        private static void Cleanup(IEnumerable<KazamachiWind> settings, Transform avatarRoot)
        {
            foreach (var item in settings)
            {
                if (item == null) continue;
                var gameObject = item.gameObject;
                Object.DestroyImmediate(item);
                if (gameObject != null && gameObject.transform != avatarRoot &&
                    gameObject.transform.childCount == 0 &&
                    gameObject.GetComponents<Component>().Length == 1)
                {
                    Object.DestroyImmediate(gameObject);
                }
            }
        }
    }
}

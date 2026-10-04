using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using nadena.dev.ndmf;
using nadena.dev.ndmf.animator;
using nadena.dev.ndmf.fluent;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using TenteEEEE.Kazamachi;
using nadena.dev.ndmf.localization;
using VRC.Dynamics;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using Object = UnityEngine.Object;

[assembly: ExportsPlugin(typeof(TenteEEEE.Kazamachi.Editor.KazamachiBuildPlugin))]

namespace TenteEEEE.Kazamachi.Editor
{
    public sealed class KazamachiBuildPlugin : Plugin<KazamachiBuildPlugin>
    {
        private static readonly string[] RotationProperties =
        {
            "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w",
            "localEulerAnglesRaw.x", "localEulerAnglesRaw.y", "localEulerAnglesRaw.z",
            "localEulerAnglesBaked.x", "localEulerAnglesBaked.y", "localEulerAnglesBaked.z",
            "localEulerAngles.x", "localEulerAngles.y", "localEulerAngles.z"
        };
        private static int _generatedClipCount;
        private static int _totalKeyframeCount;

        private sealed class RuntimeControlState
        {
            public VRCAvatarDescriptor Descriptor;
            public VRCExpressionParameters Parameters;
            public List<VRCExpressionParameters.Parameter> ParameterList;
            public VRCExpressionsMenu RootMenu;
        }

        private sealed class ParameterBudgetError : SimpleError
        {
            private const string Key = "tenteeeee.kazamachi.parameter-budget";
            private const string Message = "Expression Parametersの同期予算を超えるため、このKazamachi設定はローカル専用で動作します。";
            private static readonly Localizer LocalizedMessage = new Localizer("ja-jp", () =>
                new List<(string, Func<string, string>)>
                {
                    ("ja-jp", key => key == Key ? Message : null)
                });

            public override Localizer Localizer => LocalizedMessage;
            public override string TitleKey => Key;
            public override ErrorSeverity Severity => ErrorSeverity.NonFatal;
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
            _generatedClipCount = 0;
            _totalKeyframeCount = 0;
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
            var controls = CreateRuntimeControlState(context);

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
                var groupedTargets = ResolveTargets(wind, avatarRoot)
                    .GroupBy(ResolveRoot)
                    .Where(group => group.Key != null)
                    .OrderBy(group => Depth(group.Key))
                    .ToArray();

                foreach (var group in groupedTargets)
                {
                    var root = group.Key;
                    var physBones = group.Where(pb => pb != null).ToArray();

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

                if (settingRootCount > 0)
                {
                    var layerName = settings.Length == 1
                        ? "Kazamachi"
                        : "Kazamachi " + (windIndex + 1);
                    var parameterPrefix = settings.Length == 1
                        ? "TenteEEEE/Kazamachi"
                        : "TenteEEEE/Kazamachi" + (windIndex + 1);
                    var enabledParameter = parameterPrefix + "/Enabled";
                    var directionParameter = parameterPrefix + "/Direction";
                    var strengthParameter = parameterPrefix + "/Strength";
                    var turbulenceParameter = parameterPrefix + "/Turbulence";
                    var elevationParameter = wind.verticalControl ? parameterPrefix + "/Elevation" : null;

                    // Bool so a synced Enabled costs 1 bit instead of a Float's 8.
                    AddAnimatorParameter(fx, enabledParameter, wind.startEnabled ? 1f : 0f,
                        AnimatorControllerParameterType.Bool);
                    AddAnimatorParameter(fx, directionParameter,
                        Mathf.Clamp(wind.initialDirectionAngle, 0f, 360f) / 360f);
                    AddAnimatorParameter(fx, strengthParameter, Mathf.Clamp01(wind.initialStrength));
                    AddAnimatorParameter(fx, turbulenceParameter, Mathf.Clamp01(wind.initialTurbulence));
                    if (elevationParameter != null)
                    {
                        AddAnimatorParameter(fx, elevationParameter, Mathf.Clamp01(wind.initialElevation));
                    }

                    var suffix = " " + (windIndex + 1);
                    var neutral = CreateNeutralClip(targetInfos, wind, "Wind Neutral" + suffix);
                    var motion = CreateRuntimeMotion(targetInfos, avatarRoot, wind, neutral,
                        directionParameter, strengthParameter, turbulenceParameter, elevationParameter, suffix);
                    var layer = fx.AddLayer(LayerPriority.Default, layerName);
                    layer.DefaultWeight = 1f;
                    layer.BlendingMode = AnimatorLayerBlendingMode.Override;

                    // Off/Wind are separate states switched by the Bool so the toggle stays
                    // a single bit; the cross-fade keeps ON/OFF from snapping.
                    var offState = layer.StateMachine.AddState("Wind Off");
                    offState.WriteDefaultValues = writeDefaults;
                    offState.Motion = neutral;
                    var windState = layer.StateMachine.AddState("Wind");
                    windState.WriteDefaultValues = writeDefaults;
                    windState.SpeedParameter = strengthParameter;
                    windState.Speed = 1f;
                    windState.Motion = motion;
                    offState.Transitions = ImmutableList.Create(
                        CreateToggleTransition(windState, enabledParameter, true));
                    windState.Transitions = ImmutableList.Create(
                        CreateToggleTransition(offState, enabledParameter, false));
                    layer.StateMachine.DefaultState = wind.startEnabled ? windState : offState;
                    InstallRuntimeControls(context, controls, enabledParameter,
                        directionParameter, strengthParameter, turbulenceParameter, elevationParameter,
                        wind.startEnabled, Mathf.Clamp(wind.initialDirectionAngle, 0f, 360f) / 360f,
                        wind.initialStrength, wind.initialTurbulence, wind.initialElevation,
                        wind.syncToOthers, windIndex);
                    generatedLayerCount++;
                }
            }

            if (generatedLayerCount > 0)
            {
                FinalizeRuntimeControls(context, controls);
                Debug.Log("[Kazamachi] " + processedRoots.Count +
                          " 個のPhysBoneルートへ風を生成しました。除外: " + skippedCount +
                          "、生成クリップ: " + _generatedClipCount +
                          "、合計キー: " + _totalKeyframeCount);
            }
            else
            {
                Debug.LogWarning("[Kazamachi] 安全に処理できるPhysBoneルートがありませんでした。");
            }

            Cleanup(allSettings, context.AvatarRootTransform);
        }

        private static IEnumerable<VRCPhysBoneBase> ResolveTargets(KazamachiWind wind, Transform avatarRoot)
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
                    .Where(pb => !excluded.Contains(pb));
                if (wind.targetSelection == KazamachiWind.TargetSelection.Auto)
                {
                    targets = targets.Where(pb => !IsAutoExcluded(pb, wind, avatarRoot));
                }
            }

            return targets.Where(pb => pb != null && pb.transform.IsChildOf(avatarRoot)).Distinct();
        }

        // Auto only looks at the component's own GameObject and its root. Walking up the
        // hierarchy would also catch accessories parented under a breast bone.
        private static bool IsAutoExcluded(VRCPhysBoneBase physBone, KazamachiWind wind, Transform avatarRoot)
        {
            if (physBone == null || wind.autoExcludeNameKeywords == null) return false;
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
            if (physBone == null) return null;
            return physBone.rootTransform != null ? physBone.rootTransform : physBone.transform;
        }

        private static bool IsSafeRoot(Transform root, Transform avatarRoot, HashSet<Transform> humanoidBones)
        {
            return root != null && root != avatarRoot && root.parent != null &&
                   root.IsChildOf(avatarRoot) && !humanoidBones.Contains(root);
        }

        // Match the avatar's existing Write Defaults convention (same rule as Modular Avatar's
        // Merge Animator). Mixing WD On/Off states in one controller makes Unity stop resetting
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
            IList<TargetInfo> targets,
            Transform avatarRoot,
            KazamachiWind wind,
            VirtualClip neutral,
            string directionParameter,
            string strengthParameter,
            string turbulenceParameter,
            string elevationParameter,
            string suffix)
        {
            VirtualMotion direction;
            if (elevationParameter == null)
            {
                direction = CreateDirectionMotion(targets, avatarRoot, wind, 0f,
                    directionParameter, turbulenceParameter, "", suffix);
            }
            else
            {
                // Three elevation bakes (down / level / up); the radial blends between them.
                var maxElevation = Mathf.Clamp(wind.maxElevationAngle, 0f, 80f);
                var elevation = VirtualBlendTree.Create("Wind Elevation" + suffix);
                elevation.BlendType = BlendTreeType.Simple1D;
                elevation.BlendParameter = elevationParameter;
                elevation.UseAutomaticThresholds = false;
                elevation.MinThreshold = 0f;
                elevation.MaxThreshold = 1f;
                elevation.Children = ImmutableList.Create(
                    new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = CreateDirectionMotion(targets, avatarRoot, wind, -maxElevation,
                            directionParameter, turbulenceParameter, " Down", suffix),
                        Threshold = 0f, TimeScale = 1f
                    },
                    new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = CreateDirectionMotion(targets, avatarRoot, wind, 0f,
                            directionParameter, turbulenceParameter, " Level", suffix),
                        Threshold = 0.5f, TimeScale = 1f
                    },
                    new VirtualBlendTree.VirtualChildMotion
                    {
                        Motion = CreateDirectionMotion(targets, avatarRoot, wind, maxElevation,
                            directionParameter, turbulenceParameter, " Up", suffix),
                        Threshold = 1f, TimeScale = 1f
                    });
                direction = elevation;
            }
            var strength = VirtualBlendTree.Create("Wind Strength" + suffix);
            strength.BlendType = BlendTreeType.Simple1D;
            strength.BlendParameter = strengthParameter;
            strength.UseAutomaticThresholds = false;
            strength.MinThreshold = 0f;
            strength.MaxThreshold = 1f;
            strength.Children = ImmutableList.Create(
                new VirtualBlendTree.VirtualChildMotion { Motion = neutral, Threshold = 0f, TimeScale = 1f },
                new VirtualBlendTree.VirtualChildMotion { Motion = direction, Threshold = 1f, TimeScale = 1f });
            return strength;
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
            IList<TargetInfo> targets,
            Transform avatarRoot,
            KazamachiWind wind,
            float elevationDegrees,
            string directionParameter,
            string turbulenceParameter,
            string tag,
            string suffix)
        {
            var directional = VirtualBlendTree.Create("Wind Direction" + tag + suffix);
            directional.BlendType = BlendTreeType.Simple1D;
            directional.BlendParameter = directionParameter;
            directional.UseAutomaticThresholds = false;
            directional.MinThreshold = 0f;
            directional.MaxThreshold = 1f;

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
                var calm = CreateWindClip(targets, avatarRoot, wind, directions[index], elevationDegrees, 0f,
                    "Wind Calm" + tag + " " + index + suffix);
                var turbulent = CreateWindClip(targets, avatarRoot, wind, directions[index], elevationDegrees, 1f,
                    "Wind Turbulent" + tag + " " + index + suffix);
                var turbulenceTree = VirtualBlendTree.Create("Wind Turbulence" + tag + " " + index + suffix);
                turbulenceTree.BlendType = BlendTreeType.Simple1D;
                turbulenceTree.BlendParameter = turbulenceParameter;
                turbulenceTree.UseAutomaticThresholds = false;
                turbulenceTree.MinThreshold = 0f;
                turbulenceTree.MaxThreshold = 1f;
                turbulenceTree.Children = ImmutableList.Create(
                    new VirtualBlendTree.VirtualChildMotion { Motion = calm, Threshold = 0f, TimeScale = 1f },
                    new VirtualBlendTree.VirtualChildMotion { Motion = turbulent, Threshold = 1f, TimeScale = 1f });
                children.Add(new VirtualBlendTree.VirtualChildMotion
                {
                    Motion = turbulenceTree,
                    Threshold = (float)index / directionCount,
                    TimeScale = 1f
                });
            }

            // Threshold 1.0 reuses the +Z motion so the radial wraps seamlessly. Amplitude
            // blending is linear; the Strength speed parameter supplies the tempo change.
            children.Add(new VirtualBlendTree.VirtualChildMotion
            {
                Motion = children[0].Motion,
                Threshold = 1f,
                TimeScale = 1f
            });
            directional.Children = children.ToImmutable();
            return directional;
        }

        private static VirtualClip CreateWindClip(
            IEnumerable<TargetInfo> targets,
            Transform avatarRoot,
            KazamachiWind wind,
            Vector3 direction,
            float elevationDegrees,
            float turbulence,
            string name)
        {
            var clip = VirtualClip.Create(name);
            _generatedClipCount++;
            var duration = Mathf.Clamp(wind.loopDuration, 2f, 30f);
            var sampleRate = Mathf.Clamp(wind.sampleRate, 10, 60);
            var samples = Mathf.Max(2, Mathf.CeilToInt(duration * sampleRate));
            _totalKeyframeCount += targets.Count() * 4 * (samples + 1);
            foreach (var target in targets)
            {
                AddRotationCurves(clip, target.Path, avatarRoot, target.Root, target.RestDirection,
                    wind, direction, elevationDegrees, turbulence);
            }

            clip.FrameRate = Mathf.Clamp(wind.sampleRate, 10, 60);
            clip.WrapMode = WrapMode.Loop;
            var settings = clip.Settings;
            settings.loopTime = true;
            settings.loopBlend = true;
            clip.Settings = settings;
            return clip;
        }

        private static VirtualClip CreateNeutralClip(
            IEnumerable<TargetInfo> targets, KazamachiWind wind, string name)
        {
            // Bake each root's authored local rotation explicitly (two constant keys) so the
            // Strength/Enabled blends have a real rest pose to blend toward instead of relying
            // on Unity's default-value handling for missing curves.
            var clip = VirtualClip.Create(name);
            _generatedClipCount++;
            _totalKeyframeCount += targets.Count() * 8;
            var duration = Mathf.Clamp(wind.loopDuration, 2f, 30f);
            var properties = new[] { "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
            foreach (var target in targets)
            {
                var rotation = target.Root.localRotation;
                var components = new[] { rotation.x, rotation.y, rotation.z, rotation.w };
                for (var i = 0; i < 4; i++)
                {
                    var curve = new AnimationCurve(new Keyframe(0f, components[i]), new Keyframe(duration, components[i]));
                    clip.SetFloatCurve(EditorCurveBinding.FloatCurve(target.Path, typeof(Transform), properties[i]), curve);
                }
            }

            clip.FrameRate = Mathf.Clamp(wind.sampleRate, 10, 60);
            clip.WrapMode = WrapMode.Loop;
            var settings = clip.Settings;
            settings.loopTime = true;
            settings.loopBlend = true;
            clip.Settings = settings;
            return clip;
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

        private static void InstallRuntimeControls(
            BuildContext context,
            RuntimeControlState state,
            string enabledParameter,
            string directionParameter,
            string strengthParameter,
            string turbulenceParameter,
            string elevationParameter,
            bool startEnabled,
            float startDirection,
            float startStrength,
            float startTurbulence,
            float startElevation,
            bool synced,
            int index)
        {
            if (state == null) return;

            var firstNewParameter = state.ParameterList.Count;
            AddExpressionParameter(state.ParameterList, enabledParameter, startEnabled ? 1f : 0f, synced,
                VRCExpressionParameters.ValueType.Bool);
            AddExpressionParameter(state.ParameterList, directionParameter, Mathf.Clamp01(startDirection), synced);
            AddExpressionParameter(state.ParameterList, strengthParameter, Mathf.Clamp01(startStrength), synced);
            AddExpressionParameter(state.ParameterList, turbulenceParameter, Mathf.Clamp01(startTurbulence), synced);
            if (elevationParameter != null)
            {
                AddExpressionParameter(state.ParameterList, elevationParameter, Mathf.Clamp01(startElevation), synced);
            }
            state.Parameters.parameters = state.ParameterList.ToArray();
            if (synced && state.Parameters.CalcTotalCost() >
                VRCExpressionParameters.MAX_PARAMETER_COST)
            {
                for (var i = firstNewParameter; i < state.ParameterList.Count; i++)
                    state.ParameterList[i].networkSynced = false;
                ErrorReport.WithContextObject(state.Descriptor.gameObject,
                    () => ErrorReport.ReportError(new ParameterBudgetError()));
            }

            var windMenu = ScriptableObject.CreateInstance<VRCExpressionsMenu>();
            windMenu.name = index == 0 ? "Kazamachi" : "Kazamachi " + (index + 1);
            windMenu.controls = new List<VRCExpressionsMenu.Control>
            {
                new VRCExpressionsMenu.Control
                {
                    name = "Wind",
                    type = VRCExpressionsMenu.Control.ControlType.Toggle,
                    parameter = new VRCExpressionsMenu.Control.Parameter { name = enabledParameter },
                    value = 1f
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Direction",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter { name = directionParameter }
                    }
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Strength",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter { name = strengthParameter }
                    }
                },
                new VRCExpressionsMenu.Control
                {
                    name = "Turbulence",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter { name = turbulenceParameter }
                    }
                }
            };
            if (elevationParameter != null)
            {
                windMenu.controls.Insert(2, new VRCExpressionsMenu.Control
                {
                    name = "Elevation",
                    type = VRCExpressionsMenu.Control.ControlType.RadialPuppet,
                    subParameters = new[]
                    {
                        new VRCExpressionsMenu.Control.Parameter { name = elevationParameter }
                    }
                });
            }
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
            bool synced, VRCExpressionParameters.ValueType valueType = VRCExpressionParameters.ValueType.Float)
        {
            if (parameters.Any(parameter => parameter != null && parameter.name == name)) return;
            parameters.Add(new VRCExpressionParameters.Parameter
            {
                name = name,
                valueType = valueType,
                defaultValue = defaultValue,
                saved = false,
                networkSynced = synced
            });
        }

        private static void AddRotationCurves(
            VirtualClip clip,
            string path,
            Transform avatarRoot,
            Transform root,
            Vector3 restDirectionAvatar,
            KazamachiWind wind,
            Vector3 direction,
            float elevationDegrees,
            float turbulence)
        {
            var duration = Mathf.Clamp(wind.loopDuration, 2f, 30f);
            var sampleRate = Mathf.Clamp(wind.sampleRate, 10, 60);
            var samples = Mathf.Max(2, Mathf.CeilToInt(duration * sampleRate));
            var curves = new[] { new AnimationCurve(), new AnimationCurve(), new AnimationCurve(), new AnimationCurve() };
            var previous = Quaternion.identity;
            var first = Quaternion.identity;

            for (var i = 0; i <= samples; i++)
            {
                var normalizedTime = i == samples ? 0f : (float)i / samples;
                var quaternion = CalculateRotation(avatarRoot, root, restDirectionAvatar, wind,
                    normalizedTime, direction, elevationDegrees, turbulence);
                if (i == 0) first = quaternion;
                if (i == samples) quaternion = first;
                if (i > 0 && Quaternion.Dot(previous, quaternion) < 0f)
                {
                    quaternion = new Quaternion(-quaternion.x, -quaternion.y, -quaternion.z, -quaternion.w);
                }

                var time = i == samples ? duration : duration * normalizedTime;
                curves[0].AddKey(time, quaternion.x);
                curves[1].AddKey(time, quaternion.y);
                curves[2].AddKey(time, quaternion.z);
                curves[3].AddKey(time, quaternion.w);
                previous = quaternion;
            }

            for (var curveIndex = 0; curveIndex < curves.Length; curveIndex++)
            {
                for (var keyIndex = 0; keyIndex < curves[curveIndex].length; keyIndex++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curves[curveIndex], keyIndex,
                        AnimationUtility.TangentMode.Auto);
                    AnimationUtility.SetKeyRightTangentMode(curves[curveIndex], keyIndex,
                        AnimationUtility.TangentMode.Auto);
                }
            }

            clip.SetFloatCurve(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.x"), curves[0]);
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.y"), curves[1]);
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.z"), curves[2]);
            clip.SetFloatCurve(EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation.w"), curves[3]);
        }

        private static Quaternion CalculateRotation(
            Transform avatarRoot,
            Transform root,
            Vector3 restDirectionAvatar,
            KazamachiWind wind,
            float time,
            Vector3 directionOverride,
            float elevationDegrees,
            float turbulence)
        {
            var direction = directionOverride.normalized;
            var side = Vector3.Cross(Vector3.up, direction);
            if (side.sqrMagnitude < 0.000001f) side = Vector3.Cross(Vector3.forward, direction);
            side.Normalize();
            var otherSide = Vector3.Cross(direction, side).normalized;

            var duration = Mathf.Clamp(wind.loopDuration, 2f, 30f);
            var sampleRate = Mathf.Clamp(wind.sampleRate, 10, 60);
            var baseCycles = KazamachiWind.ComputeBaseCycles(wind.maxWindSpeed, sampleRate, duration, out _);
            var flutterNoise = PeriodicNoise(time, wind.seed + 17, baseCycles);
            var directionNoise = PeriodicNoise(time, wind.seed + 53, baseCycles);
            var verticalNoise = PeriodicNoise(time, wind.seed + 97, baseCycles);
            var gustCount = Mathf.Max(1, Mathf.RoundToInt(wind.gustFrequency * duration));
            var gustPhase = Hash01(wind.seed + 131);
            var gustWave = wind.gustFrequency <= 0f
                ? 0f
                : Mathf.Pow(Mathf.Max(0f, Mathf.Sin(2f * Mathf.PI * (gustCount * time + gustPhase))), 6f);

            var variedDirection = (direction + side * (turbulence * 0.525f * directionNoise) +
                                   otherSide * (turbulence * 0.225f * verticalNoise)).normalized;
            // Steady lean is 55%; gusts add 45% at strength 1. Flutter and stronger gusts may overshoot.
            var leanUnits = 0.55f + 0.45f * wind.gustStrength * gustWave + turbulence * 0.375f * flutterNoise;
            var maxLeanAngle = Mathf.Clamp(wind.maxLeanAngle, 0f, 60f);
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
            var parentInAvatar = Quaternion.Inverse(avatarRoot.rotation) * root.parent.rotation;
            var windRotationParent = Quaternion.Inverse(parentInAvatar) * windRotationAvatar * parentInAvatar;
            return windRotationParent * root.localRotation;
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

using UnityEditor;
using UnityEngine;
using TenteEEEE.Kazamachi;
using VRC.SDK3.Avatars.Components;

namespace TenteEEEE.Kazamachi.Editor
{
    [CustomEditor(typeof(KazamachiWind))]
    public sealed class KazamachiEditor : UnityEditor.Editor
    {
        private SerializedProperty _initialDirectionAngle;
        private SerializedProperty _initialStrength, _initialTurbulence, _verticalControl;
        private SerializedProperty _initialElevation, _maxElevationAngle, _maxWindSpeed;
        private SerializedProperty _gustStrength, _gustFrequency, _maxLeanAngle;
        private SerializedProperty _loopDuration, _sampleRate, _seed, _targetSelection;
        private SerializedProperty _included, _excluded, _skipAnimated, _autoKeywords;
        private bool _showDetails;
        private string _detailsKey;

        private void OnEnable()
        {
            _detailsKey = "Kazamachi.Details." + target.GetInstanceID();
            _showDetails = SessionState.GetBool(_detailsKey, false);
            _initialDirectionAngle = Find("initialDirectionAngle"); _initialStrength = Find("initialStrength");
            _initialTurbulence = Find("initialTurbulence"); _verticalControl = Find("verticalControl");
            _initialElevation = Find("initialElevation"); _maxElevationAngle = Find("maxElevationAngle");
            _maxWindSpeed = Find("maxWindSpeed"); _gustStrength = Find("gustStrength");
            _gustFrequency = Find("gustFrequency"); _maxLeanAngle = Find("maxLeanAngle");
            _loopDuration = Find("loopDuration"); _sampleRate = Find("sampleRate"); _seed = Find("seed");
            _targetSelection = Find("targetSelection"); _included = Find("includedPhysBones");
            _excluded = Find("excludedPhysBones"); _skipAnimated = Find("skipRootsWithAnimation");
            _autoKeywords = Find("autoExcludeNameKeywords");
        }

        private SerializedProperty Find(string name) { return serializedObject.FindProperty(name); }
        private static GUIContent Label(SerializedProperty property, string label)
        {
            return new GUIContent(label, property.tooltip);
        }
        private static void Field(SerializedProperty property, string label, bool children = false)
        {
            EditorGUILayout.PropertyField(property, Label(property, label), children);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox("Builds looping wind animations for PhysBone roots. Use the Radial Puppet to adjust wind direction, strength, and turbulence at runtime.", MessageType.Info);
            var wind = (KazamachiWind)target;
            if (wind.GetComponentInParent<VRCAvatarDescriptor>() == null)
                EditorGUILayout.HelpBox("Place this component under a VRChat avatar descriptor.\nVRChatアバターのDescriptorの子階層に配置してください。", MessageType.Warning);

            EditorGUILayout.LabelField("Wind", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(new GUIContent(
                "Synced: starts OFF, other players see the wind too.",
                "風は常にOFFで開始します。操作は他のプレイヤーにも同期されます（同期パラメータ: Wind 1bit + 各スライダー8bit）。"), EditorStyles.miniLabel);
            Field(_initialDirectionAngle, "Initial Direction (deg)");
            EditorGUILayout.LabelField("0° = front  |  90° = right  |  180° = back  |  270° = left", EditorStyles.miniLabel);
            Field(_initialStrength, "Initial Strength"); Field(_initialTurbulence, "Initial Turbulence");
            Field(_verticalControl, "Vertical Control");
            if (_verticalControl.boolValue)
            {
                Field(_initialElevation, "Initial Elevation");
                EditorGUILayout.LabelField("0 = down  |  0.5 = level  |  1 = up", EditorStyles.miniLabel);
                Field(_maxElevationAngle, "Max Elevation Angle (deg)");
            }

            EditorGUILayout.Space(); EditorGUILayout.LabelField("Range", EditorStyles.boldLabel);
            Field(_maxWindSpeed, "Max Wind Speed (m/s)"); Field(_maxLeanAngle, "Max Lean Angle (deg)");
            Field(_gustStrength, "Gust Strength"); Field(_gustFrequency, "Gust Frequency (/s)");

            EditorGUILayout.Space(); EditorGUILayout.LabelField("Motion", EditorStyles.boldLabel);
            _showDetails = EditorGUILayout.Foldout(_showDetails, "Advanced", true);
            SessionState.SetBool(_detailsKey, _showDetails);
            if (_showDetails)
            {
                EditorGUI.indentLevel++;
                Field(_loopDuration, "Loop Duration (s)"); Field(_sampleRate, "Sample Rate"); Field(_seed, "Seed");
                EditorGUI.indentLevel--;
            }
            var duration = Mathf.Clamp(_loopDuration.floatValue, 2f, 30f);
            var rate = Mathf.Clamp(_sampleRate.intValue, 10, 60);
            int requestedCycles;
            var cappedCycles = KazamachiWind.ComputeBaseCycles(_maxWindSpeed.floatValue, rate, duration, out requestedCycles);
            if (cappedCycles < requestedCycles)
                EditorGUILayout.HelpBox("Wind speed is limited by the sample rate and loop duration.\nサンプルレートかループ秒数を上げると、より速い揺れを表現できます。", MessageType.Warning);

            EditorGUILayout.Space(); EditorGUILayout.LabelField("Targets", EditorStyles.boldLabel);
            Field(_targetSelection, "Target Selection");
            var mode = (KazamachiWind.TargetSelection)_targetSelection.enumValueIndex;
            if (mode == KazamachiWind.TargetSelection.IncludeOnly)
            {
                Field(_included, "Included PhysBones", true);
                if (_included.arraySize == 0)
                    EditorGUILayout.HelpBox("No PhysBones are selected, so no wind animation will be generated.\n対象PhysBoneを指定してください。", MessageType.Warning);
            }
            else if (mode == KazamachiWind.TargetSelection.Auto)
            {
                Field(_autoKeywords, "Auto Exclude Keywords", true); Field(_excluded, "Excluded PhysBones", true);
                EditorGUILayout.HelpBox("Auto mode excludes name matches and unsafe roots.\nGameObject名またはroot名のキーワード（既定は胸）を除外します。Humanoid本体、アバタールート、入れ子rootも安全のため除外し、判定結果はビルド時Consoleに表示します。", MessageType.Info);
            }
            else
            {
                Field(_excluded, "Excluded PhysBones", true);
                EditorGUILayout.HelpBox("All mode can include accessory and hidden outfit PhysBones.\n胸・アクセサリー・非表示衣装も対象になる場合があります。Humanoid本体、アバタールート、入れ子rootは除外されます。", MessageType.Warning);
            }
            Field(_skipAnimated, "Skip Animated Roots");
            if (_maxLeanAngle.floatValue <= 0f)
                EditorGUILayout.HelpBox("Max Lean Angle is zero, so no wind animation will be generated.\n最大傾斜角が0のため、風アニメーションは生成されません。", MessageType.Warning);
            serializedObject.ApplyModifiedProperties();
        }
    }
}

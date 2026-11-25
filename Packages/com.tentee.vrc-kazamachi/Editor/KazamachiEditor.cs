using UnityEditor;
using TenteEEEE.Kazamachi.Editor.Baking;
using UnityEngine;
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
        private SerializedProperty _included, _excluded, _autoKeywords;
        private SerializedProperty _skipHidden;
        private SerializedProperty _splitHumanoidRoot;
        private bool _showDetails;
        private string _detailsKey;

        private void OnEnable()
        {
            _detailsKey = "Kazamachi.Details." + target.GetInstanceID();
            _showDetails = SessionState.GetBool(_detailsKey, false);
            _initialDirectionAngle = Find("initialDirectionAngle");
            _initialStrength = Find("initialStrength");
            _initialTurbulence = Find("initialTurbulence");
            _verticalControl = Find("verticalControl");
            _initialElevation = Find("initialElevation");
            _maxElevationAngle = Find("maxElevationAngle");
            _maxWindSpeed = Find("maxWindSpeed");
            _gustStrength = Find("gustStrength");
            _gustFrequency = Find("gustFrequency");
            _maxLeanAngle = Find("maxLeanAngle");
            _loopDuration = Find("loopDuration");
            _sampleRate = Find("sampleRate");
            _seed = Find("seed");
            _targetSelection = Find("targetSelection");
            _included = Find("includedPhysBones");
            _excluded = Find("excludedPhysBones");
            _autoKeywords = Find("autoExcludeNameKeywords");
            _skipHidden = Find("skipNeverActivePhysBones");
            _splitHumanoidRoot = Find("splitHumanoidRootPhysBones");
        }

        private SerializedProperty Find(string name)
        {
            return serializedObject.FindProperty(name);
        }

        private static void Field(SerializedProperty property, string key, bool children = false)
        {
            EditorGUILayout.PropertyField(property, KazamachiText.Label(key), children);
        }

        private static GUIContent Content(string key)
        {
            return new GUIContent(KazamachiText.Line(key));
        }

        private void TargetModePopup()
        {
            var rect = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(rect, KazamachiText.Label(KazamachiText.K.TargetSelection), _targetSelection);
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.Popup(rect, KazamachiText.Label(KazamachiText.K.TargetSelection),
                _targetSelection.enumValueIndex,
                new[] { Content(KazamachiText.K.AllPhysBones), Content(KazamachiText.K.IncludeOnly), Content(KazamachiText.K.Auto) });
            if (EditorGUI.EndChangeCheck()) _targetSelection.enumValueIndex = selected;
            EditorGUI.EndProperty();
        }

        public override void OnInspectorGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(KazamachiText.Line(KazamachiText.K.Wind), EditorStyles.boldLabel);
                var languageRect = GUILayoutUtility.GetRect(90, EditorGUIUtility.singleLineHeight, GUILayout.Width(90));
                if (KazamachiText.DrawLanguagePopup(languageRect)) Repaint();
            }

            serializedObject.Update();
            EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.Intro), MessageType.Info);
            if (((KazamachiWind)target).GetComponentInParent<VRCAvatarDescriptor>() == null)
            {
                EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.AvatarWarning), MessageType.Warning);
            }

            EditorGUILayout.LabelField(KazamachiText.Label(KazamachiText.K.Synced), EditorStyles.miniLabel);
            Field(_initialDirectionAngle, KazamachiText.K.Direction);
            EditorGUILayout.LabelField(KazamachiText.Line(KazamachiText.K.DirectionHint), EditorStyles.miniLabel);
            Field(_initialStrength, KazamachiText.K.Strength);
            Field(_initialTurbulence, KazamachiText.K.Turbulence);
            Field(_verticalControl, KazamachiText.K.Vertical);
            if (_verticalControl.boolValue)
            {
                Field(_initialElevation, KazamachiText.K.Elevation);
                EditorGUILayout.LabelField(KazamachiText.Line(KazamachiText.K.ElevationHint), EditorStyles.miniLabel);
                Field(_maxElevationAngle, KazamachiText.K.MaxElevation);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(KazamachiText.Line(KazamachiText.K.Range), EditorStyles.boldLabel);
            Field(_maxWindSpeed, KazamachiText.K.MaxSpeed);
            Field(_maxLeanAngle, KazamachiText.K.MaxLean);
            Field(_gustStrength, KazamachiText.K.GustStrength);
            Field(_gustFrequency, KazamachiText.K.GustFrequency);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(KazamachiText.Line(KazamachiText.K.Motion), EditorStyles.boldLabel);
            _showDetails = EditorGUILayout.Foldout(_showDetails, KazamachiText.Line(KazamachiText.K.Advanced), true);
            SessionState.SetBool(_detailsKey, _showDetails);
            if (_showDetails)
            {
                EditorGUI.indentLevel++;
                Field(_loopDuration, KazamachiText.K.Loop);
                Field(_sampleRate, KazamachiText.K.SampleRate);
                Field(_seed, KazamachiText.K.Seed);
                EditorGUI.indentLevel--;
            }

            var bakeSettings = WindBakeSettings.Create(_loopDuration.floatValue, _sampleRate.intValue,
                _maxWindSpeed.floatValue, _maxLeanAngle.floatValue, _gustStrength.floatValue,
                _gustFrequency.floatValue, _seed.intValue, _maxElevationAngle.floatValue);
            int requestedCycles;
            var cappedCycles = WindBakeSettings.ComputeBaseCycles(bakeSettings.MaxWindSpeed, bakeSettings.SampleRate,
                bakeSettings.Duration, out requestedCycles);
            if (cappedCycles < requestedCycles)
            {
                EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.SpeedWarning), MessageType.Warning);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(KazamachiText.Line(KazamachiText.K.Targets), EditorStyles.boldLabel);
            TargetModePopup();
            var mode = (KazamachiWind.TargetSelection)_targetSelection.enumValueIndex;
            if (mode == KazamachiWind.TargetSelection.IncludeOnly)
            {
                Field(_included, KazamachiText.K.Included, true);
                if (_included.arraySize == 0)
                {
                    EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.EmptyIncluded), MessageType.Warning);
                }
            }
            else if (mode == KazamachiWind.TargetSelection.Auto)
            {
                Field(_autoKeywords, KazamachiText.K.AutoKeywords, true);
                Field(_excluded, KazamachiText.K.Excluded, true);
                EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.AutoHelp), MessageType.Info);
            }
            else
            {
                Field(_excluded, KazamachiText.K.Excluded, true);
                EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.AllHelp), MessageType.Warning);
            }

            if (mode != KazamachiWind.TargetSelection.IncludeOnly)
            {
                Field(_skipHidden, KazamachiText.K.SkipHidden);
            }
            Field(_splitHumanoidRoot, KazamachiText.K.SplitRoot);
            if (_maxLeanAngle.floatValue <= 0f)
            {
                EditorGUILayout.HelpBox(KazamachiText.Text(KazamachiText.K.ZeroLean), MessageType.Warning);
            }
            serializedObject.ApplyModifiedProperties();
        }
    }
}

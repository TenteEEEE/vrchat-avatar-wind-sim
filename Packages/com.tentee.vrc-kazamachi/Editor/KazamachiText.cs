using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TenteEEEE.Kazamachi.Editor
{
    internal enum InspectorLanguage { Japanese, English, Both }

    internal static class KazamachiText
    {
        internal sealed class Entry
        {
            internal readonly string Ja, En, JaTooltip, EnTooltip;

            internal Entry(string ja, string en, string jaTooltip = null, string enTooltip = null)
            {
                Ja = ja;
                En = en;
                JaTooltip = jaTooltip;
                EnTooltip = enTooltip;
            }
        }

        internal static class K
        {
            internal const string Intro = "Intro";
            internal const string AvatarWarning = "AvatarWarning";
            internal const string Wind = "Wind";
            internal const string Range = "Range";
            internal const string Motion = "Motion";
            internal const string Targets = "Targets";
            internal const string Synced = "Synced";
            internal const string Direction = "Direction";
            internal const string DirectionHint = "DirectionHint";
            internal const string Strength = "Strength";
            internal const string Turbulence = "Turbulence";
            internal const string Vertical = "Vertical";
            internal const string Elevation = "Elevation";
            internal const string ElevationHint = "ElevationHint";
            internal const string MaxElevation = "MaxElevation";
            internal const string MaxSpeed = "MaxSpeed";
            internal const string MaxLean = "MaxLean";
            internal const string GustStrength = "GustStrength";
            internal const string GustFrequency = "GustFrequency";
            internal const string Advanced = "Advanced";
            internal const string Loop = "Loop";
            internal const string SampleRate = "SampleRate";
            internal const string Seed = "Seed";
            internal const string TargetSelection = "TargetSelection";
            internal const string Auto = "Auto";
            internal const string AllPhysBones = "AllPhysBones";
            internal const string IncludeOnly = "IncludeOnly";
            internal const string Included = "Included";
            internal const string Excluded = "Excluded";
            internal const string AutoKeywords = "AutoKeywords";
            internal const string SkipHidden = "SkipHidden";
            internal const string SplitRoot = "SplitRoot";
            internal const string SpeedWarning = "SpeedWarning";
            internal const string EmptyIncluded = "EmptyIncluded";
            internal const string AutoHelp = "AutoHelp";
            internal const string AllHelp = "AllHelp";
            internal const string ZeroLean = "ZeroLean";
        }

        private const string PrefKey = "TenteEEEE.Kazamachi.InspectorLanguage";
        private static readonly string[] LanguageOptions = { "日本語", "English", "両方" };
        private static readonly Dictionary<string, GUIContent> LabelCache = new Dictionary<string, GUIContent>();
        private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>
        {
            { K.Intro, new Entry(
                "PhysBoneのrootにループする風アニメーションを生成します。実行時に風向・強さ・揺らぎを調整できます。",
                "Builds looping wind animations for PhysBone roots. Use the Radial Puppet to adjust wind direction, strength, and turbulence at runtime.") },
            { K.AvatarWarning, new Entry("VRChatアバターのDescriptorの子階層に配置してください。", "Place this component under a VRChat avatar descriptor.") },
            { K.Wind, new Entry("風", "Wind") },
            { K.Range, new Entry("範囲", "Range") },
            { K.Motion, new Entry("動き", "Motion") },
            { K.Targets, new Entry("対象", "Targets") },
            { K.Synced, new Entry(
                "同期: 風はOFFで開始し、他のプレイヤーにも表示されます。",
                "Synced: starts OFF, other players see the wind too.",
                "風は常にOFFで開始します。操作は他のプレイヤーにも同期されます（同期パラメータ: Wind 1bit + 各スライダー8bit）。",
                "Wind always starts OFF. Controls are synced to other players (synced parameters: Wind 1 bit + 8 bits per slider).") },
            { K.Direction, new Entry(
                "初期風向（度）", "Initial Direction (deg)",
                "アバター読み込み直後の風向です。0度は前方(+Z)、90度は右(+X)で、時計回りに指定します。",
                "Wind direction when the avatar loads. 0° is forward (+Z), 90° is right (+X); angles increase clockwise.") },
            { K.DirectionHint, new Entry("0° = 前  |  90° = 右  |  180° = 後  |  270° = 左", "0° = front  |  90° = right  |  180° = back  |  270° = left") },
            { K.Strength, new Entry(
                "初期の強さ", "Initial Strength",
                "アバター読み込み直後の風の強さです。0で無風、1で最大風速と最大傾斜角に達します。",
                "Wind strength when the avatar loads. 0 is calm; 1 reaches maximum wind speed and lean angle.") },
            { K.Turbulence, new Entry(
                "初期の揺らぎ", "Initial Turbulence",
                "アバター読み込み直後の揺らぎ量です。0で一定の風、1で最大の方向変化と強弱変化になります。",
                "Turbulence when the avatar loads. 0 gives steady wind; 1 gives the greatest changes in direction and strength.") },
            { K.Vertical, new Entry(
                "上下操作", "Vertical Control",
                "Expression Menuに上下方向の操作を追加します。既定ではOFFです。有効にすると生成クリップ数が17から49に増えます。",
                "Adds vertical control to the Expression Menu. Off by default. Enabling it increases generated clips from 17 to 49.") },
            { K.Elevation, new Entry(
                "初期の上下方向", "Initial Elevation",
                "アバター読み込み直後の上下方向です。0で下向き、0.5で水平、1で上向きになります。",
                "Vertical direction when the avatar loads. 0 is down, 0.5 is level, and 1 is up.") },
            { K.ElevationHint, new Entry("0 = 下  |  0.5 = 水平  |  1 = 上", "0 = down  |  0.5 = level  |  1 = up") },
            { K.MaxElevation, new Entry(
                "最大仰角（度）", "Max Elevation Angle (deg)",
                "上下操作を端まで動かしたときの最大仰角です。値を大きくすると上下の風が強くなり、吹き上げで髪などが持ち上がりやすくなります。",
                "Maximum elevation at either end of vertical control. Higher values strengthen vertical wind and can lift hair when blowing upward.") },
            { K.MaxSpeed, new Entry(
                "最大風速（m/s）", "Max Wind Speed (m/s)",
                "風の揺れの速さの上限です。値を上げると速く揺れますが、実際の上限はサンプルレートとループ秒数でも制限されます。",
                "Maximum wind motion speed. Higher values allow faster motion, within limits set by sample rate and loop duration.") },
            { K.MaxLean, new Entry(
                "最大傾斜角（度）", "Max Lean Angle (deg)",
                "強さ1での基準傾斜角です。定常風ではこの値の55%、突風強さ1のピークで設定値になります。揺らぎや突風強さが大きいと超える場合があります。",
                "Base lean angle at strength 1. Steady wind uses 55% of this value; a gust strength 1 peak reaches it. Turbulence or stronger gusts can exceed it.") },
            { K.GustStrength, new Entry(
                "突風の強さ", "Gust Strength",
                "突風ピークの倍率です。1で最大傾斜角相当、1より大きい値では基準傾斜角を超える突風になります。",
                "Gust peak multiplier. 1 reaches the maximum lean angle; values above 1 can exceed the base lean angle.") },
            { K.GustFrequency, new Entry(
                "突風の頻度（回/秒）", "Gust Frequency (/s)",
                "強さ1のときに発生する突風の平均回数です。1秒あたりの回数で指定し、0にすると突風がなくなります。",
                "Average gusts per second at strength 1. Set to 0 to disable gusts.") },
            { K.Advanced, new Entry("詳細設定", "Advanced") },
            { K.Loop, new Entry(
                "ループ時間（秒）", "Loop Duration (s)",
                "生成するアニメーションのループ時間です。長くすると同じサンプルレートでも速い揺れを表現できますが、アニメーションデータが増えます。",
                "Animation loop duration. Longer loops allow faster motion at the same sample rate, but increase animation data.") },
            { K.SampleRate, new Entry(
                "サンプルレート", "Sample Rate",
                "解析解像度の基準です。高くすると速い揺れをより正確に表現でき、揺れの速さの上限も変わります。実際の解析は4倍の密度で行い、保存キーは自動的に削減されます。",
                "Analysis resolution. Higher values represent fast motion more accurately and change the speed limit. Analysis uses 4× density; stored keys are reduced automatically.") },
            { K.Seed, new Entry(
                "シード", "Seed",
                "風の揺れ方を決める整数値です。同じ値と設定なら同じ周期パターンになり、変更すると別の揺れ方になります。",
                "Integer seed for wind motion. The same seed and settings produce the same loop pattern; changing it creates a different pattern.") },
            { K.TargetSelection, new Entry("対象の選択", "Target Selection") },
            { K.Auto, new Entry("自動", "Auto") },
            { K.AllPhysBones, new Entry("すべてのPhysBone", "All PhysBones") },
            { K.IncludeOnly, new Entry("指定したものだけ", "Include Only") },
            { K.Included, new Entry(
                "含めるPhysBone", "Included PhysBones",
                "Include Only選択時に風を適用するPhysBoneを指定します。空の場合は風アニメーションを生成しません。",
                "PhysBones to affect in Include Only mode. No wind animation is generated if this list is empty.") },
            { K.Excluded, new Entry(
                "除外するPhysBone", "Excluded PhysBones",
                "AutoまたはAll PhysBones選択時に、風の対象から外すPhysBoneを指定します（Include Onlyでは使いません）。",
                "PhysBones to exclude in Auto or All PhysBones mode. Not used in Include Only mode.") },
            { K.AutoKeywords, new Entry(
                "自動除外キーワード", "Auto Exclude Keywords",
                "Auto選択時に除外する名前の一部です。PhysBoneのGameObject名またはroot名に一致し、大文字小文字は区別しません。",
                "Name fragments to exclude in Auto mode. Matched against the PhysBone GameObject or root name, case-insensitively.") },
            { K.SkipHidden, new Entry(
                "非表示PhysBoneを除外", "Skip Hidden PhysBones",
                "非アクティブなGameObjectや無効なPhysBoneに有効化アニメーションがない場合、対象から除外します。VRCFuryがあるアバターでは判定を無効にします。指定したものだけモードでは適用しません。",
                "Excludes inactive GameObjects or disabled PhysBones without an activation animation. Disabled when VRCFury is present; not used in Include Only mode.") },
            { K.SplitRoot, new Entry(
                "Humanoid rootのPhysBoneを分割", "Split Humanoid-Root PhysBones",
                "rootがHumanoidボーン（Hipsなど）のPhysBoneを、直下の揺れる子ごとに分割して風の対象にします。スカートなどに有効ですが、PhysBoneコンポーネント数が増えるためパフォーマンスランクに影響します。Multi-Child TypeがIgnoreのPhysBoneのみ対象です。",
                "Splits a PhysBone rooted at a Humanoid bone (such as Hips) into targets for each direct simulated child. Useful for skirts, but adds PhysBone components and may affect performance rank. Only applies to PhysBones with Multi-Child Type set to Ignore.") },
            { K.SpeedWarning, new Entry("サンプルレートかループ秒数を上げると、より速い揺れを表現できます。", "Increase the sample rate or loop duration to represent faster motion.") },
            { K.EmptyIncluded, new Entry("対象PhysBoneを指定してください。", "Select at least one PhysBone.") },
            { K.AutoHelp, new Entry(
                "Autoモードでは名前の一致と安全でないrootを除外します。",
                "Auto mode excludes name matches and unsafe roots.",
                "GameObject名またはroot名のキーワード（既定は胸）を除外します。Humanoid本体、アバタールート、入れ子rootも安全のため除外し、判定結果はビルド時Consoleに表示します。",
                "Excludes name keywords on the GameObject or root (default: breast). Humanoid bones, avatar root, and nested roots are also excluded for safety. Results appear in the build Console.") },
            { K.AllHelp, new Entry(
                "胸・アクセサリー・非表示衣装も対象になる場合があります。",
                "Chest, accessories, and hidden outfit PhysBones may be included.",
                "胸・アクセサリー・非表示衣装も対象になる場合があります。Humanoid本体、アバタールート、入れ子rootは除外されます。",
                "May include chest, accessory, and hidden outfit PhysBones. Humanoid bones, avatar root, and nested roots are excluded.") },
            { K.ZeroLean, new Entry("最大傾斜角が0のため、風アニメーションは生成されません。", "No wind animation will be generated because Max Lean Angle is zero.") }
        };

        private static InspectorLanguage? _language;

        internal static InspectorLanguage Language
        {
            get
            {
                if (_language.HasValue) return _language.Value;

                var defaultLanguage = Application.systemLanguage == SystemLanguage.Japanese
                    ? InspectorLanguage.Japanese
                    : InspectorLanguage.English;
                if (!EditorPrefs.HasKey(PrefKey))
                {
                    _language = defaultLanguage;
                    return _language.Value;
                }

                var savedLanguage = (InspectorLanguage)EditorPrefs.GetInt(PrefKey);
                _language = System.Enum.IsDefined(typeof(InspectorLanguage), savedLanguage)
                    ? savedLanguage
                    : defaultLanguage;
                return _language.Value;
            }
            set
            {
                _language = value;
                EditorPrefs.SetInt(PrefKey, (int)value);
            }
        }

        internal static string Select(string ja, string en, string separator)
        {
            if (Language == InspectorLanguage.Japanese) return ja;
            if (Language == InspectorLanguage.English) return en;
            return ja + separator + en;
        }

        internal static GUIContent Label(string key)
        {
            var cacheKey = key + ":" + (int)Language;
            GUIContent cached;
            if (LabelCache.TryGetValue(cacheKey, out cached)) return cached;

            var entry = Entries[key];
            var tooltip = Select(entry.JaTooltip ?? "", entry.EnTooltip ?? "", "\n").TrimEnd('\n');
            cached = new GUIContent(Line(key), tooltip);
            LabelCache.Add(cacheKey, cached);
            return cached;
        }

        internal static string Line(string key)
        {
            var entry = Entries[key];
            return Select(entry.Ja, entry.En, " / ");
        }

        internal static string Text(string key)
        {
            var entry = Entries[key];
            return Select(entry.Ja, entry.En, "\n");
        }

        internal static bool DrawLanguagePopup(Rect rect)
        {
            EditorGUI.BeginChangeCheck();
            var selected = EditorGUI.Popup(rect, (int)Language, LanguageOptions);
            if (!EditorGUI.EndChangeCheck()) return false;

            Language = (InspectorLanguage)selected;
            return true;
        }
    }
}

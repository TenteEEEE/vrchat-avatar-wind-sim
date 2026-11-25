using System.Collections.Generic;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDKBase;

namespace TenteEEEE.Kazamachi
{
    /// <summary>Build-time settings for a looping avatar-local wind animation.</summary>
    [AddComponentMenu("Kazamachi")]
    [DisallowMultipleComponent]
    public sealed class KazamachiWind : MonoBehaviour, IEditorOnly
    {
        public enum TargetSelection
        {
            AllPhysBones,
            IncludeOnly,
            // Values are serialized; do not reorder.
            Auto
        }

        [Header("Wind")]
        [Range(0f, 360f)]
        [Tooltip("アバター読み込み直後の風向です。0度は前方(+Z)、90度は右(+X)で、時計回りに指定します。\nWind direction when the avatar loads. 0° is forward (+Z), 90° is right (+X); angles increase clockwise.")]
        public float initialDirectionAngle = 90f;

        [Range(0f, 1f)]
        [Tooltip("アバター読み込み直後の風の強さです。0で無風、1で最大風速と最大傾斜角に達します。\nWind strength when the avatar loads. 0 is calm; 1 reaches maximum wind speed and lean angle.")]
        public float initialStrength = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("アバター読み込み直後の揺らぎ量です。0で一定の風、1で最大の方向変化と強弱変化になります。\nTurbulence when the avatar loads. 0 gives steady wind; 1 gives the greatest changes in direction and strength.")]
        public float initialTurbulence = 0.35f;

        [Tooltip("Expression Menuに上下方向の操作を追加します。既定ではOFFです。有効にすると生成クリップ数が17から49に増えます。\nAdds vertical control to the Expression Menu. Off by default. Enabling it increases generated clips from 17 to 49.")]
        public bool verticalControl = false;

        [Range(0f, 1f)]
        [Tooltip("アバター読み込み直後の上下方向です。0で下向き、0.5で水平、1で上向きになります。\nVertical direction when the avatar loads. 0 is down, 0.5 is level, and 1 is up.")]
        public float initialElevation = 0.5f;

        [Range(0f, 80f)]
        [Tooltip("上下操作を端まで動かしたときの最大仰角です。値を大きくすると上下の風が強くなり、吹き上げで髪などが持ち上がりやすくなります。\nMaximum elevation at either end of vertical control. Higher values strengthen vertical wind and can lift hair when blowing upward.")]
        public float maxElevationAngle = 60f;

        [Header("Range")]
        [Range(0f, 45f)]
        [Tooltip("風の揺れの速さの上限です。値を上げると速く揺れますが、実際の上限はサンプルレートとループ秒数でも制限されます。\nMaximum wind motion speed. Higher values allow faster motion, within limits set by sample rate and loop duration.")]
        public float maxWindSpeed = 22.5f;

        [Range(0f, 60f)]
        [Tooltip("強さ1での基準傾斜角です。定常風ではこの値の55%、突風強さ1のピークで設定値になります。揺らぎや突風強さが大きいと超える場合があります。\nBase lean angle at strength 1. Steady wind uses 55% of this value; a gust strength 1 peak reaches it. Turbulence or stronger gusts can exceed it.")]
        public float maxLeanAngle = 45f;

        [Range(0f, 1.5f)]
        [Tooltip("突風ピークの倍率です。1で最大傾斜角相当、1より大きい値では基準傾斜角を超える突風になります。\nGust peak multiplier. 1 reaches the maximum lean angle; values above 1 can exceed the base lean angle.")]
        public float gustStrength = 1f;

        [Min(0f)]
        [Tooltip("強さ1のときに発生する突風の平均回数です。1秒あたりの回数で指定し、0にすると突風がなくなります。\nAverage gusts per second at strength 1. Set to 0 to disable gusts.")]
        public float gustFrequency = 0.3f;

        [Header("Motion")]
        [Range(2f, 30f)]
        [Tooltip("生成するアニメーションのループ時間です。長くすると同じサンプルレートでも速い揺れを表現できますが、アニメーションデータが増えます。\nAnimation loop duration. Longer loops allow faster motion at the same sample rate, but increase animation data.")]
        public float loopDuration = 8f;

        [Range(10, 60)]
        [Tooltip("解析解像度の基準です。高くすると速い揺れをより正確に表現でき、揺れの速さの上限も変わります。実際の解析は4倍の密度で行い、保存キーは自動的に削減されます。\nAnalysis resolution. Higher values represent fast motion more accurately and change the speed limit. Analysis uses 4× density; stored keys are reduced automatically.")]
        public int sampleRate = 30;

        [Tooltip("風の揺れ方を決める整数値です。同じ値と設定なら同じ周期パターンになり、変更すると別の揺れ方になります。\nInteger seed for wind motion. The same seed and settings produce the same loop pattern; changing it creates a different pattern.")]
        public int seed = 1987;

        [Header("Targets")]
        [Tooltip("風を適用するPhysBoneの選択方法です。Autoは全体から名前キーワードを除外し、Include Onlyは指定したものだけを使います。\nHow PhysBones are selected. Auto excludes matching name keywords; Include Only uses only selected PhysBones.")]
        public TargetSelection targetSelection = TargetSelection.Auto;

        [Tooltip("Auto選択時に除外する名前の一部です。PhysBoneのGameObject名またはroot名に一致する項目を除外し、大文字小文字は区別しません。\nName fragments to exclude in Auto mode. Matched against the PhysBone GameObject or root name, case-insensitively.")]
        public List<string> autoExcludeNameKeywords = new List<string>
        {
            "breast", "bust", "oppai", "boob", "mune", "胸"
        };

        [Tooltip("Include Only選択時に風を適用するPhysBoneを指定します。空の場合は風アニメーションを生成しません。\nPhysBones to affect in Include Only mode. No wind animation is generated if this list is empty.")]
        public List<VRCPhysBoneBase> includedPhysBones = new List<VRCPhysBoneBase>();
        [Tooltip("AutoまたはAll PhysBones選択時に、風の対象から外すPhysBoneを指定します（Include Onlyでは使いません）。\nPhysBones to exclude in Auto or All PhysBones mode. Not used in Include Only mode.")]
        public List<VRCPhysBoneBase> excludedPhysBones = new List<VRCPhysBoneBase>();

        [Tooltip("非アクティブなGameObjectや無効なPhysBoneに有効化アニメーションがない場合、対象から除外します。VRCFuryがあるアバターでは判定を無効にします。Include Onlyでは適用しません。\nExcludes inactive GameObjects or disabled PhysBones without an activation animation. Disabled when VRCFury is present; not used in Include Only mode.")]
        public bool skipNeverActivePhysBones = true;

        [Tooltip("rootがHumanoidボーン（Hipsなど）のPhysBoneを、直下の揺れる子ごとに分割して風の対象にします。スカートなどに有効ですが、PhysBoneコンポーネント数が増えるためパフォーマンスランクに影響します。Multi-Child TypeがIgnoreのPhysBoneのみ対象です。\nSplits a PhysBone rooted at a Humanoid bone (such as Hips) into targets for each direct simulated child. Useful for skirts, but adds PhysBone components and may affect performance rank. Only applies to PhysBones with Multi-Child Type set to Ignore.")]
        public bool splitHumanoidRootPhysBones = false;

    }
}

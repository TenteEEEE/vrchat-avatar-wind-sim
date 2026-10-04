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
            // Appended so existing serialized values keep their meaning.
            Auto
        }

        [Header("Wind")]
        [Tooltip("導入直後に風を有効にするかを設定します。無効にすると、Expression Menuから有効にするまで風は停止します。")]
        public bool startEnabled = false;

        [Tooltip("有効にすると風の操作を他のユーザーへ同期します。上下操作OFFで25 bit、ONで33 bitを使います。同期予算を超える場合はこの設定だけローカル動作になります。無効時の同期コストは0 bitです。")]
        public bool syncToOthers = false;

        [Range(0f, 360f)]
        [Tooltip("Expression Menuを開いた直後の風向です。0度は前方(+Z)、90度は右(+X)で、時計回りに指定します。")]
        public float initialDirectionAngle = 90f;

        [Range(0f, 1f)]
        [Tooltip("Expression Menuを開いた直後の風の強さです。0で無風、1で最大風速と最大傾斜角に達します。")]
        public float initialStrength = 0.35f;

        [Range(0f, 1f)]
        [Tooltip("Expression Menuを開いた直後の揺らぎ量です。0で一定の風、1で最大の方向変化と強弱変化になります。")]
        public float initialTurbulence = 0.35f;

        [Tooltip("Expression Menuに上下方向の操作を追加します。有効にすると生成クリップ数が17から49に増え、同期時は追加で8 bitを使います。")]
        public bool verticalControl = true;

        [Range(0f, 1f)]
        [Tooltip("Expression Menuを開いた直後の上下方向です。0で下向き、0.5で水平、1で上向きになります。")]
        public float initialElevation = 0.5f;

        [Range(0f, 80f)]
        [Tooltip("上下操作を端まで動かしたときの最大仰角です。値を大きくすると上下の風が強くなり、吹き上げで髪などが持ち上がりやすくなります。")]
        public float maxElevationAngle = 60f;

        [Header("Range")]
        [Range(0f, 45f)]
        [Tooltip("風の揺れの速さの上限です。値を上げると速く揺れますが、実際の上限はサンプルレートとループ秒数でも制限されます。")]
        public float maxWindSpeed = 22.5f;

        [Range(0f, 60f)]
        [Tooltip("強さ1での基準傾斜角です。定常風ではこの値の55%、突風強さ1のピークで設定値になります。揺らぎや突風強さが大きいと超える場合があります。")]
        public float maxLeanAngle = 45f;

        [Range(0f, 1.5f)]
        [Tooltip("突風ピークの倍率です。1で最大傾斜角相当、1より大きい値では基準傾斜角を超える突風になります。")]
        public float gustStrength = 1f;

        [Min(0f)]
        [Tooltip("強さ1のときに発生する突風の平均回数です。1秒あたりの回数で指定し、0にすると突風がなくなります。")]
        public float gustFrequency = 0.3f;

        [Header("Motion")]
        [Range(2f, 30f)]
        [Tooltip("生成するアニメーションのループ時間です。長くすると同じサンプルレートでも速い揺れを表現できますが、アニメーションデータが増えます。")]
        public float loopDuration = 8f;

        [Range(10, 60)]
        [Tooltip("1秒あたりのアニメーションサンプル数です。高くすると速い揺れを表現できますが、生成データが増えます。")]
        public int sampleRate = 30;

        [Tooltip("風の揺れ方を決める整数値です。同じ値と設定なら同じ周期パターンになり、変更すると別の揺れ方になります。")]
        public int seed = 1987;

        [Header("Targets")]
        // Keep the enum values stable: existing Include Only components remain Include Only.
        // Newly added components intentionally start with the convenient broad selection.
        [Tooltip("風を適用するPhysBoneの選択方法です。Autoは全体から名前キーワードを除外し、Include Onlyは指定したものだけを使います。")]
        public TargetSelection targetSelection = TargetSelection.Auto;

        [Tooltip("Auto選択時に除外する名前の一部です。PhysBoneのGameObject名またはroot名に一致する項目を除外し、大文字小文字は区別しません。")]
        public List<string> autoExcludeNameKeywords = new List<string>
        {
            "breast", "bust", "oppai", "boob", "mune", "胸"
        };

        [Tooltip("Include Only選択時に風を適用するPhysBoneを指定します。空の場合は風アニメーションを生成しません。")]
        public List<VRCPhysBoneBase> includedPhysBones = new List<VRCPhysBoneBase>();
        [Tooltip("選択モードにかかわらず風の対象から外すPhysBoneを指定します。")]
        public List<VRCPhysBoneBase> excludedPhysBones = new List<VRCPhysBoneBase>();

        [Tooltip("有効にすると既存アニメーションが回転を制御するPhysBone rootを除外し、アニメーション同士の競合を避けます。")]
        public bool skipRootsWithAnimation = true;

        /// <summary>Returns the requested base cycles after the anti-aliasing cap.</summary>
        public static int ComputeBaseCycles(
            float maxWindSpeed, int sampleRate, float duration, out int requestedCycles)
        {
            requestedCycles = Mathf.Max(1, Mathf.RoundToInt(duration *
                (0.12f + 0.045f * Mathf.Max(0f, maxWindSpeed))));
            var maxBaseCycles = Mathf.Max(1, Mathf.FloorToInt(sampleRate * duration / 16f));
            return Mathf.Min(requestedCycles, maxBaseCycles);
        }
    }
}

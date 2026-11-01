# Kazamachi（風待ち）

VRChatアバターの既存PhysBoneに、撮影向けの「風」を加えるNDMFプラグインです。
ビルド時にPhysBone rootの親にpivotを挿入して揺らすループアニメーションをFX Animatorへ生成し、Expression Menuから風向・強さ・揺らぎを操作できます。
カスタムスクリプトはアップロードされません。風は常にOFFで開始します。メニュー操作は同期され、他のプレイヤーにも風が見えます。

## 導入

1. VCCに `https://tenteeeee.github.io/vpm-repos/index.json` を追加し、`Kazamachi` をインストールします。
2. Hierarchyでアバターのルートを選択し、`GameObject > Kazamachi` を実行します。
3. Inspectorで設定し、通常どおりNDMF経由でビルドまたはアップロードします。

Inspectorの項目名は英語です。マウスオーバーで日本語の説明が表示されます。

## Expression Menu

`Kazamachi` サブメニューにアイコン付きで次の項目が追加されます。

| 項目 | 種類 | 内容 |
|---|---|---|
| `Wind` | Toggle | 風のON/OFF。切り替え時は0.5秒でフェードします |
| `Direction` | Radial | 風向。0=前(+Z)から時計回り。0.25=右、0.5=後、0.75=左、1.0=前 |
| `Elevation` | Radial | 上下方向（`Vertical Control` ONのときのみ）。0=吹き下ろし、0.5=水平、1=吹き上げ |
| `Strength` | Radial | 強さ。0=無風、1=Inspectorの最大値。揺れの振幅と速さが同時に変わります |
| `Turbulence` | Radial | 揺らぎ。0=一定の風、1=方向と強弱が最も乱れる |

風向の0と1はどちらも正面です。継ぎ目を正面に置くことで、正面と側面の間を滑らかに選べます。

## 主な設定

| Inspector | 既定値 | 説明 |
|---|---|---|
| Initial Direction / Strength / Turbulence | 90° / 0.35 / 0.35 | 各ラジアルの初期値 |
| Vertical Control | OFF | 上下ラジアルを追加します。生成クリップ数は17→49に増えます |
| Skip Hidden PhysBones | ON | 有効化アニメーションのない非表示GameObjectや無効なPhysBoneを除外します。VRCFuryがある場合は判定を無効にします（Include Onlyには適用しません） |
| Max Elevation Angle | 60° | 上下ラジアル端での風の仰角 |
| Max Wind Speed (m/s) | 22.5（0〜45） | 強さ1のときの揺れの速さ |
| Max Lean Angle (deg) | 45（0〜60） | 強さ1のときの基準傾斜角 |
| Gust Strength | 1（0〜1.5） | 突風ピークの倍率 |
| Gust Frequency (/s) | 0.3 | 強さ1のときの突風回数 |
| Advanced: Loop Duration / Sample Rate / Seed | 8秒 / 30 / 1987 | ループ長、解析解像度の基準、揺れ方のパターン。Sample Rateを上げると速い揺れをより正確に表現できます。実際の解析は4倍の密度で行い、保存キーは自動的に削減されます |

傾きの構成は「定常風55% + 突風45% × Gust Strength」です。
Gust Strength 1の突風ピークで、ちょうどMax Lean Angleに達します。
揺らぎやGust Strength 1超えの突風では、Max Lean Angleを一時的に超えることがあります（上限80°）。

Max Wind Speedを上げても、Sample RateとLoop Durationで決まる上限より速くはなりません。
上限に当たっているときはInspectorに警告が出ます。

## 対象PhysBoneの選び方

- **Auto（既定）**: 全PhysBoneが対象です。ただし、GameObject名またはroot名に `Auto Exclude Keywords`（既定: breast / bust / oppai / boob / mune / 胸）を含むものは除外します。
  親階層は見ないので、胸ボーン配下のアクセサリーは対象に残ります。除外結果はビルド時にConsoleへ出力されます。
- **All PhysBones**: 全PhysBoneが対象です。`Excluded PhysBones` に入れたものだけを外します。
- **Include Only**: `Included PhysBones` に指定したものだけを動かします。

どのモードでも、次のrootは安全のため自動で除外します。
- Humanoidボーン本体とアバタールート
- 入れ子になって二重に風が掛かるroot
- 別のPhysBoneのチェーンの途中にあるroot（pivotを挿入するとそのチェーンの物理が変わるため）

別のPhysBoneの `Ignore Transforms` に指定されたrootは対象に残し、ビルド時に挿入したpivotも同じ `Ignore Transforms` に追加します。元のアバターは変更しません。

同じrootを使う複数のPhysBoneは、1つにまとめて処理します。

## 仕組みと制約

- PhysBoneに外力を与える機能ではありません。rootの直上に挿入したpivotをループアニメーションで動かし、PhysBoneの慣性・Spring・Pullが反応します。PhysBoneの `Is Animated` は変更せず、root自体の既存アニメーションとも競合しません。
- 風向はPhysBone rootの親ボーンの向きを基準に焼き込むため、頭や体を回すと風向も一緒に回ります（アバター基準で固定されません）。
- rootの子が四方に広がり方向が打ち消し合う場合は、PhysBoneのendpoint方向、未設定ならアバターの下方向を基準にします。各毛束を `Include Only` で指定することも検討してください。
- 全クリップを「強さ1の暴風」で生成します。実行時はStrengthに応じて振幅と再生速度を落とします。
- Write Defaultsは、アバターのFXレイヤーがすべてWD ONならON、それ以外（OFFのみ・混在・判定対象なし）はOFFにします。AdditiveレイヤーとDirect Blend Treeだけのレイヤーは判定から除きます。
- パラメータは `TenteEEEE/Kazamachi/{Enabled,Direction,Strength,Turbulence,Elevation}` で、いずれも `saved=false`・同期ありです。複数のコンポーネントが生成される場合は、レイヤー、メニュー、パラメータに対応する番号が付きます。
- 同期ビット数はコンポーネント1個あたり33bit（Elevationなしは25bit）です。上限を超える場合はその風の生成をスキップし、NDMFのレポートに必要量と空き容量を表示します。
- Pullが強い、Immobileが高い、角度制限が小さい、といったPhysBoneでは風が弱く見えます。
- 生成データが重い場合は、`Vertical Control` をOFFにするか、Loop Durationを下げてください。キーは誤差0.5°以内に収まるよう自動で間引かれます。ビルド後のNDMFの表示に、風を追加したroot数、除外数、アニメーションデータの目安サイズ（非圧縮）が出ます。

## 推奨の確認手順

まずStrengthを0.3前後にして動きを確認します。次に0.6で風の存在感を、1.0で最大傾斜と突風を確認します。
衣装やPhysBone設定に合わせて、Max Lean Angleと対象リストを調整してください。

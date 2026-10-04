# Kazamachi（風待ち）

VRChatアバターの既存PhysBoneに、撮影向けの「風」を加えるNDMFプラグインです。
ビルド時にPhysBone rootを揺らすループアニメーションをFX Animatorへ生成し、Expression Menuから風向・強さ・揺らぎを操作できます。
カスタムスクリプトはアップロードされません。

## 導入

1. VCCに `https://tenteeeee.github.io/vpm-repos/index.json` を追加し、`Kazamachi` をインストールします。
2. Hierarchyでアバターのルートを選択し、`GameObject > Kazamachi` を実行します。
3. Inspectorで設定し、通常どおりNDMF経由でビルドまたはアップロードします。

Inspectorの項目名は英語です。マウスオーバーで日本語の説明が表示されます。

## Expression Menu

`Kazamachi` サブメニューに次の項目が追加されます。

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
| Start Enabled | OFF | 導入直後の風のON/OFF |
| Sync To Others | OFF | ONで他人にも風が見えます。同期コストは25 bit（上下OFF）または33 bit（上下ON） |
| Initial Direction / Strength / Turbulence | 90° / 0.35 / 0.35 | 各ラジアルの初期値 |
| Vertical Control | ON | 上下ラジアルを追加します。生成クリップ数は17→49に増えます |
| Max Elevation Angle | 60° | 上下ラジアル端での風の仰角 |
| Max Wind Speed (m/s) | 22.5（0〜45） | 強さ1のときの揺れの速さ |
| Max Lean Angle (deg) | 45（0〜60） | 強さ1のときの基準傾斜角 |
| Gust Strength | 1（0〜1.5） | 突風ピークの倍率 |
| Gust Frequency (/s) | 0.3 | 強さ1のときの突風回数 |
| Advanced: Loop Duration / Sample Rate / Seed | 8秒 / 30 / 1987 | ループ長、サンプル数、揺れ方のパターン |

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
- 既存アニメーションが回転を動かしているroot（`Skip Animated Roots` ONのとき）

同じrootを使う複数のPhysBoneは、1つにまとめて処理します。

## 仕組みと制約

- PhysBoneに外力を与える機能ではありません。PhysBone rootの回転をループアニメーションで動かし、`isAnimated` を有効にします。PhysBoneの慣性・Spring・Pullがその動きに反応します。
- 全クリップを「強さ1の暴風」で生成します。実行時はStrengthに応じて振幅と再生速度を落とします。
- Write Defaultsは、アバターのFXレイヤーの多数派に合わせます。
- パラメータは `TenteEEEE/Kazamachi/{Enabled,Direction,Strength,Turbulence,Elevation}` で、いずれも `saved=false` です。
- Sync To Others ONで同期パラメータの上限を超える場合は、NDMFの警告を出し、その設定だけをローカル専用にします。
- Pullが強い、Immobileが高い、角度制限が小さい、といったPhysBoneでは風が弱く見えます。
- 生成データが重い場合は、`Vertical Control` をOFFにするか、Sample RateまたはLoop Durationを下げてください。ビルドログに生成クリップ数と合計キー数が表示されます。

## 推奨の確認手順

まずStrengthを0.3前後にして動きを確認します。次に0.6で風の存在感を、1.0で最大傾斜と突風を確認します。
衣装やPhysBone設定に合わせて、Max Lean Angleと対象リストを調整してください。

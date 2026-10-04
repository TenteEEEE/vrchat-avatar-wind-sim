# Kazamachi 概要

VRChatアバターのPhysBoneに風の揺れを加えるツールです。NDMFによるアバターのビルド時にループアニメーションを生成し、Expression Menuから風を操作できます。

## 導入

VPMリポジトリ `https://tenteeeee.github.io/vpm-repos/index.json` を追加して **Kazamachi** をインストールします。アバターのルートを選択し、`GameObject > Kazamachi` からコンポーネントを追加してください。Inspectorで対象と風の設定を行い、NDMF経由でビルドします。

Expression Menuのサブメニュー名は `Kazamachi` です。`Wind`で風のON/OFF、`Direction`で風向、`Strength`で強さ、`Turbulence`で揺らぎを操作します。上下方向を有効にすると`Elevation`も表示されます。

Unity 2022.3、VRChat Avatars SDK 3.10.5以降、NDMF 1.14.8以降が必要です。詳しい設定は[パッケージ説明書](Packages/com.tentee.vrc-kazamachi/README.md)を参照してください。

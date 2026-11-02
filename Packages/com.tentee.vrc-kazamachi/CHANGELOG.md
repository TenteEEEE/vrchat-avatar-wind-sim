# Changelog

## 1.0.0 - 2026-11-02

Kazamachi（風待ち）の初回正式リリースです。0.3.3の風の動きと設定を引き継ぎ、正式版としてバージョン表記と配布情報を整えました。0.3.3からの設定変更は不要です。

### 主な機能

- VRChatアバターの既存PhysBoneに、撮影向けのそよ風から暴風までの揺れを追加します。
- Expression Menuから風のON/OFF・風向・強さ・揺らぎを操作できます。上下方向の操作も任意で追加できます。
- 風はOFFで開始し、メニュー操作は他のプレイヤーにも同期されます。同期パラメータは1コンポーネントあたり25bit、上下方向ありで33bitです。
- NDMFのビルド時にPhysBone rootの親へpivotとループアニメーションを生成します。元のアバターやPhysBoneのIs Animatedを変更せず、カスタムスクリプトもアップロードしません。
- 対象の自動選択・個別指定・除外、アニメーションキーの削減、パラメータ容量の確認に対応します。

### 導入・動作環境

- VCCへ `https://tenteeeee.github.io/vpm-repos/index.json` を追加し、**Kazamachi** をインストールしてください。
- Unity 2022.3、VRChat Avatars SDK 3.10.5以降、NDMF 1.14.8以降が必要です。
- アバターのルートを選択して `GameObject > Kazamachi` を実行します。詳しい設定と制約は[日本語説明書](https://github.com/TenteEEEE/vrchat-avatar-wind-sim/blob/v1.0.0/Packages/com.tentee.vrc-kazamachi/README.md)を参照してください。

### English

First stable release of Kazamachi, a photo-friendly wind system for VRChat avatar PhysBones. Includes synced Expression Menu controls, optional elevation, non-destructive NDMF build-time generation, target filtering, and reduced animation data. Wind behavior and settings are unchanged from 0.3.3; no migration is required.

---

Versions below are pre-1.0 development history.

## 0.3.3

- Successful builds no longer open the NDMF Console or leave warnings.
- Replace per-item logs with one summary log.
- Report a missing FX Animator through NDMF.
- Turn off Loop Pose on generated clips: same motion, about 2.6x cheaper Animator evaluation in a 136-root measurement.

## 0.3.2

- Fix: a root listed in another PhysBone's Ignore Transforms got no wind and changed that chain; the inserted pivot is now added to the same Ignore Transforms.
- Internal cleanup: removed the unused root-rotation bake path; no change to generated animations.
- Store pivot rotations as Euler angles (three curves instead of four) and use a 0.5° key tolerance. Together these reduce animation data by about 35% at default settings (8.0 to 5.2 MB LZMA in a 136-root measurement). No settings changes or migration are required.

## 0.3.1

- Animate an inserted pivot above each PhysBone root, preserving existing root animations without changing Is Animated. Removed Skip Animated Roots.
- Fall back to the endpoint or avatar down when child directions are spread out.
- Exclude roots that sit inside another PhysBone's chain (a pivot there would change that chain).
- Skip winds that exceed the synced expression parameter budget.
- Report build warnings, exclusions, and a bake summary (roots and estimated animation data size) through NDMF.

## 0.3.0

- Reduce stored animation keys with an error-bounded curve fit (0.25° at analysis samples; about a quarter of 0.2.0 at default settings).
- Disable Vertical Control by default.
- Skip hidden PhysBones that cannot be activated; VRCFury disables this check.
- Sample Rate controls analysis resolution and the flutter speed cap; stored keys are reduced automatically.
- Show stored key counts and the 0.2.0-equivalent count in the build log.
- Number layers, menus, parameters, clips and trees consistently for multiple generating components.
- Correct the Write Defaults description in the manual (WD On only when every counted FX state is WD On).

## 0.2.0

- **Breaking:** Remove Sync To Others and Start Enabled. Wind always starts OFF and menu operations are always synced to other players (33 bits per component, 25 without Elevation).
- Add icons to the Expression Menu submenu and all five controls.

## 0.1.1

- Fix the bundled Japanese manual (README.md), which was garbled in 0.1.0.
- Clarify the Japanese tooltips for initial values and Excluded PhysBones.

## 0.1.0

- Add looping wind animation controls for VRChat avatar PhysBones.
- Add runtime direction, strength, turbulence, and optional elevation controls.
- Generate Expression Menu controls and animation assets during avatar builds.

# Changelog

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

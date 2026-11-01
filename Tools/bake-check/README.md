# Bake check

Copy `Packages/com.tentee.vrc-kazamachi/Editor/Baking/*.cs` and all C# files in this directory into a blank Unity 2022.3 project under one Editor-only asmdef with no assembly references.

Run Unity with `-batchmode -executeMethod BakeCheck.Run`. Add `-bakeCheckOut <path>` to choose the JSON report location; the default is `_work/bake-report.json` relative to the project. The report and console table sweep 0.25, 0.5 and 1.0 degrees for quaternion and Euler pivot formats and each turbulence setting. Keys are compared with legacy counts; bake time is summed per row. Quaternion rows now bake pivot curves; their key counts can shift with floating-point noise compared with the old root-rotation curves. Exit code 1 indicates a sampler regression, pivot component delta above 1e-6, Euler analysis-grid error above tolerance plus 0.001 degrees, Euler seam mismatch above 1e-4, or another failure.

Run `-batchmode -executeMethod SizeProbe.Run` in a disposable project to build the 136-root, 17-clip variants and write `size-probe-report.txt`. It reports stored keys, analytic and low-pass error, uncompressed and LZMA StandaloneWin64 AssetBundle size, bake time, and Animator differences between formats. It creates and replaces `Assets/SizeProbe` and `ProbeOut`.

Run `-batchmode -executeMethod DumpCheck.Run` to dump every key's raw float bits for the plugin's Euler pivot bakes. Add `-dumpOut <path>` to choose the output path; the default is `dump.txt`. Compare dumps byte for byte across versions. Exit code 1 indicates a failure.

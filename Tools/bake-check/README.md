# Bake check

Copy `Packages/com.tentee.vrc-kazamachi/Editor/Baking/*.cs` and both C# files in this directory into a blank Unity 2022.3 project under one Editor-only asmdef with no assembly references. Run Unity with `-batchmode -executeMethod BakeCheck.Run`. Add `-bakeCheckOut <path>` to choose the JSON report location; the default is `_work/bake-report.json` relative to the project. The exit code is 1 if the sampler regression exceeds 1e-6 or the harness fails.

The report and console table sweep 0.1, 0.25 and 0.5 degrees separately for each turbulence setting. Bake time is the sum of new-reducer calls for each row; legacy key counts use that configuration’s sample count.

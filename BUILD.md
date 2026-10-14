# Build the SpacePhone 3.2 phone-only RC1

Read README.md and SOURCE-MANIFEST.json for the frozen production source identity. RC1 is compiled with zero warnings/errors. Final native gameplay validation was stopped at the author's request after the last recorder trade correction; no complete native acceptance is claimed.

## Inputs

- Windows x64, a legitimate installed Probably Stolen Demo and compatible BepInEx IL2CPP environment.
- The original build used .NET SDK 6.0.428 and targets net6.0. BepInEx/runtime versions in the release notes describe the tested environment; they do not imply every other loader build is compatible.
- From the reviewer's own installation: BepInEx/core/BepInEx.Core.dll, BepInEx.Unity.IL2CPP.dll, 0Harmony.dll, Il2CppInterop.Runtime.dll, and BepInEx/interop/*.dll. These references are not included in this source snapshot and must not be published with the mod.

The official [BepInEx IL2CPP guide](https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html) describes the correct Windows IL2CPP x64 loader and first-run interop generation. Install it by its own instructions before resolving build references. Do not change security settings or bypass account/DRM restrictions to make a build work.

## Build

Open a normal developer terminal at the repository root after obtaining the legitimate references:

    dotnet --version
    dotnet build plugin/SpacePhone.csproj -c Release -p:GameRoot="C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen Demo"

Replace GameRoot with the reviewer's actual legitimate installation. The project uses local assembly references, no production PackageReference packages, and Private=false to avoid redistributing generated/native game dependencies.

Output: plugin/bin/Release/net6.0/SpacePhone.dll. A normal manual mod layout uses that DLL and the 21 included Assets/ PNGs in BepInEx/plugins/SpacePhone/Assets/. Art files are described in ASSET-SOURCES.txt. No browser host, game runtime or original fonts are produced by this project.

## Source/binary correspondence and path mapping

Every plugin/<name> maps directly to the frozen phone-only candidate's Source/<name>. There are exactly 35 source/project files. This snapshot preserves their bytes and SHA256 rather than copying the shared work tree with another Mod's incomplete dependency.

The candidate reference DLL is SHA256 4bb0b5c07f3bfa8e3d96d84a16f91189765b9999cf4fec995dea74be08d821de. The DLL is not included in this source tree. Its 35 source hashes match this snapshot. Actual release validation is recorded with the matching Nexus package; these compilation instructions alone do not establish native acceptance.

A rebuild with a different SDK, generated interop set, compiler settings, source path or PDB/debug path may have a different binary SHA even when the source is the same. These instructions do not claim a byte-identical build. Optional MSBuild PathMap can map the reviewer's plugin/ source root to a stable review root, but changing it also creates a new build identity. The accepted release identity must remain bound to its actual tests; never rename or reuse the old 3.1 hash as proof of 3.2 correspondence.

The local test-only Integration, save/ES3/native-log barriers, GUI launcher and private runs are not required to build the production plugin and are not part of the distributed DLL. A zero-warning compilation is not native gameplay acceptance.

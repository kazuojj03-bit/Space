# Build and review steps

These are developer/reviewer instructions. Players use the packaged **SpacePhone安装器.exe**. A rebuilt binary is not automatically byte-identical to a historical release.

## Requirements

- Windows x64 and a legitimate Steam **Probably Stolen Demo** installation.
- BepInEx 6 Unity IL2CPP x64; the original verified environment used be.788.
- For the plugin: a .NET 6 SDK compatible with the net6.0 project, or a suitable later SDK with the required targeting pack.
- For BrowserHost: .NET SDK 10.x on Windows and access to the exact NuGet dependency Microsoft.Web.WebView2 **1.0.4258.31**.
- For the newer Windows Forms installer: Windows .NET Framework C# compiler and desktop references.
- Use isolated fixtures or a save-blocked game copy for tests. Do not test installation or automatic gameplay against a personal save.

## Prepare local plugin references

Install the correct BepInEx into a legitimate game copy and run it once to generate the required BepInEx/interop references. This first IL2CPP launch may be slow and may fetch Unity base libraries when they are not cached. The source review package intentionally does not redistribute game assemblies or generated interop files.

The game path supplied to MSBuild must contain the required BepInEx/core and BepInEx/interop references. It is a build input, not a request to bundle those references into the release.

Official guide: https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html

## Build the plugin

From the source repository root in a developer shell:

    dotnet build .\plugin\SpacePhone.csproj -c Release -p:GameRoot="C:\Path\To\Probably Stolen Demo"

The original project targets **net6.0**, references BepInEx.Core, BepInEx.Unity.IL2CPP, 0Harmony, Il2CppInterop.Runtime and locally generated interop DLLs, with Private=false for these references.

The normal output is plugin/bin/Release/net6.0/SpacePhone.dll. Compute its SHA256 and record the SDK, reference versions, source revision and source differences. Do not assume a matching 3.1 AssemblyVersion proves matching bytes.

## Publish BrowserHost

    dotnet restore .\webhost\WebViewHost.csproj
    dotnet publish .\webhost\WebViewHost.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\BrowserHost

The original host project targets **net10.0-windows**, Windows Forms, PublishSingleFile=false and WebView2 1.0.4258.31. Keep the complete publish directory for runtime use, including the relevant WebView2 native loader. A self-contained .NET output may include runtime tools such as createdump.exe; identify them from the runtime's own distribution and notices.

Microsoft WebView2 Runtime is still required on the target system. It is separate from the host's SDK DLLs and separate from the .NET runtime. A standalone runtime installer can be carried with the one-click bundle; detect an existing runtime and install only if missing.

Official distribution guide: https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution

## Legacy installer generation

This generation contains readable CMD and PowerShell/WPF source and has no C# GUI EXE build. Keep Installer.Core.ps1 and SpacePhone-Installer.ps1 together with their manifest and payload when making a runtime package.

Start-Installer.cmd uses Windows PowerShell 5.1, STA and process-level ExecutionPolicy Bypass. Save Chinese PowerShell sources as UTF-8 with BOM for Windows PowerShell 5.1. Do not call the legacy CMD a standalone executable.

The legacy fixture suites use synthetic game files and explicit test markers, checking payload hashes, backups, rollback, path boundaries and preservation. Reconstruct a valid fixture package from source-controlled manifests and approved release artifacts before running tests; source-only directories are not complete runtime packages.

## New one-click installer generation

The new frontend is a real compiled Windows PE GUI. The frontend sources under installer/oneclick must be compiled with their actual project/build script or with Windows .NET Framework csc.exe and the references used in source.

Confirmed system compiler location on the original build machine:

    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe

Available desktop references include System.Windows.Forms.dll, System.Drawing.dll, System.Web.Extensions.dll, System.IO.Compression.dll and System.IO.Compression.FileSystem.dll as needed by the final frontend source. Use target:winexe, an STA entry point and the frontend's required manifest/resources. The player-facing output name is **SpacePhone安装器.exe**.

The backend sources OneClick.ps1, OneClick.Core.ps1 and Legacy.Helpers.ps1 are distinct from the earlier installer. The current backend fixture mode is constrained to an isolated test-artifacts tree with a marker; failure injection is not allowed against the real game.

A full runtime bundle must also contain:
- validated mod manifest/payload, including the complete BrowserHost;
- validated BepInEx be.788 loader payload;
- Microsoft WebView2 Evergreen Standalone Installer from the official distribution, with verified publisher and redistribution notices;
- frontend/backend build/version records and complete SHA256 manifests.

Do not substitute an online bootstrapper for the promised bundled standalone runtime installer. Do not include generated game interop assemblies or game data to conceal first-launch dependencies.

## Package and test

Compute archive and executable identities independently after final assembly. Ensure no private saves, platform profiles, personal music, test plugins or extracted original assets are included.

Keep the legacy quarantined archive identity separate from any newly rebuilt plugin, host or installer. Changes to the package or scanner verdict must be recorded as actual results, not implied from successful compilation.

For installation tests, cover an existing compatible loader, a missing loader, wrong/mixed loaders, running game/host, denied permissions, tampered manifests, incomplete extraction, symlink/reparse targets, partial writes, backup failure and rollback. Check protected files before and after.

Do not send a review request, create a public source repository or attach private files merely by running the build. External publication is a separate authorized action.

## Exact one-click frontend command

Run in Windows PowerShell 5.1:

    powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\oneclick\Build-Setup.ps1 -PackageRoot "D:\SpacePhoneBuild"

This builds the readable C# frontend with native csc.exe, x64 /target:winexe, no obfuscation, no packed archive. It copies the three backend scripts and embeds their hashes. Put the approved package/mod + package/loader, loader-manifest.json and official WebView2 installer beside it to make a usable runtime distribution. The source-only output is deliberately not a bundled game.

## Fixtures

Run engine/Test-OneClick.ps1 against an explicitly prepared bundle using its -PackageRoot parameter. This creates only marked PE-header dummy-game directories under installer/oneclick/test-artifacts. Never execute those dummy game files. GUI --selftest <JSON path> renders without activating a visible window. --check-report <JSON path> --game <folder> performs a read-only preflight. Neither switches install components.

## Historical-source mapping

The historical plugin has one developer-path-specific browser test hook, removed in the public copy. The exact public change is documented in docs/BrowserClient-public-source.diff; its private path is redacted in the diff. Runtime SpacePhone.dll remains the previously validated binary. This reviewer source is not asserted to reproduce its byte hash.


## Repository review checklist (2026-10-13 follow-up)

The README build link now refers to this actual root `BUILD.md`. Historical source differences are at `docs/HISTORICAL-SOURCE.md`, `docs/PLUGIN-SOURCE-DIFFERENCES.txt` and `docs/BrowserClient-public-source.diff`.

### Exact frontend output and source-only build

From the repository root in Windows PowerShell 5.1, use a NEW local output directory:

```powershell
$reviewOutput = Join-Path $PWD 'artifacts\oneclick'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\oneclick\Build-Setup.ps1 -PackageRoot $reviewOutput
```

Outputs: `artifacts/oneclick/SpacePhone安装器.exe`, generated `Integrity.build.cs` and `engine/{OneClick.ps1,OneClick.Core.ps1,Legacy.Helpers.ps1}`. The script invokes native .NET Framework `csc.exe`, x64 Windows GUI target, source icon/manifest, and embeds the actual backend-script hashes. It does not invoke or install that EXE.

For plugin and host, use the build/publish commands above. Their outputs are separate: `plugin/bin/Release/net6.0/SpacePhone.dll`, and `artifacts/BrowserHost/` (complete self-contained `win-x64` host directory). Do NOT copy game-reference DLLs to these output packages. NuGet restore requires access to the official exact dependency version; build environments must have the required SDK/targeting packs.

### A reviewer compile is not a complete player installer bundle

To assemble a player-ready OneClick bundle, obtain the EXACT reviewed release components rather than inventing an unchecked manifest:

```text
SpacePhone安装器.exe
engine/OneClick.ps1, OneClick.Core.ps1, Legacy.Helpers.ps1
package/mod/manifest.json
package/mod/payload/BepInEx/plugins/SpacePhone/SpacePhone.dll
package/mod/payload/BepInEx/plugins/SpacePhone/Assets/ (all 21 icons)
package/mod/payload/BepInEx/plugins/SpacePhone/BrowserHost/ (complete published host)
package/loader/BepInEx/core/, package/loader/dotnet/ and official loader root files
loader-manifest.json
package/runtime/MicrosoftEdgeWebView2RuntimeInstallerX64.exe
licenses/ (third-party notices and corresponding expanded LGPL source)
licenses/许可说明.html (from installer/oneclick/packaging/)
三步安装.html (from installer/oneclick/packaging/)
```

The actual player bundle's directory structure and manifest schemas must match the backend source. This list is for review, not a blind copy/installation command. Check `engine/OneClick.Core.ps1` for exact runtime location and required manifests before staging; use the approved manifest identities from the actual release, not a fresh internet artifact silently renamed to match an old hash. This repository is source-only and cannot fabricate a byte-identical full release from changing downloads.

After creating a NEW staging folder (never the real game folder), copy the independently rebuilt plugin DLL to the plugin path above, copy all 21 `assets/*.png` to `Assets/`, and copy the entire host publish output to `BrowserHost/`. Copy this repository's `third-party/` contents to the bundle's `licenses/`, then copy `installer/oneclick/packaging/许可说明.html` to `licenses/许可说明.html` and `installer/oneclick/packaging/三步安装.html` to bundle root. Verify all local HTML license links resolve; these locations are hard-coded in the player-facing frontend.

Generate the Mod manifest from that NEW staged payload in PowerShell, NOT from game files. For example, after assigning `$reviewBundle` to your new artifacts folder:

```powershell
$payloadRoot = (Resolve-Path -LiteralPath (Join-Path $reviewBundle 'package/mod/payload')).Path.TrimEnd('\')
$manifestEntries = @(Get-ChildItem -LiteralPath $payloadRoot -Recurse -File | ForEach-Object {
    [ordered]@{
        path = $_.FullName.Substring($payloadRoot.Length + 1).Replace('\','/')
        sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size = [long]$_.Length
    }
})
$modManifest = [ordered]@{schemaVersion=1;modId='SpacePhone';version='3.1-review-rebuild';files=$manifestEntries}
[IO.File]::WriteAllText((Join-Path $reviewBundle 'package/mod/manifest.json'),($modManifest | ConvertTo-Json -Depth 8),(New-Object Text.UTF8Encoding($false)))
```

The `path` is relative to a game root, while the actual file lives at `package/mod/payload/<path>`. The installer accepts only `BepInEx/plugins/SpacePhone/SpacePhone.dll`, `Assets/`, `BrowserHost/`, and its documented six top-level documentation names. Do not place game/interop files or unrelated output into this payload. A rebuilt plugin/host requires newly computed hashes; do not reuse the old Mod manifest.

For the loader, obtain the pinned official be.788 ZIP listed in `docs/DEPENDENCY-AUDIT.md` and verify its full ZIP SHA256 `f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a`. Extract its approved runtime file tree to `package/loader/`, with no game interop or other plugins. Copy `docs/loader-verified-manifest.json` to bundle-root `loader-manifest.json`, then verify every listed `path/size/sha256` against `package/loader/<path>`. The loader manifest requires `schemaVersion=1`, `loaderId=BepInEx-Unity-IL2CPP-x64`, nonempty `version`, and the file records. `Read-OCPackage` performs these checks without installing anything.

Run the existing isolated fixture suite only AFTER obtaining all of these complete, validated inputs:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\installer\oneclick\engine\Test-OneClick.ps1 -PackageRoot $reviewBundle
```

Save the compile environment, changed source revision, generated manifests and resulting archive hashes. The exact historical release and a reviewer rebuild remain separately identified.

The one-click source pins historical WebView2 Standalone bytes to SHA256 `2a6add76c37bfa872eb8c2b22d45b3216b8e2a1f193e0774006340ff62ac5fa0`, 214,090,448 bytes. Microsoft's evergreen download can change. A freshly downloaded Microsoft-signed installer may therefore be rejected correctly by this historical source. Do not disable integrity checks to make it pass. A replacement requires a NEW reviewed source revision updating the hash in `Build-Setup.ps1`/`Integrity.cs`, verified official provenance, manifests and install tests; do not claim it is the historical 3.1 package.

### Legacy source and fixtures

Legacy `installer/legacy/Start-Installer.cmd` launches its PS5.1 WPF frontend. Its runtime manifest/payload are separate release inputs. Review the launcher and scripts directly; do not run them against a personal installation as part of code review.

`installer/oneclick/engine/Test-OneClick.ps1` requires an explicitly prepared, validated complete bundle and creates ONLY marked dummy-game test fixtures. Plugin rules/transaction tests similarly require their documented references or fixture inputs. A frontend compilation, an old recorded 496-pass installer suite and the 520-pass historical gameplay round are distinct scopes; source publication does not rerun or merge them.

This source-only repository excludes binaries needed to run the existing isolated tests, but includes their scripts and all project application source. Downloading exact third-party dependencies, providing legitimate game references and using a marked isolated test directory are reviewer-controlled prerequisites. No original game/save/account data is uploaded to supply them.

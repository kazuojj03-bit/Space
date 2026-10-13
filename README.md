# SpacePhone 3.1 — full review source

Unofficial phone and counter-business Mod for the Windows x64 Steam **Probably Stolen Demo**. The gameplay is primarily Simplified Chinese. [Nexus Mod page 716](https://www.nexusmods.com/probablystolen/mods/716).

This repository is prepared for Nexus security review, not a statement that a scan or staff review has passed. Support ticket **271290** requested version-controlled full source and detailed build instructions. Published with the project owner's authorization for source inspection and build review. No game binaries or account data are supplied.

## Source and build entry points

- [BUILD.md](BUILD.md): precise commands, prerequisites and component output locations.
- `plugin/`: every C# plugin source and the `net6.0` project. Start with `PhoneInput.cs`, `TradeHooks.cs`, `PhoneCommissionClients.cs`, `CommissionRules.cs`, `BrowserClient.cs`.
- `webhost/`: complete Windows Forms/WebView2 host source and `WebViewHost.csproj`; .NET 10, WebView2 SDK **1.0.4258.31**.
- `installer/legacy/`: original CMD/PowerShell/WPF installer and isolated tests.
- `installer/oneclick/`: newer native Windows Forms EXE frontend, manifest, icon, build script, PowerShell transaction backend and fixture tests. Start with `SpacePhoneSetup.cs` and `engine/OneClick.Core.ps1`.
- `assets/`: 21 procedural project icons, no exported original game textures or fonts.
- `third-party/`: original notices and corresponding expanded source for the distributed LGPL components.
- [Historical source correspondence](docs/HISTORICAL-SOURCE.md): the production DLL identity and the disclosed one-line developer test-hook difference.
- [Dependency provenance](docs/DEPENDENCY-AUDIT.md): exact versions, source URLs, hashes and licensing records.

All project C#/PowerShell/CMD and asset files are unchanged from the previously prepared reviewer source. This snapshot repairs documentation links, records the support requirement and adds detailed staging limitations. See `docs/SOURCE-HASHES-CURRENT.json` for current per-file identities.

## Runtime/data/network behavior

The plugin retains the original game's number dialing and telephone stories, adds suppliers, guided tasks, native counter partners and physical requests. Stock prices are local fictional game data and never real brokerage transactions. Matching-client payment and actual native item transfer advance requests.

The optional BrowserHost opens the official Douyin and TikTok websites. Website browsing and sign-in communicate with those websites; platform availability, network and account restrictions apply. Local commands use a named pipe restricted to the current user. Browser session storage is local `SpacePhone/WebProfiles` under LocalApplicationData, outside game saves; password autosave/autofill are disabled, WebView2 downloads are cancelled, and logout clears site sessions. Business records and game saves are not uploaded by the plugin.

The installer acts only after the user's install action and normal Windows UAC. It validates files, preserves compatible shared dependencies, provides backup/rollback and does not force-close or start the game. Legacy CMD uses process-level PowerShell ExecutionPolicy Bypass, without modifying machine-wide policy. There is no automatic Mod updater. A new BepInEx installation can download matching Unity base libraries on first game launch. Microsoft Evergreen WebView2 has its own runtime servicing.

No original game binaries, generated game interop, extracted NPC textures, fonts, saves, account profiles, personal music, credentials or precompiled Mod/runtime binaries are in this repository. Local legitimate build references are required.

## Review boundaries and rights

Historical production: `SpacePhone.dll`, 244,224 bytes, SHA256 `a6835e5c4a5b932a8870621f4f4beba35560c29754f12e564240bea0a487b0f2`. The plugin source differs from that binary's historical input by the disclosed developer path-specific branch. Rebuilds are not asserted byte-identical. Both installer generations are supplied so a reviewer can inspect the original quarantined file and the proposed replacement separately.

The documented isolated gameplay/installer checks are functional evidence, not malware clearance. The remote Nexus upload and its scanner-specific result have not been retrieved independently. Please review actual file identities and scans before approval.

Project author permissions are retained; this reviewer publication does not apply a new permissive license to project code, icons or writing. Third-party licenses remain attached to their own components. No game publisher, Apple, Douyin or TikTok affiliation is claimed. Generative AI extensively assisted the code, UI, dialogue and release materials: **AI-Generated Content / AI Media**.

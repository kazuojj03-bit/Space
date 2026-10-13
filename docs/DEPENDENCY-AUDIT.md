> Historical dependency audit snapshot. In this repository, the audit\'s `public-licenses/` is supplied as `third-party/`, with expanded corresponding sources in `third-party/sources/`. The runtime `loader-payload/` and diagnostic NuGet metadata are not bundled; obtain the exact official loader and verify it against `docs/loader-verified-manifest.json`. This note maps historical audit names rather than pretending they are current local paths.

# SpacePhone 3.1 一键包依赖审计

日期：2026-10-13。只在本工作目录下载、展开和检查依赖；未安装系统运行时，未执行游戏、读取存档或改动游戏目录。

## BepInEx 加载器

原下载文件 `BepInEx-IL2CPP.zip` 为 **34336405 字节**，SHA256：
`f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a`。

重新从 BepInEx 官方构建站下载 be.788 的 Windows x64 IL2CPP 包，字节数及完整 ZIP SHA256 与旧文件完全一致。BepInEx.Core 程序集中的 InformationalVersion 是 `6.0.0-be.788+5b766a3b7f6c164d4798924a93f3acf4db769d06`。官方地址：

https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788+5b766a3.zip

原包有 **228 个非空文件**；空 plugins/patchers 文件夹不是运行文件。白名单和逐文件 SHA256 见 `loader-verified-manifest.json`。展开的 `loader-payload/` 仅包含：BepInEx/core、dotnet 内嵌运行时、winhttp.dll、doorstop_config.ini、.doorstop_version、changelog.txt。没有游戏 interop、游戏原程序集、用户插件、游戏存档、字体、人物图片或音频。原官方 ZIP 没有随附许可文本；新整合包必须额外附上本目录整理的 `public-licenses/`。

版本与许可：

| 组件 | 确认版本 / 提交 | 许可 |
| --- | --- | --- |
| BepInEx | be.788 / 5b766a3b7f6c164d4798924a93f3acf4db769d06 | LGPL 2.1 |
| UnityDoorstop | 4.5.0 | LGPL 2.1 |
| Il2CppInterop Common / Generator / HarmonySupport / Runtime | 1.5.3 / dbda1cb353b0f4253345dc45136d170b9e50a5a0 | LGPL 3.0 only |
| 内嵌 mini CoreCLR | .NET 6.0.7 / 官方 BepInEx dotnet-runtime 发布 | MIT 及 .NET 第三方 notices |
| Dobby | BepInEx fork 1.0.5 | Apache 2.0 |
| HarmonyX | 2.10.2 | MIT |
| MonoMod RuntimeDetour / Utils | 22.7.31.1 | MIT |
| MonoMod Backports / ILHelpers | 1.1.2 / 1.1.0，a1b82852b2574742776af08818487b90b0bfab93 | MIT |
| Mono.Cecil / Mdb / Pdb / Rocks | 0.11.4 | MIT |
| AsmResolver / DotNet / PE / PE.File | 6.0.0-beta.5 / 124e161a9bc0ebb83b4d97276ae3da3c9cfe2fc5 | MIT |
| AssetRipper.Primitives | 3.2.0 / d759b3db98bf2a1cb8c2181faf5d405610aac585 | MIT，另附 uTinyRipper notice |
| AssetRipper.CIL | 1.2.2 / 06216e69825ccd6536dc17e9ca81c4d539ce9321 | MIT |
| AssetRipper.Gee.External.Capstone | 2.3.2 / b90e380c14e857865c94a355954236e678bd557c | MIT |
| Cpp2IL / LibCpp2IL / StableNameDotNet / WasmDisassembler | 2022.1.0-development.1452 / 558ddd98642010897d54316b51fbaa7889fda093 | MIT |
| Disarm | 2022.1.0-master.99 / 8574010015c8c56bb7faad7e407c50b0abe9cc72 | MIT |
| Iced | 1.21.0 | MIT |
| SemanticVersioning | 2.0.2 / c52c9d4c5d34a6a3bb6ff45f7276d8b6e951a42d | MIT |

具体 NuGet 元数据与哈希另见 `nuget-component-metadata.json`。不把所有第三方都标为 MIT；不将本项目自己的代码或微软 WebView2 Runtime 套用 LGPL。

## 首次启动与网络

be.788 源码中 `UnityBaseLibrariesSource` 默认是 `https://unity.bepinex.dev/libraries/{VERSION}.zip`；已有对应 ZIP 缓存时使用缓存，否则启动时下载 Unity 基础接口库，然后根据玩家自己的游戏生成 `BepInEx/interop`。这一过程发生在游戏正常启动时，一键安装器不启动游戏，也不携带从用户游戏提取或生成的 interop。

因此可以称“**加载器和微软浏览器运行时已随包提供，不必分别找依赖**”，不能称“第一次运行全部 100% 离线”。首次生成接口及浏览真实视频网站需要网络；首次启动可能较慢。源码依据为本包对应 BepInEx `Il2CppInteropManager.cs`，官方接口库说明为 https://unity.bepinex.dev/ 。

## Microsoft WebView2 Evergreen Standalone x64

已从官方 x64 Standalone 链接下载，**没有运行安装**：

- 稳定入口：https://go.microsoft.com/fwlink/?LinkId=2124701
- 本次最终地址：https://msedge.sf.dl.delivery.mp.microsoft.com/filestreamingservice/files/7c7c0e6f-8cb5-406a-8e51-df0c62011e55/MicrosoftEdgeWebView2RuntimeInstallerX64.exe
- 文件：`MicrosoftEdgeWebView2RuntimeInstallerX64-official.exe`，发布时使用原正式名称 `MicrosoftEdgeWebView2RuntimeInstallerX64.exe`。
- 大小：**214090448 字节**。
- SHA256：`2a6add76c37bfa872eb8c2b22d45b3216b8e2a1f193e0774006340ff62ac5fa0`。
- Authenticode：**Valid**，签名者 **Microsoft Corporation**。
- 文件版本 `1.3.275.13` 为安装器版本，不声称它是实际 Chromium/WebView2 引擎版本。

`LinkId=2124703` 是约 1.86 MB 的联网 Bootstrapper；它只用于核对区别，不进入全依赖离线安装包。不能将其改名当 Standalone。

微软官方部署文档明确说明可随应用包含 Evergreen Standalone Installer，并给出 `/silent /install`：
https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution

完整正式许可由官方下载组件使用的 API 取得，英文和微软官方简体中文版都保存在 public-licenses：
https://developer.microsoft.com/microsoft-edge/api/eula/webview2?locale=en-us
https://developer.microsoft.com/microsoft-edge/api/eula/webview2?locale=zh-cn

该 Runtime 是微软专有软件，具有 object-code 分发权及条件。分发要求包括让终端用户同意保护微软和代码的条款；一键 EXE 应在安装 Runtime 前提供本地许可入口，清楚说明点击安装表示同意这些条款，或用未预勾选的许可同意框。不修改微软安装器及其签名。只有 Runtime 缺失时才运行它；检测到已安装则保留。卸载手机不移除供其他应用共享的 WebView2 Runtime。

## LGPL 源码与公开材料

`public-licenses/sources/` 中为展开的 BepInEx 精确提交、Il2CppInterop 1.5.3、Doorstop 4.5.0 源码，**没有嵌套 ZIP**。这是相应 LGPL 组件的源文件、构建工程、脚本和原许可证，保持上游文本不变。建议随一键包一起提供；若移至 Nexus 同页独立 Source 附件，需要持续保持与二进制版本相对应且可获得，而不是只留指向未来会变更的 master 分支的网页。

4 个外部预编译引用文件未重新分发：Bep 源码自带的 Unity.InputSystem/CoreModule/InputLegacyModule，以及 Interop 源码自带的 Il2Cppmscorlib。它们是外部编译引用，源代码文件没有因此删改；完整列表、取得路径和构建条件见 `public-licenses/sources/BUILD-AND-REFERENCES.txt`。不宣称能够完全无外部环境或逐字节重建官方 loader；按上游工程及合法接口引用构建。

本审计是来源、版本及实际分发条款记录，不是任何作品的完整法律保证。Nexus 隔离问题须以实际站内原因与审核结果为准，不能以依赖签名或有源码断言必定通过扫描，也不应改扩展名或通过自解压隐藏可执行文件。

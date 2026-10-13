# SpacePhone full offline installer backend. No game execution, no save access.
. (Join-Path $PSScriptRoot 'Legacy.Helpers.ps1')
$script:OCMetadata='SpacePhone-oneclick'
$script:OCFixtureRoot=[IO.Path]::GetFullPath((Join-Path (Split-Path $PSScriptRoot -Parent) 'test-artifacts'))
$script:OCRootLoaderFiles=@('winhttp.dll','doorstop_config.ini','.doorstop_version','changelog.txt','LICENSE','LICENSE.txt','LICENSE.md','README.md','README.txt')
$script:OCRequiredLoader=@('winhttp.dll','doorstop_config.ini','BepInEx/core/BepInEx.Core.dll','BepInEx/core/BepInEx.Unity.IL2CPP.dll','BepInEx/core/BepInEx.Preloader.Core.dll','BepInEx/core/Il2CppInterop.Runtime.dll','BepInEx/core/0Harmony.dll','dotnet/coreclr.dll','dotnet/hostpolicy.dll','dotnet/System.Private.CoreLib.dll')

function Assert-SPNoReparse([string]$Path) {
    # Same ancestor-by-ancestor check as the stable installer, using native attributes
    # instead of two PowerShell provider lookups per ancestor. Missing future paths
    # are allowed; permission/IO errors and actual links are never ignored.
    $p=[IO.Path]::GetFullPath($Path)
    while ($p) {
        try {
            $attributes=[IO.File]::GetAttributes($p)
            if (($attributes-band[IO.FileAttributes]::ReparsePoint)-ne0) {throw "不支持符号链接、联接或云占位路径：$p"}
        } catch [IO.FileNotFoundException] {} catch [IO.DirectoryNotFoundException] {}
        $parent=[IO.Path]::GetDirectoryName($p)
        if ($parent-eq$p) {break}
        $p=$parent
    }
}

function Get-OCKind([string]$Path) {
    Assert-SPRelative $Path
    if ($Path.StartsWith($script:SPPrefix,[StringComparison]::Ordinal)) { $null=Get-SPComponent $Path; return 'Mod' }
    if ($script:OCRootLoaderFiles -ccontains $Path) { return 'Loader' }
    if ($Path.StartsWith('BepInEx/core/',[StringComparison]::Ordinal) -or $Path.StartsWith('dotnet/',[StringComparison]::Ordinal)) { return 'Loader' }
    throw "一键安装器不会管理此文件：$Path"
}
function Assert-OCFixture([string]$GamePath,[bool]$FixtureMode,[int]$FailAfterFiles) {
    if ($FailAfterFiles -gt 0 -and -not $FixtureMode) { throw '失败注入只允许独立 fixture。' }
    if ($FixtureMode) {
        Assert-SPWithin $script:OCFixtureRoot $GamePath
        $marker=Join-SPSafe $GamePath '.spacephone-test-fixture'
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw).Trim() -cne 'SPACEPHONE-ISOLATED-TEST-NO-GAME-SAVES') { throw '测试目录没有独立 fixture 标记。' }
    }
}
function Assert-OCAMD64([string]$Path) {
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $reader=New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 128 -or $reader.ReadUInt16() -ne 0x5A4D) { throw '不是有效 Windows PE 文件。' }
        $stream.Position=0x3C
        $offset=$reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt ($stream.Length-6)) { throw 'PE 文件头损坏。' }
        $stream.Position=$offset
        if ($reader.ReadUInt32() -ne 0x4550 -or $reader.ReadUInt16() -ne 0x8664) { throw '需要 Windows AMD64（x64）版本。' }
    } catch { throw "文件架构检查失败：$([IO.Path]::GetFileName($Path))。$($_.Exception.Message)" }
    finally { $reader.Dispose(); $stream.Dispose() }
}
function Assert-OCGame([string]$GamePath,[bool]$FixtureMode=$false,[int]$FailAfterFiles=0) {
    $root=Get-SPFullPath $GamePath
    Assert-SPNoReparse $root
    Assert-OCFixture $root $FixtureMode $FailAfterFiles
    foreach ($rel in @('Probably Stolen.exe','GameAssembly.dll','UnityPlayer.dll')) {
        $p=Join-SPSafe $root $rel
        if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw '请选择包含 Probably Stolen.exe、GameAssembly.dll 和 UnityPlayer.dll 的游戏根文件夹。' }
        Assert-OCAMD64 $p
    }
    if (-not (Test-Path -LiteralPath (Join-SPSafe $root 'Probably Stolen_Data') -PathType Container)) { throw '缺少 Probably Stolen_Data 游戏数据文件夹。' }
    foreach ($rel in @('BepInEx','BepInEx/core','BepInEx/plugins','BepInEx/plugins/SpacePhone','dotnet',$script:OCMetadata,($script:OCMetadata+'/backups'),($script:OCMetadata+'/staging'))) {
        $p=Join-SPSafe $root $rel
        if (Test-Path -LiteralPath $p -PathType Leaf) { throw "安装目录被文件占用：$rel" }
    }
    if (Test-Path -LiteralPath (Join-SPSafe $root ($script:OCMetadata+'/state.json')) -PathType Container) {throw '一键安装状态文件位置被目录占用，游戏文件尚未改动。'}
    Assert-SPNotRunning $root
    return $root
}
function Read-OCPackage([string]$PackageRoot) {
    $root=Get-SPFullPath $PackageRoot
    Assert-SPNoReparse $root
    $mod=Read-SPManifest (Join-SPSafe $root 'package/mod')
    $loaderRoot=Join-SPSafe $root 'package/loader'
    $manifestPath=Join-SPSafe $root 'loader-manifest.json'
    $m=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($m.schemaVersion -ne 1 -or $m.loaderId -cne 'BepInEx-Unity-IL2CPP-x64' -or -not $m.version -or -not $m.files) { throw '随包加载器清单身份不正确。' }
    $seen=@{}; $files=@()
    foreach ($f in @($m.files)) {
        if ((Get-OCKind ([string]$f.path)) -ne 'Loader') { throw '加载器清单含有 Mod 或保护文件。' }
        if ($seen.ContainsKey([string]$f.path) -or [string]$f.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$f.size -lt 0) { throw '加载器清单路径或校验字段无效。' }
        $seen[[string]$f.path]=$true
        $source=Join-SPSafe $loaderRoot ([string]$f.path)
        if (-not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-SPHash $source) -ne ([string]$f.sha256).ToLowerInvariant() -or (Get-Item -LiteralPath $source).Length -ne [long]$f.size) { throw "随包加载器校验失败：$($f.path)" }
        $files += [pscustomobject]@{path=[string]$f.path;sha256=([string]$f.sha256).ToLowerInvariant();size=[long]$f.size;kind='Loader';source=$source}
    }
    foreach ($rel in $script:OCRequiredLoader) { if (-not $seen.ContainsKey($rel)) { throw "随包加载器不完整：$rel" } }
    Assert-OCAMD64 (Join-SPSafe $loaderRoot 'winhttp.dll')
    Assert-OCAMD64 (Join-SPSafe $loaderRoot 'dotnet/coreclr.dll')
    $modFiles=@()
    foreach ($f in $mod.files) { $modFiles += [pscustomobject]@{path=$f.path;sha256=$f.sha256;size=$f.size;kind='Mod';source=(Join-SPSafe $mod.payload $f.path)} }
    return [pscustomobject]@{root=$root;mod=$mod;modFiles=$modFiles;loaderFiles=$files;loaderVersion=[string]$m.version;loaderManifestSha256=(Get-SPHash $manifestPath)}
}
function Assert-OCManagedPE([string]$Path) {
    $stream=[IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $reader=New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length-lt128 -or $reader.ReadUInt16()-ne0x5a4d) {throw '程序集不是有效 PE 文件。'}
        $stream.Position=0x3c;$offset=$reader.ReadInt32()
        if($offset-lt64-or$offset-gt($stream.Length-6)){throw '程序集 PE 文件头损坏。'}
        $stream.Position=$offset
        if($reader.ReadUInt32()-ne0x4550){throw '程序集 PE 签名无效。'}
        $machine=$reader.ReadUInt16()
        if($machine-ne0x8664-and$machine-ne0x014c){throw '不是受支持的 Windows x64/AnyCPU 程序集。'}
    }catch{throw "现有加载器文件损坏或不兼容：$([IO.Path]::GetFileName($Path))。$($_.Exception.Message)"}
    finally{$reader.Dispose();$stream.Dispose()}
}
function Get-OCLoaderStatus([string]$GamePath,$LoaderFiles=$null) {
    foreach ($rel in @('MelonLoader','version.dll','dinput8.dll','BepInEx/core/BepInEx.dll','BepInEx/core/BepInEx.Unity.Mono.dll')) {
        if (Test-Path -LiteralPath (Join-SPSafe $GamePath $rel)) { throw '发现其他或混合加载器，安装器不会覆盖。请在干净游戏副本测试，或先由你原有加载器的说明恢复干净环境。' }
    }
    $signals=@('winhttp.dll','doorstop_config.ini','.doorstop_version','BepInEx/core','dotnet')
    $found=$false
    foreach ($rel in $signals) { if (Test-Path -LiteralPath (Join-SPSafe $GamePath $rel)) { $found=$true } }
    if (-not $found) { return 'Missing' }
    $required=@($script:OCRequiredLoader)
    # Include every executable/runtime DLL and runtime JSON from the validated
    # official package. XML docs, release notes and licence files are optional.
    if ($LoaderFiles) {foreach($file in @($LoaderFiles)){if(([string]$file.path)-match '\.dll$' -or ([string]$file.path)-match '^(dotnet|BepInEx/core)/.+\.json$'){$required+=[string]$file.path}}}
    foreach ($rel in @($required|Sort-Object -Unique)) {
        if (-not (Test-Path -LiteralPath (Join-SPSafe $GamePath $rel) -PathType Leaf)) { throw '现有加载器安装不完整。为避免破坏已有 Mod，安装器没有改动文件。请恢复其完整安装或使用干净的游戏目录。' }
        $existing=Join-SPSafe $GamePath $rel
        if ((Get-Item -LiteralPath $existing).Length-eq0){throw '现有加载器含有空文件，安装器没有改动文件。'}
        if ($rel-match'\.dll$') {Assert-OCManagedPE $existing}
    }
    Assert-OCAMD64 (Join-SPSafe $GamePath 'winhttp.dll')
    Assert-OCAMD64 (Join-SPSafe $GamePath 'dotnet/coreclr.dll')
    $ini=Get-Content -LiteralPath (Join-SPSafe $GamePath 'doorstop_config.ini') -Raw
    if ($ini -notmatch '(?im)^\s*enabled\s*=\s*true\s*$' -or $ini -notmatch '(?im)^\s*target_assembly\s*=\s*BepInEx[\\/]core[\\/]BepInEx\.Unity\.IL2CPP\.dll\s*$' -or $ini -notmatch '(?im)^\s*coreclr_path\s*=\s*dotnet[\\/]coreclr\.dll\s*$') { throw '现有 Doorstop 配置不是受支持的 BepInEx IL2CPP 配置，安装器不会覆盖。' }
    return 'Existing'
}
function Assert-OCState($State,[string]$GamePath) {
    if ($State.schemaVersion -ne 1 -or $State.modId -cne 'SpacePhone.OneClick' -or (Get-SPFullPath ([string]$State.gamePath)) -ine $GamePath) { throw '一键安装状态与游戏目录不匹配。' }
    $seen=@{}
    foreach ($f in @($State.managedFiles)) {
        $null=Get-OCKind ([string]$f.path)
        if ($seen.ContainsKey([string]$f.path) -or [string]$f.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw '一键安装状态清单无效。' }
        $seen[[string]$f.path]=$true
    }
    if ($State.backupRelative -and [string]$State.backupRelative -notmatch '^SpacePhone-oneclick/backups/[a-zA-Z0-9-]+$') { throw '一键安装备份路径无效。' }
}
function Get-OCState([string]$GamePath) {
    $path=Join-SPSafe $GamePath ($script:OCMetadata+'/state.json')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $state=Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-OCState $state $GamePath
    return $state
}
function New-OCBackup([string]$GamePath,[string[]]$Paths,$PreviousState,[string]$Operation) {
    $relative=$script:OCMetadata+'/backups/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)
    $backup=Join-SPSafe $GamePath $relative
    [IO.Directory]::CreateDirectory($backup) | Out-Null
    $records=@(); $newDirs=@{}
    foreach ($rel in @($Paths|Sort-Object -Unique)) {
        $null=Get-OCKind $rel
        $source=Join-SPSafe $GamePath $rel
        $parent=[IO.Path]::GetDirectoryName($source)
        while ($parent -ine $GamePath) {
            if (-not (Test-Path -LiteralPath $parent)) { $dirRel=$parent.Substring($GamePath.Length+1).Replace('\','/'); Assert-SPRelative $dirRel; $newDirs[$dirRel]=$true }
            $parent=[IO.Path]::GetDirectoryName($parent)
        }
        $record=[ordered]@{path=$rel;existed=[bool](Test-Path -LiteralPath $source -PathType Leaf);sha256=$null;size=0;backupFile=$null}
        if ($record.existed) {
            $record.backupFile='files/'+('{0:D5}'-f ($records.Count+1))+'.bin'
            $copy=Join-SPSafe $backup $record.backupFile
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copy)) | Out-Null
            Copy-Item -LiteralPath $source -Destination $copy
            $record.sha256=Get-SPHash $source
            $record.size=(Get-Item -LiteralPath $source).Length
            if ((Get-SPHash $copy) -ne $record.sha256) { throw "组合安装备份校验失败：$rel" }
        }
        $records += [pscustomobject]$record
    }
    $snapshot=[ordered]@{schemaVersion=1;modId='SpacePhone.OneClick';gamePath=$GamePath;createdUtc=[DateTime]::UtcNow.ToString('o');operation=$Operation;previousState=$PreviousState;files=$records;createdDirectories=@($newDirs.Keys)}
    Write-SPJson (Join-SPSafe $backup 'snapshot.json') $snapshot
    return [pscustomobject]@{relative=$relative;path=$backup;snapshot=[pscustomobject]$snapshot}
}
function Read-OCBackup([string]$GamePath,[string]$Relative) {
    if ($Relative -notmatch '^SpacePhone-oneclick/backups/[a-zA-Z0-9-]+$') { throw '备份位置不安全。' }
    $backup=Join-SPSafe $GamePath $Relative
    $s=Get-Content -LiteralPath (Join-SPSafe $backup 'snapshot.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($s.schemaVersion -ne 1 -or $s.modId -cne 'SpacePhone.OneClick' -or (Get-SPFullPath ([string]$s.gamePath)) -ine $GamePath) { throw '组合安装备份不属于这个游戏。' }
    $seen=@{}
    foreach ($f in @($s.files)) {
        $null=Get-OCKind ([string]$f.path)
        if ($seen.ContainsKey([string]$f.path)) { throw '组合备份路径重复。' }
        $seen[[string]$f.path]=$true
        $null=Join-SPSafe $GamePath ([string]$f.path)
        if ($f.existed) {
            if ([string]$f.backupFile -notmatch '^files/[0-9]{5}\.bin$' -or [string]$f.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw '组合备份校验字段错误。' }
            $p=Join-SPSafe $backup ([string]$f.backupFile)
            if (-not (Test-Path -LiteralPath $p -PathType Leaf) -or (Get-SPHash $p) -ne [string]$f.sha256 -or (Get-Item -LiteralPath $p).Length -ne [long]$f.size) { throw "组合备份损坏：$($f.path)" }
        }
    }
    foreach ($dir in @($s.createdDirectories)) {
        Assert-SPRelative ([string]$dir)
        # Each empty directory must be an ancestor of an explicitly allowed file.
        if (@($s.files|Where-Object {([string]$_.path).StartsWith(($dir+'/'),[StringComparison]::OrdinalIgnoreCase)}).Count -eq 0) { throw '备份的目录清单超出范围。' }
        $null=Join-SPSafe $GamePath ([string]$dir)
    }
    if ($s.previousState) { Assert-OCState $s.previousState $GamePath }
    return [pscustomobject]@{relative=$Relative;path=$backup;snapshot=$s}
}
function Restore-OCBackup([string]$GamePath,$Backup) {
    Assert-SPTargetLayout $GamePath $Backup.snapshot.files
    foreach ($f in @($Backup.snapshot.files)) {
        $target=Join-SPSafe $GamePath ([string]$f.path)
        if ($f.existed) {
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            Copy-Item -LiteralPath (Join-SPSafe $Backup.path ([string]$f.backupFile)) -Destination $target -Force
            if ((Get-SPHash $target) -ne [string]$f.sha256) { throw "组合备份恢复校验失败：$($f.path)" }
        } elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
    }
    foreach ($dir in @($Backup.snapshot.createdDirectories|Sort-Object {$_.Length} -Descending)) {
        $p=Join-SPSafe $GamePath ([string]$dir)
        if ((Test-Path -LiteralPath $p -PathType Container) -and @(Get-ChildItem -LiteralPath $p -Force).Count -eq 0) { [IO.Directory]::Delete($p) }
    }
    $statePath=Join-SPSafe $GamePath ($script:OCMetadata+'/state.json')
    if ($Backup.snapshot.previousState) { Write-SPJson $statePath $Backup.snapshot.previousState }
    elseif (Test-Path -LiteralPath $statePath -PathType Leaf) { Remove-Item -LiteralPath $statePath -Force }
}
function Get-OCPlan([string]$GamePath,[string]$PackageRoot,[bool]$FixtureMode=$false,[int]$FailAfterFiles=0,[scriptblock]$Progress) {
    $root=Assert-OCGame $GamePath $FixtureMode $FailAfterFiles
    if ($Progress) { & $Progress '校验随包依赖与 Mod' 6 }
    $package=Read-OCPackage $PackageRoot
    $loaderStatus=Get-OCLoaderStatus $root $package.loaderFiles
    $previous=Get-OCState $root
    $files=@()
    if ($loaderStatus -eq 'Missing') { $files += $package.loaderFiles }
    $files += $package.modFiles
    Assert-SPTargetLayout $root $files
    $inventory=@(Get-SPInventory $root $package.mod.components)
    $desired=@{};foreach ($f in $files) {$desired[$f.path]=$f}
    $same=$loaderStatus -eq 'Existing'
    foreach ($f in $files) {
        $p=Join-SPSafe $root $f.path
        if (-not (Test-Path -LiteralPath $p -PathType Leaf) -or (Get-SPHash $p) -ne $f.sha256) {$same=$false;break}
    }
    if ($same) { foreach ($rel in $inventory) { if (-not $desired.ContainsKey($rel)) {$same=$false;break} } }
    return [pscustomobject]@{gamePath=$root;package=$package;loaderStatus=$loaderStatus;files=$files;inventory=$inventory;previous=$previous;same=$same;desired=$desired}
}
function Invoke-OCInstallInternal([string]$GamePath,[string]$PackageRoot,[bool]$FixtureMode=$false,[int]$FailAfterFiles=0,[scriptblock]$Progress) {
    $plan=Get-OCPlan $GamePath $PackageRoot $FixtureMode $FailAfterFiles $Progress
    $root=$plan.gamePath
    if ($plan.same -and $plan.previous) {return [pscustomobject]@{status='AlreadyInstalled';message='已经安装相同版本，可直接启动游戏。';loaderAction='Preserved';backupPath=$null}}
    $paths=@($plan.inventory)+@($plan.files|ForEach-Object {$_.path})
    if ($Progress) {& $Progress '备份原有文件' 18}
    $backup=New-OCBackup $root $paths $plan.previous 'Install'
    $stage=Join-SPSafe $root ($script:OCMetadata+'/staging/'+[Guid]::NewGuid().ToString('N').Substring(0,8))
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $staged=@{}; $touched=$false
    try {
        $index=0
        foreach ($f in $plan.files) {
            $index++;$p=Join-SPSafe $stage (('{0:D5}' -f $index)+'.bin')
            Copy-Item -LiteralPath $f.source -Destination $p
            if ((Get-SPHash $p) -ne $f.sha256) {throw '组合安装临时文件校验失败。'}
            $staged[$f.path]=$p
        }
        Assert-SPNotRunning $root
        $touched=$true
        foreach ($rel in $plan.inventory) {if (-not $plan.desired.ContainsKey($rel)) {Remove-Item -LiteralPath (Join-SPSafe $root $rel) -Force}}
        $index=0
        foreach ($f in $plan.files) {
            $p=Join-SPSafe $root $f.path
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p)) | Out-Null
            Copy-Item -LiteralPath $staged[$f.path] -Destination $p -Force
            if ((Get-SPHash $p) -ne $f.sha256) {throw "组合安装后校验失败：$($f.path)"}
            $index++
            if ($Progress) {& $Progress '安装手机与所需加载器' (25+[int](68*$index/$plan.files.Count))}
            if ($FailAfterFiles -gt 0 -and $index -ge $FailAfterFiles) {throw 'TEST: Simulated combined write failure'}
        }
        $managed=@($plan.files|Select-Object path,sha256,size,kind)
        if ($plan.previous) {
            foreach ($f in @($plan.previous.managedFiles)) {
                if ((Get-OCKind ([string]$f.path)) -eq 'Loader' -and $plan.loaderStatus -eq 'Existing') {$managed+=$f}
            }
        }
        $state=[ordered]@{schemaVersion=1;modId='SpacePhone.OneClick';version=$plan.package.mod.version;gamePath=$root;installedUtc=[DateTime]::UtcNow.ToString('o');backupRelative=$backup.relative;managedFiles=$managed;loaderInstalledByTool=(@($managed|Where-Object {$_.kind -eq 'Loader'}).Count -gt 0);modManifestSha256=$plan.package.mod.manifestSha256;loaderManifestSha256=$plan.package.loaderManifestSha256}
        Write-SPJson (Join-SPSafe $root ($script:OCMetadata+'/state.json')) $state
        if ($Progress) {& $Progress '安装完成' 100}
        return [pscustomobject]@{status='Installed';message='安装完成，正常启动游戏即可使用深空手机。';loaderAction=$(if($plan.loaderStatus -eq 'Missing'){'Installed'}else{'Preserved'});backupPath=$backup.path;files=$plan.files.Count}
    } catch {
        $errorMessage=$_.Exception.Message
        if ($touched) {
            try {Restore-OCBackup $root (Read-OCBackup $root $backup.relative)}
            catch {throw "安装失败：$errorMessage。自动恢复也失败：$($_.Exception.Message)。请先不要启动游戏；备份：$($backup.path)"}
            throw "安装失败，已恢复安装前的 Mod 与加载器：$errorMessage"
        }
        throw "游戏文件尚未改动：$errorMessage"
    } finally {
        if (Test-Path -LiteralPath $stage) {Assert-SPWithin (Join-SPSafe $root ($script:OCMetadata+'/staging')) $stage;Assert-SPNoReparse $stage;Remove-Item -LiteralPath $stage -Recurse -Force}
    }
}
function Invoke-OCRollbackInternal([string]$GamePath,[bool]$FixtureMode=$false) {
    $root=Assert-OCGame $GamePath $FixtureMode
    $state=Get-OCState $root
    if (-not $state -or -not $state.backupRelative) {throw '没有本工具可回退的记录。'}
    $backup=Read-OCBackup $root ([string]$state.backupRelative)
    Assert-SPTargetLayout $root $backup.snapshot.files
    $safety=New-OCBackup $root @($backup.snapshot.files|ForEach-Object {$_.path}) $state 'RollbackSafety'
    try {Restore-OCBackup $root $backup;return [pscustomobject]@{status='RolledBack';message='已恢复上次安装前的手机和加载器文件；个人数据保留。';backupPath=$safety.path}}
    catch {
        $errorMessage=$_.Exception.Message
        try {Restore-OCBackup $root (Read-OCBackup $root $safety.relative)} catch {throw "回退与安全恢复失败，备份：$($safety.path)"}
        throw "回退失败，当前安装已经恢复：$errorMessage"
    }
}
function Invoke-OCUninstallInternal([string]$GamePath,[bool]$FixtureMode=$false) {
    $root=Assert-OCGame $GamePath $FixtureMode
    $state=Get-OCState $root
    if (-not $state) {throw '本工具没有管理此目录，不能删除手工安装的文件。'}
    $modFiles=@($state.managedFiles|Where-Object {(Get-OCKind ([string]$_.path)) -eq 'Mod'})
    Assert-SPTargetLayout $root $modFiles
    $backup=New-OCBackup $root @($modFiles|ForEach-Object {$_.path}) $state 'Uninstall'
    try {
        foreach ($f in $modFiles) {$p=Join-SPSafe $root ([string]$f.path);if(Test-Path -LiteralPath $p -PathType Leaf){Remove-Item -LiteralPath $p -Force}}
        Remove-Item -LiteralPath (Join-SPSafe $root ($script:OCMetadata+'/state.json')) -Force
        return [pscustomobject]@{status='Uninstalled';message='手机 Mod 已移除；加载器、其他 Mod、音乐、账号目录和存档保留。';loaderAction='Preserved';backupPath=$backup.path}
    } catch {
        $errorMessage=$_.Exception.Message
        try {Restore-OCBackup $root (Read-OCBackup $root $backup.relative)} catch {throw "移除与恢复失败，备份：$($backup.path)"}
        throw "移除失败，当前版本已经恢复：$errorMessage"
    }
}
function Invoke-OCInstall([string]$GamePath,[string]$PackageRoot,[bool]$FixtureMode=$false,[int]$FailAfterFiles=0,[scriptblock]$Progress) {Invoke-SPExclusive $GamePath {Invoke-OCInstallInternal $GamePath $PackageRoot $FixtureMode $FailAfterFiles $Progress}}
function Invoke-OCRollback([string]$GamePath,[bool]$FixtureMode=$false) {Invoke-SPExclusive $GamePath {Invoke-OCRollbackInternal $GamePath $FixtureMode}}
function Invoke-OCUninstall([string]$GamePath,[bool]$FixtureMode=$false) {Invoke-SPExclusive $GamePath {Invoke-OCUninstallInternal $GamePath $FixtureMode}}

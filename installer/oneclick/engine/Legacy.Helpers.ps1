# SpacePhone 3.1 installer core. Windows PowerShell 5.1, no network operations.
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
$script:SPDocNames = @('README.txt','安装教程.html','更新记录.txt','开发说明.txt','功能验证记录.txt','THIRD-PARTY-NOTICES.txt')
$script:SPPrefix = 'BepInEx/plugins/SpacePhone/'

function Get-SPHash([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}
function Get-SPFullPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '请先选择游戏安装文件夹。 / Select the game folder.' }
    $p = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $p) { $p=(Get-Item -LiteralPath $p -Force).FullName }
    if ($p.TrimEnd('\','/') -eq [IO.Path]::GetPathRoot($p).TrimEnd('\','/')) { throw '不能选择磁盘根目录。' }
    return $p.TrimEnd('\','/')
}
function Assert-SPWithin([string]$Root, [string]$Path) {
    $r = (Get-SPFullPath $Root) + [IO.Path]::DirectorySeparatorChar
    $p = [IO.Path]::GetFullPath($Path)
    if (-not $p.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)) { throw "路径超出安装范围：$p" }
}
function Assert-SPNoReparse([string]$Path) {
    $p = [IO.Path]::GetFullPath($Path)
    while ($p) {
        if (Test-Path -LiteralPath $p) {
            $item = Get-Item -LiteralPath $p -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "不支持符号链接、联接或云占位路径：$p" }
        }
        $parent = [IO.Path]::GetDirectoryName($p)
        if ($parent -eq $p) { break }
        $p = $parent
    }
}
function Assert-SPRelative([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or $Path.Contains('\') -or $Path.StartsWith('/') -or $Path.Contains(':')) { throw "无效的相对路径：$Path" }
    foreach ($part in $Path.Split('/')) {
        if (-not $part -or $part -eq '.' -or $part -eq '..' -or $part -match '[<>"|?*\x00-\x1f]' -or $part -match '[ .]$' -or $part -match '^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)') { throw "不安全的文件名：$Path" }
    }
}
function Get-SPComponent([string]$Path) {
    Assert-SPRelative $Path
    if (-not $Path.StartsWith($script:SPPrefix, [StringComparison]::Ordinal)) { throw "安装清单不是 SpacePhone 文件：$Path" }
    $rel = $Path.Substring($script:SPPrefix.Length)
    if ($rel -eq 'SpacePhone.dll') { return 'Plugin' }
    if ($rel.StartsWith('Assets/',[StringComparison]::Ordinal) -and $rel.Length -gt 7) { return 'Assets' }
    if ($rel.StartsWith('BrowserHost/',[StringComparison]::Ordinal) -and $rel.Length -gt 12) { return 'BrowserHost' }
    if ($script:SPDocNames -ccontains $rel) { return "Document:$rel" }
    throw "安装器不会管理这个文件：$Path"
}
function Join-SPSafe([string]$Root,[string]$Relative) {
    Assert-SPRelative $Relative
    $p = [IO.Path]::GetFullPath((Join-Path $Root ($Relative.Replace('/',[IO.Path]::DirectorySeparatorChar))))
    Assert-SPWithin $Root $p
    Assert-SPNoReparse $p
    return $p
}
function Write-SPJson([string]$Path,$Value) {
    Assert-SPNoReparse $Path
    $folder = [IO.Path]::GetDirectoryName($Path)
    [IO.Directory]::CreateDirectory($folder) | Out-Null
    $tmp = Join-Path $folder ('.write-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($tmp,($Value | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $tmp -Destination $Path -Force
    } finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force } }
}
function Read-SPManifest([string]$PackageRoot) {
    $root = Get-SPFullPath $PackageRoot
    Assert-SPNoReparse $root
    $manifestPath = Join-SPSafe $root 'manifest.json'
    $payload = Join-SPSafe $root 'payload'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { throw '安装器旁边缺少 manifest.json；请完整解压安装器 ZIP。' }
    $m = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($m.schemaVersion -ne 1 -or $m.modId -cne 'SpacePhone' -or -not $m.version -or -not $m.files) { throw '安装清单的格式或 Mod 身份不正确。' }
    $seen = @{}
    $files = @()
    $components = @{}
    $hasDll = $false
    foreach ($file in @($m.files)) {
        $component = Get-SPComponent ([string]$file.path)
        if ($seen.ContainsKey([string]$file.path)) { throw "安装清单有重复路径：$($file.path)" }
        $seen[[string]$file.path] = $true
        if ([string]$file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or [long]$file.size -lt 0) { throw "文件校验字段无效：$($file.path)" }
        $source = Join-SPSafe $payload ([string]$file.path)
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "安装文件缺失：$($file.path)" }
        if ((Get-Item -LiteralPath $source).Length -ne [long]$file.size -or (Get-SPHash $source) -ne ([string]$file.sha256).ToLowerInvariant()) { throw "安装文件校验失败，请重新解压原始 ZIP：$($file.path)" }
        $files += [pscustomobject]@{ path=[string]$file.path; sha256=([string]$file.sha256).ToLowerInvariant(); size=[long]$file.size }
        $components[$component] = $true
        if ($component -eq 'Plugin') { $hasDll = $true }
    }
    if (-not $hasDll) { throw '安装清单缺少 SpacePhone.dll。' }
    return [pscustomobject]@{ root=$root; payload=$payload; version=[string]$m.version; files=$files; components=@($components.Keys); manifestSha256=(Get-SPHash $manifestPath) }
}
function Assert-SPGame([string]$GamePath) {
    $root = Get-SPFullPath $GamePath
    Assert-SPNoReparse $root
    foreach ($relative in @('Probably Stolen.exe','GameAssembly.dll','UnityPlayer.dll')) {
        if (-not (Test-Path -LiteralPath (Join-SPSafe $root $relative) -PathType Leaf)) { throw '请选择包含 Probably Stolen.exe、GameAssembly.dll 和 UnityPlayer.dll 的游戏根文件夹。' }
    }
    $data = Join-SPSafe $root 'Probably Stolen_Data'
    if (-not (Test-Path -LiteralPath $data -PathType Container)) { throw '缺少 Probably Stolen_Data；这不是完整的游戏根文件夹。' }
    foreach ($relative in @('winhttp.dll','BepInEx/core/BepInEx.Core.dll','BepInEx/core/BepInEx.Unity.IL2CPP.dll')) {
        if (-not (Test-Path -LiteralPath (Join-SPSafe $root $relative) -PathType Leaf)) { throw '需要先安装 BepInEx 6 IL2CPP x64 加载器，并启动游戏一次后退出。请查看随包教程；安装器不会下载或替换加载器。' }
    }
    foreach ($relative in @('BepInEx/plugins/SpacePhone','SpacePhone-installer','SpacePhone-installer/state.json')) { Assert-SPNoReparse (Join-SPSafe $root $relative) }
    foreach ($relative in @('BepInEx/plugins/SpacePhone','SpacePhone-installer','SpacePhone-installer/backups','SpacePhone-installer/staging')) {
        if (Test-Path -LiteralPath (Join-SPSafe $root $relative) -PathType Leaf) { throw "应当是安装文件夹的位置被文件占用：$relative" }
    }
    return $root
}
function Assert-SPNotRunning([string]$GamePath) {
    $root = Get-SPFullPath $GamePath
    # If Windows denies a process path, conservatively refuse the matching executable name.
    foreach ($processName in @('Probably Stolen','SpacePhone.WebViewHost')) {
        foreach ($p in @(Get-Process -Name $processName -ErrorAction SilentlyContinue)) {
            $path = $null
            try { $path = $p.Path } catch { }
            # Conservative by executable name: also covers 8.3 aliases and denied paths.
            # Do not permit writes merely because Windows reports the same path differently.
            throw '游戏或 SpacePhone 短视频窗口仍在运行。请正常保存并退出游戏、关闭短视频窗口后重试；安装器不会强制结束进程。'
        }
    }
}
function Get-SPState([string]$GamePath) {
    $p = Join-SPSafe $GamePath 'SpacePhone-installer/state.json'
    if (-not (Test-Path -LiteralPath $p)) { return $null }
    $state = Get-Content -LiteralPath $p -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($state.schemaVersion -ne 1 -or $state.modId -cne 'SpacePhone' -or (Get-SPFullPath ([string]$state.gamePath)) -ine (Get-SPFullPath $GamePath)) { throw '安装状态文件不属于这个游戏目录，不能安全继续。' }
    $seen = @{}
    foreach ($file in @($state.managedFiles)) {
        $null = Get-SPComponent ([string]$file.path)
        if ($seen.ContainsKey([string]$file.path) -or [string]$file.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw '安装状态文件的路径或校验值无效。' }
        $seen[[string]$file.path] = $true
    }
    if ($state.backupRelative) {
        Assert-SPRelative ([string]$state.backupRelative)
        if ([string]$state.backupRelative -notmatch '^SpacePhone-installer/backups/[a-zA-Z0-9-]+$') { throw '安装状态中的备份路径无效。' }
    }
    return $state
}
function Get-SPInventory([string]$GamePath,[string[]]$Components) {
    $list = @{}
    foreach ($component in $Components) {
        if ($component -eq 'Plugin') { $rel = $script:SPPrefix + 'SpacePhone.dll' }
        elseif ($component.StartsWith('Document:')) { $rel = $script:SPPrefix + $component.Substring(9) }
        elseif ($component -eq 'Assets' -or $component -eq 'BrowserHost') {
            $rel = $script:SPPrefix + $component
            $dir = Join-SPSafe $GamePath $rel
            if (Test-Path -LiteralPath $dir) {
                if (-not (Test-Path -LiteralPath $dir -PathType Container)) { throw "应当是文件夹的位置被文件占用：$rel" }
                # Enumerate manually and never descend into reparse points.
                $queue = New-Object 'Collections.Generic.Queue[string]'
                $queue.Enqueue($dir)
                while ($queue.Count -gt 0) {
                    $current = $queue.Dequeue()
                    foreach ($entry in @(Get-ChildItem -LiteralPath $current -Force)) {
                        Assert-SPNoReparse $entry.FullName
                        if ($entry.PSIsContainer) { $queue.Enqueue($entry.FullName) }
                        else {
                            $relative = $entry.FullName.Substring($GamePath.Length+1).Replace('\','/')
                            $null = Get-SPComponent $relative
                            $list[$relative] = $true
                        }
                    }
                }
            }
            continue
        } else { throw "未知组件：$component" }
        $p = Join-SPSafe $GamePath $rel
        if (Test-Path -LiteralPath $p) {
            if (-not (Test-Path -LiteralPath $p -PathType Leaf)) { throw "应当是文件的位置被文件夹占用：$rel" }
            $list[$rel] = $true
        }
    }
    return @($list.Keys | Sort-Object)
}
function Assert-SPTargetLayout([string]$GamePath,$Files) {
    # No automatic file/directory type migrations: reject these before backup or writes.
    foreach ($file in @($Files)) {
        $target=Join-SPSafe $GamePath ([string]$file.path)
        if ($target.Length -ge 260 -or [IO.Path]::GetDirectoryName($target).Length -ge 248) { throw '游戏路径过长，Windows PowerShell 5.1 无法安全安装。请把游戏移到较短的 Steam 库路径后重试；游戏文件尚未改动。' }
        if (Test-Path -LiteralPath $target -PathType Container) { throw "安装文件的位置已经是文件夹，请先处理路径冲突：$($file.path)" }
        $parent=[IO.Path]::GetDirectoryName($target)
        while ($parent -and $parent -ine $GamePath) {
            Assert-SPWithin $GamePath $parent
            Assert-SPNoReparse $parent
            if (Test-Path -LiteralPath $parent -PathType Leaf) { throw "安装文件的父路径被文件占用，请先处理路径冲突：$parent" }
            $parent=[IO.Path]::GetDirectoryName($parent)
        }
    }
}
function New-SPBackup([string]$GamePath,[string[]]$Paths,$PreviousState,[string]$Operation) {
    $tag = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $relative = 'SpacePhone-installer/backups/' + $tag
    $backup = Join-SPSafe $GamePath $relative
    [IO.Directory]::CreateDirectory($backup) | Out-Null
    $records = @()
    foreach ($rel in @($Paths | Sort-Object -Unique)) {
        $null = Get-SPComponent $rel
        $source = Join-SPSafe $GamePath $rel
        $exists = Test-Path -LiteralPath $source -PathType Leaf
        $record = [ordered]@{ path=$rel; existed=[bool]$exists; sha256=$null; size=0; backupFile=$null }
        if ($exists) {
            $record.backupFile='files/'+('{0:D5}' -f ($records.Count+1))+'.bin'
            $copy = Join-SPSafe $backup $record.backupFile
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($copy)) | Out-Null
            Copy-Item -LiteralPath $source -Destination $copy
            $record.sha256 = Get-SPHash $source
            $record.size = (Get-Item -LiteralPath $source).Length
            if ((Get-SPHash $copy) -ne $record.sha256) { throw "备份校验失败：$rel。游戏尚未改动。" }
        }
        $records += [pscustomobject]$record
    }
    $snapshot = [ordered]@{ schemaVersion=1; modId='SpacePhone'; gamePath=$GamePath; createdUtc=[DateTime]::UtcNow.ToString('o'); operation=$Operation; previousState=$PreviousState; files=$records }
    Write-SPJson (Join-SPSafe $backup 'snapshot.json') $snapshot
    return [pscustomobject]@{ relative=$relative; path=$backup; snapshot=[pscustomobject]$snapshot }
}
function Read-SPBackup([string]$GamePath,[string]$Relative) {
    Assert-SPRelative $Relative
    if ($Relative -notmatch '^SpacePhone-installer/backups/[a-zA-Z0-9-]+$') { throw '备份位置不安全。' }
    $backup = Join-SPSafe $GamePath $Relative
    $record = Get-Content -LiteralPath (Join-SPSafe $backup 'snapshot.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($record.schemaVersion -ne 1 -or $record.modId -cne 'SpacePhone' -or (Get-SPFullPath ([string]$record.gamePath)) -ine $GamePath) { throw '备份不属于这个游戏。' }
    $seen = @{}
    foreach ($file in @($record.files)) {
        $null = Get-SPComponent ([string]$file.path)
        if ($seen.ContainsKey([string]$file.path)) { throw '备份清单有重复路径。' }
        $seen[[string]$file.path] = $true
        $null = Join-SPSafe $GamePath ([string]$file.path)
        if ($file.existed) {
            if (-not $file.PSObject.Properties['backupFile'] -or [string]$file.backupFile -notmatch '^files/[0-9]{5}\.bin$') { throw '备份文件位置无效。' }
            $source = Join-SPSafe $backup ([string]$file.backupFile)
            if ([string]$file.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or -not (Test-Path -LiteralPath $source -PathType Leaf) -or (Get-SPHash $source) -ne [string]$file.sha256 -or (Get-Item -LiteralPath $source).Length -ne [long]$file.size) { throw "备份文件损坏，未开始回退：$($file.path)" }
        }
    }
    # Previous state is validated separately before it is restored.
    if ($record.previousState) {
        if ($record.previousState.schemaVersion -ne 1 -or $record.previousState.modId -cne 'SpacePhone' -or (Get-SPFullPath ([string]$record.previousState.gamePath)) -ine $GamePath) { throw '备份中的旧安装状态无效。' }
        foreach ($file in @($record.previousState.managedFiles)) {
            $null = Get-SPComponent ([string]$file.path)
            if ([string]$file.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw '旧安装状态校验值无效。' }
        }
        if ($record.previousState.backupRelative -and [string]$record.previousState.backupRelative -notmatch '^SpacePhone-installer/backups/[a-zA-Z0-9-]+$') { throw '旧安装状态备份位置无效。' }
    }
    return [pscustomobject]@{ relative=$Relative; path=$backup; snapshot=$record }
}
function Restore-SPBackup([string]$GamePath,$Backup) {
    Assert-SPTargetLayout $GamePath $Backup.snapshot.files
    foreach ($file in @($Backup.snapshot.files)) {
        $target = Join-SPSafe $GamePath ([string]$file.path)
        if ($file.existed) {
            $source = Join-SPSafe $Backup.path ([string]$file.backupFile)
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            Copy-Item -LiteralPath $source -Destination $target -Force
            if ((Get-SPHash $target) -ne [string]$file.sha256) { throw "恢复后的文件校验失败：$($file.path)" }
        } elseif (Test-Path -LiteralPath $target -PathType Leaf) { Remove-Item -LiteralPath $target -Force }
    }
    $statePath = Join-SPSafe $GamePath 'SpacePhone-installer/state.json'
    if ($Backup.snapshot.previousState) { Write-SPJson $statePath $Backup.snapshot.previousState }
    elseif (Test-Path -LiteralPath $statePath -PathType Leaf) { Remove-Item -LiteralPath $statePath -Force }
}
function Assert-SPFixture([string]$GamePath,[bool]$FixtureMode,[int]$FailAfter) {
    if ($FailAfter -gt 0 -and -not $FixtureMode) { throw '模拟失败仅允许在独立测试目录使用。' }
    if ($FixtureMode) {
        $marker = Join-SPSafe $GamePath '.spacephone-test-fixture'
        if (-not (Test-Path -LiteralPath $marker -PathType Leaf) -or (Get-Content -LiteralPath $marker -Raw).Trim() -cne 'SPACEPHONE-ISOLATED-TEST-NO-GAME-SAVES') { throw '测试模式只允许明确标记的独立 fixture 目录。' }
        $root = Get-SPFullPath $GamePath
        $realRoot = Get-SPFullPath 'C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen Demo'
        if ($root -ieq $realRoot -or $root.StartsWith(($realRoot+'\'),[StringComparison]::OrdinalIgnoreCase)) { throw '测试禁止指向原游戏目录。' }
    }
}
function Invoke-SPInstallInternal([string]$GamePath,[string]$PackageRoot,[bool]$FixtureMode=$false,[int]$FailAfterFiles=0,[scriptblock]$Progress) {
    $root = Assert-SPGame $GamePath
    Assert-SPFixture $root $FixtureMode $FailAfterFiles
    Assert-SPNotRunning $root
    if ($Progress) { & $Progress '校验安装文件 / Verify payload' 12 }
    $manifest = Read-SPManifest $PackageRoot
    Assert-SPTargetLayout $root $manifest.files
    $previous = Get-SPState $root
    $inventory = @(Get-SPInventory $root $manifest.components)
    $desired = @{}
    foreach ($file in $manifest.files) { $desired[$file.path] = $file }
    $same = $true
    foreach ($file in $manifest.files) {
        $target = Join-SPSafe $root $file.path
        if (-not (Test-Path -LiteralPath $target -PathType Leaf) -or (Get-SPHash $target) -ne $file.sha256) { $same=$false; break }
    }
    if ($same) { foreach ($rel in $inventory) { if (-not $desired.ContainsKey($rel)) { $same=$false; break } } }
    if ($same -and $previous) { return [pscustomobject]@{ status='AlreadyInstalled'; message='这些文件已经是同一版本，无需重复安装，也没有创建新备份。'; version=$manifest.version; fileCount=$manifest.files.Count; backupPath=$null } }
    $paths = @($inventory) + @($manifest.files | ForEach-Object { $_.path })
    if ($Progress) { & $Progress '备份当前组件 / Back up current components' 25 }
    $backup = New-SPBackup $root $paths $previous 'Install'
    $stageRelative = 'SpacePhone-installer/staging/' + [Guid]::NewGuid().ToString('N')
    $stage = Join-SPSafe $root $stageRelative
    [IO.Directory]::CreateDirectory($stage) | Out-Null
    $touched = $false
    $stageFiles=@{}
    try {
        $stagedCount=0
        foreach ($file in $manifest.files) {
            $source = Join-SPSafe $manifest.payload $file.path
            $stagedCount++
            $staged = Join-SPSafe $stage (('{0:D5}' -f $stagedCount)+'.bin')
            $stageFiles[$file.path]=$staged
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($staged)) | Out-Null
            Copy-Item -LiteralPath $source -Destination $staged
            if ((Get-SPHash $staged) -ne $file.sha256) { throw '临时安装文件校验失败。' }
        }
        Assert-SPNotRunning $root
        $i=0
        $touched=$true
        foreach ($rel in $inventory) {
            if (-not $desired.ContainsKey($rel)) { Remove-Item -LiteralPath (Join-SPSafe $root $rel) -Force }
        }
        foreach ($file in $manifest.files) {
            $target = Join-SPSafe $root $file.path
            [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
            Copy-Item -LiteralPath $stageFiles[$file.path] -Destination $target -Force
            if ((Get-SPHash $target) -ne $file.sha256) { throw "安装后校验失败：$($file.path)" }
            $i++
            if ($Progress) { & $Progress '安装并校验 / Install and verify' (35+[int](60*$i/$manifest.files.Count)) }
            if ($FailAfterFiles -gt 0 -and $i -ge $FailAfterFiles) { throw 'TEST: Simulated write failure' }
        }
        # Keep ownership of earlier optional components absent from this package.
        $managed = @($manifest.files)
        if ($previous) {
            foreach ($file in @($previous.managedFiles)) {
                $component = Get-SPComponent ([string]$file.path)
                if ($manifest.components -notcontains $component) { $managed += $file }
            }
        }
        $state = [ordered]@{ schemaVersion=1; modId='SpacePhone'; version=$manifest.version; gamePath=$root; installedUtc=[DateTime]::UtcNow.ToString('o'); manifestSha256=$manifest.manifestSha256; backupRelative=$backup.relative; managedFiles=$managed }
        Write-SPJson (Join-SPSafe $root 'SpacePhone-installer/state.json') $state
        if ($Progress) { & $Progress '安装完成 / Completed' 100 }
        $message=if ($same) { '已备份并接管现有相同版本，文件内容不变。现在可使用此工具回退或移除。' } else { '安装完成。正常启动游戏即可使用；保留此安装器可回退或移除。' }
        return [pscustomobject]@{ status='Installed'; message=$message; version=$manifest.version; fileCount=$manifest.files.Count; backupPath=$backup.path }
    } catch {
        $failure = $_.Exception.Message
        if ($touched) {
            try { Restore-SPBackup $root (Read-SPBackup $root $backup.relative) }
            catch { throw "安装失败：$failure。自动恢复未完成：$($_.Exception.Message)。备份位于 $($backup.path)；请先不要启动游戏。" }
            throw "安装失败，已自动恢复安装前文件：$failure"
        }
        throw "安装尚未改动游戏文件：$failure"
    } finally {
        # Checked absolute path under this game's installer staging; native PS only.
        if (Test-Path -LiteralPath $stage) {
            Assert-SPWithin (Join-SPSafe $root 'SpacePhone-installer/staging') $stage
            Assert-SPNoReparse $stage
            Remove-Item -LiteralPath $stage -Recurse -Force
        }
    }
}
function Invoke-SPRollbackInternal([string]$GamePath,[scriptblock]$Progress) {
    $root = Assert-SPGame $GamePath
    Assert-SPNotRunning $root
    $state = Get-SPState $root
    if (-not $state -or -not $state.backupRelative) { throw '没有可回退的安装记录。旧的手工安装备份请按随包教程恢复。' }
    $backup = Read-SPBackup $root ([string]$state.backupRelative)
    Assert-SPTargetLayout $root $backup.snapshot.files
    $paths = @($backup.snapshot.files | ForEach-Object { [string]$_.path })
    # Backup the current version first, so a failed restore can itself be undone.
    $safety = New-SPBackup $root $paths $state 'RollbackSafety'
    Assert-SPNotRunning $root
    try {
        if ($Progress) { & $Progress '恢复上一次安装 / Restore previous installation' 40 }
        Restore-SPBackup $root $backup
        if ($Progress) { & $Progress '回退完成 / Restored' 100 }
        return [pscustomobject]@{ status='RolledBack'; message='已恢复本工具上一次安装前的组件。存档、音乐和登录目录保留。'; backupPath=$safety.path }
    } catch {
        $failure=$_.Exception.Message
        try { Restore-SPBackup $root (Read-SPBackup $root $safety.relative) }
        catch { throw "回退失败：$failure。安全恢复也未完成；请使用备份 $($safety.path)。" }
        throw "回退失败，当前版本已恢复：$failure"
    }
}
function Invoke-SPUninstallInternal([string]$GamePath,[scriptblock]$Progress) {
    $root = Assert-SPGame $GamePath
    Assert-SPNotRunning $root
    $state = Get-SPState $root
    if (-not $state) { throw '此安装器没有管理过这个游戏目录，不能替你删除手工安装的 Mod。请按随包教程手动移除。' }
    $paths=@($state.managedFiles | ForEach-Object { [string]$_.path })
    $backup = New-SPBackup $root $paths $state 'Uninstall'
    Assert-SPNotRunning $root
    try {
        $i=0
        foreach ($rel in $paths) {
            $p = Join-SPSafe $root $rel
            if (Test-Path -LiteralPath $p -PathType Leaf) { Remove-Item -LiteralPath $p -Force }
            $i++
            if ($Progress) { & $Progress '移除本工具安装文件 / Remove managed files' ([int](90*$i/[Math]::Max(1,$paths.Count))) }
        }
        $statePath = Join-SPSafe $root 'SpacePhone-installer/state.json'
        Remove-Item -LiteralPath $statePath -Force
        if ($Progress) { & $Progress '移除完成 / Removed' 100 }
        return [pscustomobject]@{ status='Uninstalled'; message='本工具管理的文件已移除；存档、个人音乐、网页登录资料及其他 Mod 保留。'; backupPath=$backup.path }
    } catch {
        $failure=$_.Exception.Message
        try { Restore-SPBackup $root (Read-SPBackup $root $backup.relative) }
        catch { throw "移除失败：$failure；安全恢复未完成，请使用备份 $($backup.path)。" }
        throw "移除失败，原版本已经恢复：$failure"
    }
}
function Invoke-SPExclusive([string]$GamePath,[scriptblock]$Body) {
    $root=(Get-SPFullPath $GamePath).ToLowerInvariant()
    $sha=[Security.Cryptography.SHA256]::Create()
    try { $digest=[BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($root))).Replace('-','') } finally { $sha.Dispose() }
    $mutex=New-Object Threading.Mutex($false,('Local\SpacePhoneInstaller-'+$digest))
    $owned=$false
    try {
        try { $owned=$mutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $owned=$true }
        if (-not $owned) { throw '另一个 SpacePhone 安装器正在操作此目录。请等待它完成再重试。' }
        & $Body
    } finally { if ($owned) { $mutex.ReleaseMutex() }; $mutex.Dispose() }
}
function Invoke-SPInstall([string]$GamePath,[string]$PackageRoot,[bool]$FixtureMode=$false,[int]$FailAfterFiles=0,[scriptblock]$Progress) {
    Invoke-SPExclusive $GamePath { Invoke-SPInstallInternal $GamePath $PackageRoot $FixtureMode $FailAfterFiles $Progress }
}
function Invoke-SPRollback([string]$GamePath,[scriptblock]$Progress) {
    Invoke-SPExclusive $GamePath { Invoke-SPRollbackInternal $GamePath $Progress }
}
function Invoke-SPUninstall([string]$GamePath,[scriptblock]$Progress) {
    Invoke-SPExclusive $GamePath { Invoke-SPUninstallInternal $GamePath $Progress }
}
function Find-SPGameFolders {
    $steamPaths=@('C:\Program Files (x86)\Steam','C:\Program Files\Steam')
    foreach ($key in @('HKCU:\Software\Valve\Steam','HKLM:\SOFTWARE\WOW6432Node\Valve\Steam')) {
        try { $v=Get-ItemProperty -LiteralPath $key; if ($v.PSObject.Properties['SteamPath']) { $steamPaths += [string]$v.SteamPath }; if ($v.PSObject.Properties['InstallPath']) { $steamPaths += [string]$v.InstallPath } } catch { }
    }
    $libraries=@($steamPaths)
    foreach ($steam in @($steamPaths | Select-Object -Unique)) {
        $vdf=Join-Path $steam 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf -PathType Leaf) {
            try {
                $raw=Get-Content -LiteralPath $vdf -Raw
                foreach ($m in [regex]::Matches($raw,'"path"\s+"([^"]+)"')) { $libraries += $m.Groups[1].Value.Replace('\\','\') }
            } catch { }
        }
    }
    $found=@()
    foreach ($library in @($libraries | Select-Object -Unique)) {
        foreach ($name in @('Probably Stolen Demo','Probably Stolen')) {
            $p=Join-Path $library ('steamapps\common\'+$name)
            if (Test-Path -LiteralPath (Join-Path $p 'Probably Stolen.exe') -PathType Leaf) { $found += $p }
        }
    }
    return @($found | Select-Object -Unique)
}

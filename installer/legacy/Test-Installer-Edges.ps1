param([string]$CorePackage)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'Installer.Core.ps1')
if (-not $CorePackage) { $CorePackage=Join-Path (Split-Path $PSScriptRoot -Parent) 'spacephone-publishing\installer-core' }
$root=Join-Path $PSScriptRoot ('test-artifacts\edges-'+[Guid]::NewGuid().ToString('N').Substring(0,8))
[IO.Directory]::CreateDirectory($root) | Out-Null
$checks=New-Object 'Collections.Generic.List[object]'
$utf8=New-Object Text.UTF8Encoding($false)
function Write-EdgeFile([string]$Base,[string]$Rel,[string]$Value) {
    $p=Join-SPSafe $Base $Rel
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p)) | Out-Null
    [IO.File]::WriteAllText($p,$Value,$utf8)
}
function New-EdgeGame([string]$Name) {
    $p=Join-SPSafe $root $Name
    [IO.Directory]::CreateDirectory($p) | Out-Null
    foreach ($rel in @('Probably Stolen.exe','GameAssembly.dll','UnityPlayer.dll','winhttp.dll','BepInEx/core/BepInEx.Core.dll','BepInEx/core/BepInEx.Unity.IL2CPP.dll')) { Write-EdgeFile $p $rel 'FAKE-FIXTURE-NEVER-EXECUTE' }
    [IO.Directory]::CreateDirectory((Join-SPSafe $p 'Probably Stolen_Data')) | Out-Null
    Write-EdgeFile $p '.spacephone-test-fixture' 'SPACEPHONE-ISOLATED-TEST-NO-GAME-SAVES'
    return $p
}
function Check-Edge([string]$Name,[bool]$Pass,[string]$Detail='') {
    $checks.Add([pscustomobject]@{name=$Name;pass=$Pass;detail=$Detail})
    if (-not $Pass) { throw "EDGE FAILED: $Name $Detail" }
}
function Throw-Edge([string]$Name,[scriptblock]$Body,[string]$Pattern) {
    $message=''; try { & $Body | Out-Null } catch { $message=$_.Exception.Message }
    Check-Edge $Name ([bool]($message -match $Pattern)) $message
}
try {
    $manifest=Read-SPManifest $CorePackage
    $manual=New-EdgeGame 'manual-same'
    foreach ($f in $manifest.files) {
        $target=Join-SPSafe $manual $f.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        Copy-Item -LiteralPath (Join-SPSafe $manifest.payload $f.path) -Destination $target
    }
    Check-Edge 'Manual matching installation initially unowned' ($null -eq (Get-SPState $manual))
    $result=Invoke-SPInstall $manual $CorePackage $true
    Check-Edge 'Manual matching installation is backed up and adopted' ($result.status -eq 'Installed' -and [bool](Get-SPState $manual) -and [bool]$result.backupPath)
    $backups=@(Get-ChildItem -LiteralPath (Join-SPSafe $manual 'SpacePhone-installer/backups') -Directory).Count
    $result=Invoke-SPInstall $manual $CorePackage $true
    Check-Edge 'Adopted matching installation then no-ops' ($result.status -eq 'AlreadyInstalled' -and @(Get-ChildItem -LiteralPath (Join-SPSafe $manual 'SpacePhone-installer/backups') -Directory).Count -eq $backups)
    $result=Invoke-SPRollback $manual
    Check-Edge 'Adoption rollback restores unowned manual installation' ($result.status -eq 'RolledBack' -and $null -eq (Get-SPState $manual) -and (Get-SPHash (Join-SPSafe $manual 'BepInEx/plugins/SpacePhone/SpacePhone.dll')) -eq @($manifest.files | Where-Object {$_.path -like '*/SpacePhone.dll'})[0].sha256)
    $conflict=New-EdgeGame 'rollback-directory'
    Write-EdgeFile $conflict 'BepInEx/plugins/SpacePhone/SpacePhone.dll' 'OLD-DLL'
    Write-EdgeFile $conflict 'BepInEx/plugins/SpacePhone/Assets/dialer.png' 'OLD-ASSET'
    $null=Invoke-SPInstall $conflict $CorePackage $true
    $dllHash=Get-SPHash (Join-SPSafe $conflict 'BepInEx/plugins/SpacePhone/SpacePhone.dll')
    $stateHash=Get-SPHash (Join-SPSafe $conflict 'SpacePhone-installer/state.json')
    $asset=Join-SPSafe $conflict 'BepInEx/plugins/SpacePhone/Assets/dialer.png'
    Remove-Item -LiteralPath $asset -Force
    [IO.Directory]::CreateDirectory($asset) | Out-Null
    Write-EdgeFile $asset 'sentinel.txt' 'DIRECTORY-SENTINEL'
    $backupCount=@(Get-ChildItem -LiteralPath (Join-SPSafe $conflict 'SpacePhone-installer/backups') -Directory).Count
    Throw-Edge 'Rollback rejects user-created directory before any writes' { Invoke-SPRollback $conflict } '位置已经是文件夹'
    Check-Edge 'Rejected rollback preserves live DLL and state' ((Get-SPHash (Join-SPSafe $conflict 'BepInEx/plugins/SpacePhone/SpacePhone.dll')) -eq $dllHash -and (Get-SPHash (Join-SPSafe $conflict 'SpacePhone-installer/state.json')) -eq $stateHash)
    Check-Edge 'Rejected rollback preserves directory sentinel' ((Get-Content -LiteralPath (Join-SPSafe $asset 'sentinel.txt') -Raw) -eq 'DIRECTORY-SENTINEL')
    Check-Edge 'Rejected rollback creates no safety backup' (@(Get-ChildItem -LiteralPath (Join-SPSafe $conflict 'SpacePhone-installer/backups') -Directory).Count -eq $backupCount)
    $spHostGame=New-EdgeGame 'running-host'
    [IO.Directory]::CreateDirectory((Join-SPSafe $spHostGame 'BepInEx/plugins/SpacePhone/BrowserHost')) | Out-Null
    $helper=Join-SPSafe $spHostGame 'BepInEx/plugins/SpacePhone/BrowserHost/SpacePhone.WebViewHost.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ping.exe') -Destination $helper
    $process=Start-Process -FilePath $helper -ArgumentList @('-t','127.0.0.1') -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Milliseconds 200
        Throw-Edge 'Running optional web host blocks Core writes' { Invoke-SPInstall $spHostGame $CorePackage $true } '仍在运行'
        Check-Edge 'Running host rejection creates no metadata' (-not (Test-Path -LiteralPath (Join-SPSafe $spHostGame 'SpacePhone-installer')))
        # Supplying a relative or normalized alias cannot bypass name-based guard.
        Throw-Edge 'Normalized relative alias cannot bypass process guard' { Invoke-SPInstall (Join-Path $spHostGame '..\running-host') $CorePackage $true } '仍在运行'
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
        [IO.File]::WriteAllText($helper,'FAKE-FIXTURE-NEVER-EXECUTE',$utf8)
    }
    $report=[ordered]@{runtime=$PSVersionTable.PSVersion.ToString();testRoot=$root;realGameWritten=$false;realGameExecuted=$false;pass=$checks.Count;fail=0;checks=@($checks.ToArray())}
    Write-SPJson (Join-Path $PSScriptRoot 'installer-edge-report.json') $report
    Write-Output ("EDGE PASS "+$checks.Count+" / FAIL 0")
} catch {
    $report=[ordered]@{runtime=$PSVersionTable.PSVersion.ToString();testRoot=$root;realGameWritten=$false;pass=@($checks|Where-Object {$_.pass}).Count;fail=1;error=$_.Exception.Message;stack=$_.ScriptStackTrace;checks=@($checks.ToArray())}
    Write-SPJson (Join-Path $PSScriptRoot 'installer-edge-report.json') $report
    Write-Output ($report|ConvertTo-Json -Depth 8)
    exit 1
}

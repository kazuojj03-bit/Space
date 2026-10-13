[CmdletBinding()]
param(
    [string]$CorePackage,
    [string]$FullPackage
)
$ErrorActionPreference='Stop'
if (-not $CorePackage) { $CorePackage=Join-Path (Split-Path $PSScriptRoot -Parent) 'spacephone-publishing\installer-core' }
if (-not $FullPackage) { $FullPackage=Join-Path (Split-Path $PSScriptRoot -Parent) 'spacephone-publishing\installer-full' }
. (Join-Path $PSScriptRoot 'Installer.Core.ps1')
$testRoot=Join-Path $PSScriptRoot ('test-artifacts\'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$checks=New-Object 'Collections.Generic.List[object]'
$utf8=New-Object Text.UTF8Encoding($false)
function Write-TestFile([string]$Root,[string]$Relative,[string]$Value) {
    $p=Join-SPSafe $Root $Relative
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p)) | Out-Null
    [IO.File]::WriteAllText($p,$Value,$utf8)
    return $p
}
function New-TestGame([string]$Name) {
    $p=Join-SPSafe $testRoot $Name
    [IO.Directory]::CreateDirectory($p) | Out-Null
    foreach ($rel in @('Probably Stolen.exe','GameAssembly.dll','UnityPlayer.dll','winhttp.dll','BepInEx/core/BepInEx.Core.dll','BepInEx/core/BepInEx.Unity.IL2CPP.dll')) { $null=Write-TestFile $p $rel 'FAKE-FIXTURE-NEVER-EXECUTE' }
    [IO.Directory]::CreateDirectory((Join-SPSafe $p 'Probably Stolen_Data')) | Out-Null
    $null=Write-TestFile $p '.spacephone-test-fixture' 'SPACEPHONE-ISOLATED-TEST-NO-GAME-SAVES'
    $null=Write-TestFile $p 'BepInEx/plugins/SpacePhone/SpacePhone.dll' 'PRIOR-MOD-DLL'
    $null=Write-TestFile $p 'BepInEx/plugins/SpacePhone/Assets/old-only.png' 'PRIOR-MOD-ASSET'
    $null=Write-TestFile $p 'BepInEx/plugins/SpacePhone/BrowserHost/old-host.txt' 'EXISTING-OPTIONAL-HOST'
    $null=Write-TestFile $p 'BepInEx/plugins/SpacePhone/Music/personal.wav' 'USER-MUSIC-SENTINEL'
    $null=Write-TestFile $p 'BepInEx/plugins/SpacePhone/WebProfiles/Default/account-sentinel.txt' 'TEST-SENTINEL-NOT-A-REAL-ACCOUNT'
    $null=Write-TestFile $p 'BepInEx/plugins/OtherMod/Other.dll' 'OTHER-MOD-SENTINEL'
    $null=Write-TestFile $p 'Probably Stolen_Data/save-sentinel.txt' 'NOT-A-REAL-GAME-SAVE'
    return $p
}
function Assert-Test([string]$Name,[bool]$Condition,[string]$Detail='') {
    $checks.Add([pscustomobject]@{name=$Name;pass=$Condition;detail=$Detail})
    if (-not $Condition) { throw "TEST FAILED: $Name $Detail" }
}
function Assert-Throws([string]$Name,[scriptblock]$Body,[string]$MessagePattern='') {
    $threw=$false; $msg=''
    try { & $Body | Out-Null } catch { $threw=$true; $msg=$_.Exception.Message }
    Assert-Test $Name ($threw -and (-not $MessagePattern -or $msg -match $MessagePattern)) $msg
}
function Get-ProtectedHashes([string]$Game) {
    $result=@{}
    foreach ($rel in @('BepInEx/plugins/SpacePhone/Music/personal.wav','BepInEx/plugins/SpacePhone/WebProfiles/Default/account-sentinel.txt','BepInEx/plugins/OtherMod/Other.dll','Probably Stolen_Data/save-sentinel.txt','GameAssembly.dll','UnityPlayer.dll','winhttp.dll','BepInEx/core/BepInEx.Core.dll','BepInEx/core/BepInEx.Unity.IL2CPP.dll')) { $result[$rel]=Get-SPHash (Join-SPSafe $Game $rel) }
    return $result
}
function Assert-Protected([string]$Name,[string]$Game,$Original) {
    $same=$true
    foreach ($rel in $Original.Keys) { if ((Get-SPHash (Join-SPSafe $Game $rel)) -ne $Original[$rel]) { $same=$false } }
    Assert-Test $Name $same 'Music, WebProfiles, other Mod, dummy save, game and loader sentinels'
}
function Get-ModHashes([string]$Game) {
    $result=@{}
    foreach ($rel in @(Get-SPInventory $Game @('Plugin','Assets','BrowserHost','Document:README.txt','Document:安装教程.html','Document:更新记录.txt','Document:开发说明.txt','Document:功能验证记录.txt','Document:THIRD-PARTY-NOTICES.txt'))) { $result[$rel]=Get-SPHash (Join-SPSafe $Game $rel) }
    return $result
}
function Assert-SameMap([string]$Name,$A,$B) {
    $same=$A.Count -eq $B.Count
    foreach ($k in $A.Keys) { if (-not $B.ContainsKey($k) -or $B[$k] -ne $A[$k]) { $same=$false } }
    Assert-Test $Name $same
}
function New-Variant([string]$Name,[string]$Source) {
    $target=Join-SPSafe $testRoot $Name
    [IO.Directory]::CreateDirectory($target) | Out-Null
    Copy-Item -LiteralPath (Join-Path $Source 'payload') -Destination $target -Recurse
    Copy-Item -LiteralPath (Join-Path $Source 'manifest.json') -Destination (Join-Path $target 'manifest.json')
    return $target
}
try {
    $mCore=Read-SPManifest $CorePackage
    $mFull=Read-SPManifest $FullPackage
    Assert-Test 'Verified Core actual release payload' ($mCore.files.Count -gt 20 -and $mCore.components -notcontains 'BrowserHost')
    Assert-Test 'Verified Full actual release payload' ($mFull.components -contains 'BrowserHost' -and $mFull.files.Count -gt $mCore.files.Count)
    $game=New-TestGame 'fresh-game'
    $protected=Get-ProtectedHashes $game
    $before=Get-ModHashes $game
    $result=Invoke-SPInstall $game $CorePackage $true
    Assert-Test 'Core installs successfully' ($result.status -eq 'Installed')
    Assert-Test 'Core preserves preexisting BrowserHost' ((Get-Content -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/old-host.txt') -Raw) -eq 'EXISTING-OPTIONAL-HOST')
    Assert-Test 'Core cleans obsolete managed asset only' (-not (Test-Path -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/Assets/old-only.png')))
    foreach ($file in $mCore.files) { Assert-Test ('Core installed SHA256: '+$file.path) ((Get-SPHash (Join-SPSafe $game $file.path)) -eq $file.sha256) }
    Assert-Protected 'Protected user/game/loader files preserved after Core' $game $protected
    $stateText=Get-Content -LiteralPath (Join-SPSafe $game 'SpacePhone-installer/state.json') -Raw
    $backupCount=@(Get-ChildItem -LiteralPath (Join-SPSafe $game 'SpacePhone-installer/backups') -Directory).Count
    $result=Invoke-SPInstall $game $CorePackage $true
    Assert-Test 'Identical reinstall is a no-op' ($result.status -eq 'AlreadyInstalled')
    Assert-Test 'Identical reinstall creates no extra backup' (@(Get-ChildItem -LiteralPath (Join-SPSafe $game 'SpacePhone-installer/backups') -Directory).Count -eq $backupCount)
    Assert-Test 'Identical reinstall does not rewrite state' ((Get-Content -LiteralPath (Join-SPSafe $game 'SpacePhone-installer/state.json') -Raw) -eq $stateText)
    $result=Invoke-SPInstall $game $FullPackage $true
    Assert-Test 'Optional Full update succeeds' ($result.status -eq 'Installed')
    Assert-Test 'Full removes obsolete own BrowserHost file' (-not (Test-Path -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/old-host.txt')))
    Assert-Test 'Actual production host exists' (Test-Path -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/SpacePhone.WebViewHost.exe'))
    Assert-Protected 'Protected user/game/loader files preserved after Full' $game $protected
    $variant=New-Variant 'core-update' $CorePackage
    $manifestPath=Join-Path $variant 'manifest.json'
    $v=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $v.version='3.1-test-update'
    $readme=Join-SPSafe $variant 'payload/BepInEx/plugins/SpacePhone/README.txt'
    [IO.File]::AppendAllText($readme,"`r`nTEST-ONLY-UPDATE",$utf8)
    foreach ($f in $v.files) { if ($f.path -eq 'BepInEx/plugins/SpacePhone/README.txt') { $f.sha256=Get-SPHash $readme; $f.size=(Get-Item -LiteralPath $readme).Length } }
    Write-SPJson $manifestPath $v
    $hostHash=Get-SPHash (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/SpacePhone.WebViewHost.exe')
    $fullState=Get-SPState $game
    $result=Invoke-SPInstall $game $variant $true
    Assert-Test 'New Core update succeeds on Full installation' ($result.status -eq 'Installed')
    Assert-Test 'Core update leaves installed optional host hash unchanged' ((Get-SPHash (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/SpacePhone.WebViewHost.exe')) -eq $hostHash)
    $newState=Get-SPState $game
    Assert-Test 'Core update preserves earlier optional component ownership' (@($newState.managedFiles | Where-Object { $_.path -like '*/BrowserHost/*' }).Count -eq @($fullState.managedFiles | Where-Object { $_.path -like '*/BrowserHost/*' }).Count)
    $result=Invoke-SPRollback $game
    Assert-Test 'Core rollback succeeds' ($result.status -eq 'RolledBack' -and (Get-SPState $game).version -eq '3.1')
    Assert-Test 'Core rollback leaves optional host unchanged' ((Get-SPHash (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/SpacePhone.WebViewHost.exe')) -eq $hostHash)
    $result=Invoke-SPRollback $game
    Assert-Test 'Full rollback restores earlier Core install' ($result.status -eq 'RolledBack' -and (Get-SPState $game).managedFiles.Count -eq $mCore.files.Count)
    Assert-Test 'Full rollback restores preexisting optional files' (Test-Path -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/old-host.txt'))
    $result=Invoke-SPUninstall $game
    Assert-Test 'Core uninstall removes owned plugin' ($result.status -eq 'Uninstalled' -and -not (Test-Path -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/SpacePhone.dll')))
    Assert-Test 'Core uninstall preserves unowned optional files' (Test-Path -LiteralPath (Join-SPSafe $game 'BepInEx/plugins/SpacePhone/BrowserHost/old-host.txt'))
    Assert-Protected 'Protected user/game/loader files preserved after rollback+uninstall' $game $protected
    Assert-Throws 'Uninstall refuses unowned/manual directory' { Invoke-SPUninstall $game } '没有管理'
    Assert-Throws 'Rollback refuses absent ownership' { Invoke-SPRollback $game } '没有可回退'
    $failed=New-TestGame 'failed-install'
    $failedBefore=Get-ModHashes $failed
    Assert-Throws 'Mid-copy failure triggers automatic recovery' { Invoke-SPInstall $failed $FullPackage $true 2 } '已自动恢复'
    Assert-SameMap 'Mid-copy failure restores every managed file exactly' $failedBefore (Get-ModHashes $failed)
    Assert-Test 'First failed install leaves no ownership state' ($null -eq (Get-SPState $failed))
    Assert-Test 'Staging directory cleared after failure' (@(Get-ChildItem -LiteralPath (Join-SPSafe $failed 'SpacePhone-installer/staging') -Force).Count -eq 0)
    $upfailed=New-TestGame 'failed-update'
    $null=Invoke-SPInstall $upfailed $CorePackage $true
    $upBefore=Get-ModHashes $upfailed
    $upState=Get-Content -LiteralPath (Join-SPSafe $upfailed 'SpacePhone-installer/state.json') -Raw
    Assert-Throws 'Failed update recovers prior version' { Invoke-SPInstall $upfailed $FullPackage $true 1 } '已自动恢复'
    Assert-SameMap 'Failed update restores all managed content' $upBefore (Get-ModHashes $upfailed)
    Assert-Test 'Failed update restores exact previous ownership state' ((Get-Content -LiteralPath (Join-SPSafe $upfailed 'SpacePhone-installer/state.json') -Raw) -eq $upState)
    $tampered=New-Variant 'tampered-source' $CorePackage
    [IO.File]::AppendAllText((Join-SPSafe $tampered 'payload/BepInEx/plugins/SpacePhone/README.txt'),'CORRUPT',$utf8)
    $tamperGame=New-TestGame 'tamper-game'
    Assert-Throws 'Payload corruption rejected before installation' { Invoke-SPInstall $tamperGame $tampered $true } '校验失败'
    Assert-Test 'Corrupt source creates no backup or state' (-not (Test-Path -LiteralPath (Join-SPSafe $tamperGame 'SpacePhone-installer')))
    $pathVariant=New-Variant 'unsafe-manifest' $CorePackage
    $unsafeManifest=Get-Content -LiteralPath (Join-Path $pathVariant 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $unsafeManifest.files[0].path='BepInEx/plugins/SpacePhone/BrowserHost/../WebProfiles/account.txt'
    Write-SPJson (Join-Path $pathVariant 'manifest.json') $unsafeManifest
    Assert-Throws 'Traversal into WebProfiles rejected' { Read-SPManifest $pathVariant } '不安全'
    $unsafeManifest.files[0].path='BepInEx/plugins/OtherMod/Other.dll'
    Write-SPJson (Join-Path $pathVariant 'manifest.json') $unsafeManifest
    Assert-Throws 'Other Mod destination rejected' { Read-SPManifest $pathVariant } '不是 SpacePhone'
    $unsafeManifest.files[0].path='BepInEx/plugins/SpacePhone/Music/user.mp3'
    Write-SPJson (Join-Path $pathVariant 'manifest.json') $unsafeManifest
    Assert-Throws 'Personal Music destination rejected' { Read-SPManifest $pathVariant } '不会管理'
    $missing=New-TestGame 'missing-loader'
    Remove-Item -LiteralPath (Join-SPSafe $missing 'BepInEx/core/BepInEx.Unity.IL2CPP.dll') -Force
    Assert-Throws 'Missing IL2CPP loader rejected' { Invoke-SPInstall $missing $CorePackage $true } '需要先安装'
    Assert-Throws 'Wrong root rejected' { Assert-SPGame $testRoot } '请选择包含'
    Assert-Throws 'Disk root rejected' { Get-SPFullPath 'C:\' } '磁盘根目录'
    Assert-Throws 'Failure injection disallowed without fixture' { Invoke-SPInstall (New-TestGame 'no-fixture-injection') $CorePackage $false 1 } '仅允许'
    $junctionGame=New-TestGame 'junction-game'
    $linked=Join-SPSafe $junctionGame 'BepInEx/plugins/SpacePhone/Assets/link'
    $outside=Join-SPSafe $testRoot 'junction-target'
    [IO.Directory]::CreateDirectory($outside) | Out-Null
    $null=Write-TestFile $outside 'sentinel.txt' 'LINK-TARGET-MUST-SURVIVE'
    New-Item -ItemType Junction -Path $linked -Target $outside | Out-Null
    try { Assert-Throws 'Existing nested reparse point rejected' { Invoke-SPInstall $junctionGame $CorePackage $true } '不支持符号链接' }
    finally {
        # Windows PowerShell 5.1 Remove-Item has a junction deletion bug on this OS.
        # Delete the verified junction itself without recursion; do not touch its target.
        Assert-SPWithin $junctionGame $linked
        [IO.Directory]::Delete($linked)
    }
    Assert-Test 'Junction target sentinel intact' ((Get-Content -LiteralPath (Join-SPSafe $outside 'sentinel.txt') -Raw) -eq 'LINK-TARGET-MUST-SURVIVE')
    $fileDirectory=New-TestGame 'file-directory-conflict'
    $assetFile=@($mCore.files | Where-Object { $_.path -like '*/Assets/*' })[0]
    $conflictDirectory=Join-SPSafe $fileDirectory $assetFile.path
    [IO.Directory]::CreateDirectory($conflictDirectory) | Out-Null
    $conflictBefore=Get-ModHashes $fileDirectory
    Assert-Throws 'Target file occupied by directory rejected before writes' { Invoke-SPInstall $fileDirectory $CorePackage $true } '位置已经是文件夹'
    Assert-SameMap 'Target directory conflict leaves all mod files unchanged' $conflictBefore (Get-ModHashes $fileDirectory)
    Assert-Test 'Target directory conflict creates no backup' (-not (Test-Path -LiteralPath (Join-SPSafe $fileDirectory 'SpacePhone-installer')))
    $parentFile=New-TestGame 'parent-file-conflict'
    $hostFile=@($mFull.files | Where-Object { $_.path -match '/BrowserHost/[^/]+/[^/]+$' })[0]
    $hostParent=[IO.Path]::GetDirectoryName([string]$hostFile.path).Replace('\','/')
    $null=Write-TestFile $parentFile $hostParent 'EXISTING-PARENT-FILE'
    $parentBefore=Get-ModHashes $parentFile
    Assert-Throws 'Target parent occupied by file rejected before writes' { Invoke-SPInstall $parentFile $FullPackage $true } '父路径被文件占用'
    Assert-SameMap 'Parent file conflict leaves all mod files unchanged' $parentBefore (Get-ModHashes $parentFile)
    Assert-Test 'Parent file conflict creates no backup' (-not (Test-Path -LiteralPath (Join-SPSafe $parentFile 'SpacePhone-installer')))
    $badbackup=New-TestGame 'damaged-backup'
    $null=Invoke-SPInstall $badbackup $CorePackage $true
    $bbState=Get-SPState $badbackup
    $bbBackup=Read-SPBackup $badbackup $bbState.backupRelative
    $bbFile=@($bbBackup.snapshot.files | Where-Object { $_.path -eq 'BepInEx/plugins/SpacePhone/SpacePhone.dll' })[0]
    $bbCopy=Join-SPSafe $bbBackup.path $bbFile.backupFile
    [IO.File]::AppendAllText($bbCopy,'BACKUP-CORRUPTION',$utf8)
    $bbBefore=Get-ModHashes $badbackup
    Assert-Throws 'Damaged backup rejected before rollback mutation' { Invoke-SPRollback $badbackup } '备份文件损坏'
    Assert-SameMap 'Damaged backup leaves live mod untouched' $bbBefore (Get-ModHashes $badbackup)
    # Launch only a copied Windows ping helper, never a game or the real BrowserHost.
    $running=New-TestGame 'running-game'
    $helper=Join-SPSafe $running 'Probably Stolen.exe'
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ping.exe') -Destination $helper -Force
    $process=Start-Process -FilePath $helper -ArgumentList @('-t','127.0.0.1') -WindowStyle Hidden -PassThru
    try {
        Start-Sleep -Milliseconds 250
        Assert-Throws 'Matching running executable blocks all writes' { Invoke-SPInstall $running $CorePackage $true } '仍在运行'
        Assert-Test 'Running check creates no installer metadata' (-not (Test-Path -LiteralPath (Join-SPSafe $running 'SpacePhone-installer')))
    } finally {
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
        $null=Write-TestFile $running 'Probably Stolen.exe' 'FAKE-FIXTURE-NEVER-EXECUTE'
    }
    $fullUninstall=New-TestGame 'full-uninstall'
    $fullProtected=Get-ProtectedHashes $fullUninstall
    $null=Invoke-SPInstall $fullUninstall $FullPackage $true
    $null=Write-TestFile $fullUninstall 'BepInEx/plugins/SpacePhone/BrowserHost/custom-after-install.txt' 'UNOWNED-FILE'
    $result=Invoke-SPUninstall $fullUninstall
    Assert-Test 'Full uninstall removes tracked web host' (-not (Test-Path -LiteralPath (Join-SPSafe $fullUninstall 'BepInEx/plugins/SpacePhone/BrowserHost/SpacePhone.WebViewHost.exe')))
    Assert-Test 'Uninstall preserves untracked file inside managed component' (Test-Path -LiteralPath (Join-SPSafe $fullUninstall 'BepInEx/plugins/SpacePhone/BrowserHost/custom-after-install.txt'))
    Assert-Protected 'Protected files preserved after Full uninstall' $fullUninstall $fullProtected
    $report=[ordered]@{ startedUtc=[DateTime]::UtcNow.ToString('o'); runtime=$PSVersionTable.PSVersion.ToString(); testRoot=$testRoot; realGameWritten=$false; realGameExecuted=$false; realSaveRead=$false; fixturesContainOnlyDummyGameFiles=$true; pass=$checks.Count; fail=0; checks=@($checks.ToArray()) }
    Write-SPJson (Join-Path $PSScriptRoot 'installer-test-report.json') $report
    Write-Output ("PASS "+$checks.Count+" / FAIL 0 / Windows PowerShell "+$PSVersionTable.PSVersion.ToString())
} catch {
    $report=[ordered]@{ runtime=$PSVersionTable.PSVersion.ToString(); testRoot=$testRoot; realGameWritten=$false; pass=@($checks | Where-Object { $_.pass }).Count; fail=1; error=$_.Exception.Message; stack=$_.ScriptStackTrace; checks=@($checks.ToArray()) }
    Write-SPJson (Join-Path $PSScriptRoot 'installer-test-report.json') $report
    Write-Output ($report | ConvertTo-Json -Depth 8)
    exit 1
}

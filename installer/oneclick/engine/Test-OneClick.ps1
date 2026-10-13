param([string]$PackageRoot)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'OneClick.Core.ps1')
if (-not $PackageRoot) {throw '请传入含 package/mod、package/loader、loader-manifest.json 的完整包根目录。'}
$caseRoot=Join-Path $script:OCFixtureRoot ((Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,6))
[IO.Directory]::CreateDirectory($caseRoot)|Out-Null
$checks=New-Object 'Collections.Generic.List[object]'
$utf8=New-Object Text.UTF8Encoding($false)
function Write-TFile([string]$Base,[string]$Rel,[string]$Text){$p=Join-SPSafe $Base $Rel;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p))|Out-Null;[IO.File]::WriteAllText($p,$Text,$utf8)}
function Write-TPE([string]$Path,[int]$Machine=0x8664){
 $bytes=New-Object byte[] 512
 $bytes[0]=0x4d;$bytes[1]=0x5a;$bytes[0x3c]=0x80
 $bytes[0x80]=0x50;$bytes[0x81]=0x45
 $bytes[0x84]=[byte]($Machine-band 0xff);$bytes[0x85]=[byte](($Machine-shr 8)-band 0xff)
 [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path))|Out-Null;[IO.File]::WriteAllBytes($Path,$bytes)
}
function New-TGame([string]$Name){
 $p=Join-SPSafe $caseRoot $Name;[IO.Directory]::CreateDirectory($p)|Out-Null
 foreach($rel in @('Probably Stolen.exe','GameAssembly.dll','UnityPlayer.dll')){Write-TPE (Join-SPSafe $p $rel)}
 [IO.Directory]::CreateDirectory((Join-SPSafe $p 'Probably Stolen_Data'))|Out-Null
 Write-TFile $p '.spacephone-test-fixture' 'SPACEPHONE-ISOLATED-TEST-NO-GAME-SAVES'
 Write-TFile $p 'BepInEx/plugins/OtherMod/Other.dll' 'OTHER-MOD-KEEP'
 Write-TFile $p 'BepInEx/config/Other.cfg' 'OTHER-CONFIG-KEEP'
 Write-TFile $p 'BepInEx/interop/Existing.dll' 'EXISTING-INTEROP-KEEP'
 Write-TFile $p 'BepInEx/plugins/SpacePhone/Music/my-song.wav' 'USER-MUSIC-KEEP'
 Write-TFile $p 'BepInEx/plugins/SpacePhone/WebProfiles/Default/sentinel.txt' 'NOT-A-REAL-LOGIN'
 Write-TFile $p 'Probably Stolen_Data/save-sentinel.txt' 'NOT-A-REAL-SAVE'
 return $p
}
function Test-Check([string]$Name,[bool]$Pass,[string]$Detail=''){$checks.Add([pscustomobject]@{name=$Name;pass=$Pass;detail=$Detail});if(-not $Pass){throw "TEST FAILED: $Name $Detail"}}
function Test-Throws([string]$Name,[scriptblock]$Body,[string]$Pattern){$message='';try{&$Body|Out-Null}catch{$message=$_.Exception.Message};Test-Check $Name ([bool]($message-match $Pattern)) $message}
function Get-THashes([string]$Game,[bool]$ProtectedOnly=$false){
 $map=@{}
 if($ProtectedOnly){$paths=@('Probably Stolen.exe','GameAssembly.dll','UnityPlayer.dll','BepInEx/plugins/OtherMod/Other.dll','BepInEx/config/Other.cfg','BepInEx/interop/Existing.dll','BepInEx/plugins/SpacePhone/Music/my-song.wav','BepInEx/plugins/SpacePhone/WebProfiles/Default/sentinel.txt','Probably Stolen_Data/save-sentinel.txt')}
 else{$paths=@(Get-ChildItem -LiteralPath $Game -Recurse -File -Force|Where-Object {-not $_.FullName.StartsWith((Join-Path $Game $script:OCMetadata),[StringComparison]::OrdinalIgnoreCase)}|ForEach-Object {$_.FullName.Substring($Game.Length+1).Replace('\','/')})}
 foreach($rel in $paths){$map[$rel]=Get-SPHash (Join-SPSafe $Game $rel)}
 return $map
}
function Test-Same([string]$Name,$Before,$After){$same=$Before.Count-eq$After.Count;foreach($key in $Before.Keys){if(-not$After.ContainsKey($key)-or$Before[$key]-ne$After[$key]){$same=$false}};Test-Check $Name $same}
function Copy-TLoader([string]$Game,$Package){foreach($f in $Package.loaderFiles){$p=Join-SPSafe $Game $f.path;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($p))|Out-Null;Copy-Item -LiteralPath $f.source -Destination $p}}
try{
 $package=Read-OCPackage $PackageRoot
 Test-Check 'Real Full mod payload verified' ($package.modFiles.Count-gt 250)
 Test-Check 'Official supplied loader files verified' ($package.loaderFiles.Count-gt 150)
 $fresh=New-TGame 'fresh'
 $before=Get-THashes $fresh
 $protected=Get-THashes $fresh $true
 $plan=Get-OCPlan $fresh $PackageRoot $true
 Test-Check 'Fresh game plans combined loader and mod' ($plan.loaderStatus-eq'Missing'-and$plan.files.Count-eq($package.modFiles.Count+$package.loaderFiles.Count))
 $result=Invoke-OCInstall $fresh $PackageRoot $true
 Test-Check 'Fresh combined installation succeeds' ($result.status-eq'Installed'-and$result.loaderAction-eq'Installed')
 Test-Check 'Fresh loader becomes complete' ((Get-OCLoaderStatus $fresh)-eq'Existing')
 foreach($f in $package.loaderFiles){Test-Check ('Loader SHA: '+$f.path) ((Get-SPHash (Join-SPSafe $fresh $f.path))-eq$f.sha256)}
 Test-Check 'Original production SpacePhone DLL matches' ((Get-SPHash (Join-SPSafe $fresh 'BepInEx/plugins/SpacePhone/SpacePhone.dll'))-eq@($package.modFiles|Where-Object {$_.path-like'*/SpacePhone.dll'})[0].sha256)
 Test-Same 'Fresh install preserves user/game/config/interop/othermod' $protected (Get-THashes $fresh $true)
 $backupCount=@(Get-ChildItem -LiteralPath (Join-SPSafe $fresh ($script:OCMetadata+'/backups')) -Directory).Count
 $stateHash=Get-SPHash (Join-SPSafe $fresh ($script:OCMetadata+'/state.json'))
 $result=Invoke-OCInstall $fresh $PackageRoot $true
 Test-Check 'Same installation is a no-op' ($result.status-eq'AlreadyInstalled')
 Test-Check 'Repeat creates no backup and does not modify state' (@(Get-ChildItem -LiteralPath (Join-SPSafe $fresh ($script:OCMetadata+'/backups')) -Directory).Count-eq$backupCount-and(Get-SPHash (Join-SPSafe $fresh ($script:OCMetadata+'/state.json')))-eq$stateHash)
 $result=Invoke-OCRollback $fresh $true
 Test-Check 'Combined rollback succeeds' ($result.status-eq'RolledBack')
 Test-Same 'Combined rollback restores every prior game file exactly' $before (Get-THashes $fresh)
 Test-Check 'Rollback removes newly introduced loader without removing othermods' ((Get-OCLoaderStatus $fresh)-eq'Missing'-and(Test-Path -LiteralPath (Join-SPSafe $fresh 'BepInEx/plugins/OtherMod/Other.dll')))
 $failed=New-TGame 'failure'
 Write-TFile $failed 'BepInEx/plugins/SpacePhone/SpacePhone.dll' 'PRIOR-DLL'
 Write-TFile $failed 'BepInEx/plugins/SpacePhone/Assets/old.png' 'PRIOR-ASSET'
 $failedBefore=Get-THashes $failed
 Test-Throws 'Failure after all loader files and some mod files triggers combined rollback' {Invoke-OCInstall $failed $PackageRoot $true ($package.loaderFiles.Count+3)} '已恢复安装前'
 Test-Same 'Combined failure leaves no loader or mod partial install' $failedBefore (Get-THashes $failed)
 Test-Check 'Failed fresh installation leaves no management state' ($null-eq(Get-OCState $failed))
 Test-Check 'Failed installation cleans stage' (@(Get-ChildItem -LiteralPath (Join-SPSafe $failed ($script:OCMetadata+'/staging')) -Force).Count-eq0)
 $existing=New-TGame 'existing'
 Copy-TLoader $existing $package
 Write-TFile $existing 'BepInEx/config/BepInEx.cfg' 'CUSTOM-LOADER-CONFIG-KEEP'
 $loaderBefore=@{};foreach($f in $package.loaderFiles){$loaderBefore[$f.path]=Get-SPHash (Join-SPSafe $existing $f.path)}
 $existingProtected=Get-THashes $existing $true
 $result=Invoke-OCInstall $existing $PackageRoot $true
 Test-Check 'Complete existing loader is preserved' ($result.loaderAction-eq'Preserved')
 foreach($f in $package.loaderFiles){Test-Check ('Existing loader unchanged: '+$f.path) ((Get-SPHash (Join-SPSafe $existing $f.path))-eq$loaderBefore[$f.path])}
 Test-Check 'Existing loader configuration retained' ((Get-Content -LiteralPath (Join-SPSafe $existing 'BepInEx/config/BepInEx.cfg') -Raw)-eq'CUSTOM-LOADER-CONFIG-KEEP')
 Test-Same 'Existing loader install preserves user and othermod' $existingProtected (Get-THashes $existing $true)
 $result=Invoke-OCUninstall $existing $true
 Test-Check 'Uninstall removes mobile mod only' ($result.status-eq'Uninstalled'-and-not(Test-Path -LiteralPath (Join-SPSafe $existing 'BepInEx/plugins/SpacePhone/SpacePhone.dll')))
 Test-Check 'Uninstall preserves complete loader' ((Get-OCLoaderStatus $existing)-eq'Existing')
 Test-Same 'Uninstall preserves user/config/interop/othermod/game files' $existingProtected (Get-THashes $existing $true)
 $incomplete=New-TGame 'incomplete';Write-TPE (Join-SPSafe $incomplete 'winhttp.dll')
 $incompleteBefore=Get-THashes $incomplete
 Test-Throws 'Incomplete loader refused without changes' {Invoke-OCInstall $incomplete $PackageRoot $true} '不完整'
 Test-Same 'Incomplete loader leaves prior files unchanged' $incompleteBefore (Get-THashes $incomplete)
 Test-Check 'Incomplete loader rejection creates no metadata' (-not(Test-Path -LiteralPath (Join-SPSafe $incomplete $script:OCMetadata)))
 $generator=New-TGame 'missing-generator';Copy-TLoader $generator $package
 Remove-Item -LiteralPath (Join-SPSafe $generator 'BepInEx/core/Il2CppInterop.Generator.dll') -Force
 $generatorBefore=Get-THashes $generator
 Test-Throws 'Missing interop generation dependency rejected' {Invoke-OCInstall $generator $PackageRoot $true} '不完整'
 Test-Same 'Missing generator leaves all files unchanged' $generatorBefore (Get-THashes $generator)
 Test-Check 'Missing generator creates no install metadata' (-not(Test-Path -LiteralPath (Join-SPSafe $generator $script:OCMetadata)))
 $mixed=New-TGame 'mixed';Write-TFile $mixed 'BepInEx/core/BepInEx.Unity.Mono.dll' 'OTHER-LOADER'
 $mixedBefore=Get-THashes $mixed
 Test-Throws 'Mixed Mono loader rejected' {Invoke-OCInstall $mixed $PackageRoot $true} '其他或混合'
 Test-Same 'Mixed loader leaves all files unchanged' $mixedBefore (Get-THashes $mixed)
 $x86=New-TGame 'x86';Write-TPE (Join-SPSafe $x86 'Probably Stolen.exe') 0x014c
 Test-Throws 'Wrong 32bit game architecture rejected' {Invoke-OCInstall $x86 $PackageRoot $true} 'AMD64'
 Test-Check 'Wrong architecture creates no metadata' (-not(Test-Path -LiteralPath (Join-SPSafe $x86 $script:OCMetadata)))
 $conflict=New-TGame 'conflict';[IO.Directory]::CreateDirectory((Join-SPSafe $conflict 'BepInEx/plugins/SpacePhone/Assets/dialer.png'))|Out-Null
 Test-Throws 'File-directory conflict rejected before loader writes' {Invoke-OCInstall $conflict $PackageRoot $true} '位置已经是文件夹'
 Test-Check 'Conflict creates no partial loader' ((Get-OCLoaderStatus $conflict)-eq'Missing'-and-not(Test-Path -LiteralPath (Join-SPSafe $conflict $script:OCMetadata)))
 Test-Throws 'Protected config cannot enter loader manifest scope' {Get-OCKind 'BepInEx/config/BepInEx.cfg'} '不会管理'
 Test-Throws 'Protected interop cannot enter loader scope' {Get-OCKind 'BepInEx/interop/Game.dll'} '不会管理'
 Test-Throws 'User profile cannot enter managed scope' {Get-OCKind 'BepInEx/plugins/SpacePhone/WebProfiles/Default/account.json'} '不会管理'
 Test-Throws 'Fixture injection outside dedicated fixtures refused' {Assert-OCFixture (Split-Path $PSScriptRoot -Parent) $true 1} '超出'
 $junction=New-TGame 'junction'
 $target=Join-SPSafe $caseRoot 'link-target';[IO.Directory]::CreateDirectory($target)|Out-Null;Write-TFile $target 'sentinel.txt' 'LINK-TARGET-KEEP'
 $link=Join-SPSafe $junction 'BepInEx/plugins/SpacePhone/Assets/link';[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($link))|Out-Null
 New-Item -ItemType Junction -Path $link -Target $target|Out-Null
 try{Test-Throws 'Native attribute check still rejects nested reparse point' {Invoke-OCInstall $junction $PackageRoot $true} '不支持符号链接'}
 finally{Assert-SPWithin $junction $link;[IO.Directory]::Delete($link)}
 Test-Check 'Native reparse rejection preserves target data' ((Get-Content -LiteralPath (Join-SPSafe $target 'sentinel.txt') -Raw)-eq'LINK-TARGET-KEEP')
 $report=[ordered]@{runtime=$PSVersionTable.PSVersion.ToString();packageRoot=$PackageRoot;fixtureRoot=$caseRoot;realGameWritten=$false;realGameExecuted=$false;realSaveRead=$false;loaderFiles=$package.loaderFiles.Count;modFiles=$package.modFiles.Count;pass=$checks.Count;fail=0;checks=@($checks.ToArray())}
 Write-SPJson (Join-Path $PSScriptRoot 'oneclick-test-report.json') $report
 Write-Output ("PASS "+$checks.Count+" / FAIL 0 / Windows PowerShell "+$PSVersionTable.PSVersion)
}catch{
 $report=[ordered]@{runtime=$PSVersionTable.PSVersion.ToString();fixtureRoot=$caseRoot;realGameWritten=$false;pass=@($checks|Where-Object {$_.pass}).Count;fail=1;error=$_.Exception.Message;stack=$_.ScriptStackTrace;checks=@($checks.ToArray())}
 Write-SPJson (Join-Path $PSScriptRoot 'oneclick-test-report.json') $report
 Write-Output ($report|ConvertTo-Json -Depth 8)
 exit 1
}

[CmdletBinding()]
param(
 [ValidateSet('Find','Check','Install','Rollback','Uninstall')][string]$Action='Check',
 [string]$GamePath,[string]$PackageRoot,[switch]$FixtureMode,[int]$FailAfterFiles=0,[switch]$ProgressJson
)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object Text.UTF8Encoding($false)
try {
 . (Join-Path $PSScriptRoot 'OneClick.Core.ps1')
 if (-not $PackageRoot) {$PackageRoot=Split-Path $PSScriptRoot -Parent}
 $progress=$null
 if ($ProgressJson) {$progress={param($message,$percent)[Console]::WriteLine(([pscustomobject]@{event='progress';message=[string]$message;percent=[int]$percent}|ConvertTo-Json -Compress))}}
 $result=switch($Action){
  'Find' {[pscustomobject]@{status='Found';message='已检测 Steam 游戏目录';paths=@(Find-SPGameFolders)}}
  'Check' {$p=Get-OCPlan $GamePath $PackageRoot ([bool]$FixtureMode) $FailAfterFiles $progress;[pscustomobject]@{status='Ready';message=$(if($p.same){'同版手机已存在，可以直接使用或建立管理记录。'}else{'检查通过，点击安装即可。'});gamePath=$p.gamePath;loaderAction=$(if($p.loaderStatus -eq 'Missing'){'WillInstall'}else{'PreserveExisting'});files=$p.files.Count;version=$p.package.mod.version}}
  'Install' {Invoke-OCInstall $GamePath $PackageRoot ([bool]$FixtureMode) $FailAfterFiles $progress}
  'Rollback' {Invoke-OCRollback $GamePath ([bool]$FixtureMode)}
  'Uninstall' {Invoke-OCUninstall $GamePath ([bool]$FixtureMode)}
 }
 $result|Add-Member -NotePropertyName event -NotePropertyValue 'result'
 [Console]::WriteLine(($result|ConvertTo-Json -Depth 8 -Compress))
 exit 0
} catch {
 [Console]::WriteLine(([pscustomobject]@{event='result';status='Error';message=$_.Exception.Message}|ConvertTo-Json -Compress))
 exit 2
}

[CmdletBinding()]
param([string]$PackageRoot)
$ErrorActionPreference='Stop'
if (-not $PackageRoot) { $PackageRoot=Join-Path $PSScriptRoot 'build-output' }
$engineSource=Join-Path $PSScriptRoot 'engine'
$engineTarget=Join-Path $PackageRoot 'engine'
New-Item -ItemType Directory -Path $engineTarget -Force | Out-Null
$records=@()
foreach ($name in @('OneClick.ps1','OneClick.Core.ps1','Legacy.Helpers.ps1')) {
 $source=Join-Path $engineSource $name
 Copy-Item -LiteralPath $source -Destination (Join-Path $engineTarget $name) -Force
 $records += '{"engine/'+$name+'","'+(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()+'"}'
}
$integrity=@'
using System.Collections.Generic;
using System.Reflection;
[assembly:AssemblyTitle("SpacePhone Installation Assistant")]
[assembly:AssemblyProduct("SpacePhone 3.1")]
[assembly:AssemblyVersion("3.1.1.0")]
[assembly:AssemblyFileVersion("3.1.1.0")]
namespace SpacePhoneSetup { static class Integrity {
public const string WebViewSha256="2a6add76c37bfa872eb8c2b22d45b3216b8e2a1f193e0774006340ff62ac5fa0";
public static readonly Dictionary<string,string> Engine=new Dictionary<string,string>{
RECORDS
};}}
'@
$integrity=$integrity.Replace('RECORDS',($records -join ",`r`n"))
$integrityPath=Join-Path $PackageRoot 'Integrity.build.cs'
[IO.File]::WriteAllText($integrityPath,$integrity,(New-Object Text.UTF8Encoding($true)))
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$argsList=@('/nologo','/target:winexe','/platform:x64','/optimize+','/debug-','/utf8output',('/out:'+(Join-Path $PackageRoot 'SpacePhone安装器.exe')),('/win32manifest:'+(Join-Path $PSScriptRoot 'setup.manifest')),('/win32icon:'+(Join-Path $PSScriptRoot 'setup.ico')),'/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll','/reference:System.Windows.Forms.dll','/reference:System.Web.Extensions.dll',(Join-Path $PSScriptRoot 'SpacePhoneSetup.cs'),$integrityPath)
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
Write-Output ('Built: '+(Join-Path $PackageRoot 'SpacePhone安装器.exe'))

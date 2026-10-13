<#
SpacePhone installer / 深空手机安装器
Local, offline installer. Requires Windows PowerShell 5.1 and BepInEx 6 IL2CPP.
CLI: -Action Check|Install|Rollback|Uninstall -GamePath <folder> -PackageRoot <folder>
Tests: -FixtureMode -FailAfterFiles 1 only in a marked isolated test directory.
#>
[CmdletBinding()]
param(
    [ValidateSet('Gui','Check','Install','Rollback','Uninstall')][string]$Action='Gui',
    [string]$GamePath,
    [string]$PackageRoot,
    [switch]$FixtureMode,
    [int]$FailAfterFiles=0,
    [switch]$GuiSmokeTest,
    [string]$ReportPath
)
$ErrorActionPreference='Stop'
if (-not $PackageRoot) { $PackageRoot=$PSScriptRoot }
. (Join-Path $PSScriptRoot 'Installer.Core.ps1')

if ($Action -ne 'Gui') {
    try {
        if ($FixtureMode) { Assert-SPFixture (Get-SPFullPath $GamePath) $true $FailAfterFiles }
        $result = switch ($Action) {
            'Check' {
                $root=Assert-SPGame $GamePath
                Assert-SPNotRunning $root
                $manifest=Read-SPManifest $PackageRoot
                Assert-SPTargetLayout $root $manifest.files
                $state=Get-SPState $root
                [pscustomobject]@{ status='Ready'; gamePath=$root; version=$manifest.version; files=$manifest.files.Count; hasBrowserHost=($manifest.components -contains 'BrowserHost'); hasManagedInstallation=[bool]$state; message='游戏目录、加载器与全部安装文件校验通过。' }
            }
            'Install' { Invoke-SPInstall $GamePath $PackageRoot ([bool]$FixtureMode) $FailAfterFiles }
            'Rollback' { Invoke-SPRollback $GamePath }
            'Uninstall' { Invoke-SPUninstall $GamePath }
        }
        $json=$result | ConvertTo-Json -Depth 8
        if ($ReportPath) { Write-SPJson $ReportPath $result }
        Write-Output $json
        exit 0
    } catch {
        $result=[pscustomobject]@{ status='Error'; message=$_.Exception.Message }
        if ($ReportPath) { Write-SPJson $ReportPath $result }
        Write-Output ($result | ConvertTo-Json -Depth 8)
        exit 2
    }
}

try {
    Add-Type -AssemblyName PresentationFramework,PresentationCore,WindowsBase,System.Windows.Forms
    [xml]$xaml=@'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Title="SpacePhone 3.1 · 深空手机安装器" Width="900" Height="710" MinWidth="860" MinHeight="670" WindowStartupLocation="CenterScreen" Background="#10171D" Foreground="#ECF0EC" FontFamily="Microsoft YaHei UI" FontSize="13" ResizeMode="CanMinimize">
  <Window.Resources>
    <Style TargetType="Button">
      <Setter Property="Foreground" Value="#EAF1EE"/><Setter Property="Background" Value="#26343D"/>
      <Setter Property="BorderBrush" Value="#3C5058"/><Setter Property="BorderThickness" Value="1"/>
      <Setter Property="Padding" Value="17,10"/><Setter Property="Cursor" Value="Hand"/>
      <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="Shell" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7"><ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="Shell" Property="Opacity" Value="0.82"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="Shell" Property="Opacity" Value="0.38"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
    </Style>
    <Style TargetType="TextBox"><Setter Property="Background" Value="#111C23"/><Setter Property="Foreground" Value="#EEF2EE"/><Setter Property="BorderBrush" Value="#38505A"/><Setter Property="Padding" Value="12,10"/><Setter Property="SelectionBrush" Value="#38786D"/></Style>
  </Window.Resources>
  <Grid Margin="30,25,30,22">
    <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="22"/><RowDefinition Height="Auto"/><RowDefinition Height="18"/><RowDefinition Height="Auto"/><RowDefinition Height="18"/><RowDefinition Height="*"/><RowDefinition Height="Auto"/></Grid.RowDefinitions>
    <Grid Grid.Row="0">
      <Grid.ColumnDefinitions><ColumnDefinition Width="74"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions>
      <Border Width="58" Height="66" CornerRadius="11" BorderThickness="2" BorderBrush="#74B9A8" Background="#203A3D" HorizontalAlignment="Left"><TextBlock Text="SP" FontSize="21" FontWeight="Bold" Foreground="#B5E7D8" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
      <StackPanel Grid.Column="1" VerticalAlignment="Center"><TextBlock Text="深空手机" FontSize="27" FontWeight="SemiBold"/><TextBlock Text="SPACEPHONE 3.1  /  本地安装与版本回退" Foreground="#90A5AB" Margin="1,5,0,0" FontSize="12"/></StackPanel>
      <Border Grid.Column="2" Background="#243733" CornerRadius="5" Padding="12,6" VerticalAlignment="Top"><TextBlock Text="OFFLINE INSTALLER" Foreground="#AFE0CB" FontSize="10" FontWeight="Bold"/></Border>
    </Grid>
    <Border Grid.Row="2" Background="#19262D" CornerRadius="10" Padding="21,18">
      <StackPanel>
        <TextBlock Text="01   选择游戏文件夹" FontSize="15" FontWeight="SemiBold"/>
        <TextBlock Text="选择包含 Probably Stolen.exe 的根目录。通常位于 Steam / steamapps / common。" Foreground="#A7B7BD" FontSize="12" Margin="0,8,0,12"/>
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="10"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><TextBox x:Name="PathBox" Height="43" VerticalContentAlignment="Center" FontSize="12"/><Button x:Name="BrowseButton" Grid.Column="2" Content="选择文件夹…" Padding="16,10"/></Grid>
        <DockPanel Margin="0,10,0,0"><Button x:Name="DetectButton" Content="重新检测 Steam" DockPanel.Dock="Right" FontSize="11" Padding="12,6"/><TextBlock x:Name="PathHint" Text="可手动输入路径，也可使用 Steam 自动推荐。" Foreground="#7F979F" FontSize="11" VerticalAlignment="Center" TextWrapping="Wrap" Margin="0,0,12,0"/></DockPanel>
      </StackPanel>
    </Border>
    <Border Grid.Row="4" Background="#19262D" CornerRadius="10" Padding="21,18">
      <StackPanel><TextBlock Text="02   检查并安装" FontSize="15" FontWeight="SemiBold"/>
        <TextBlock x:Name="PackageHint" Text="需要已有 BepInEx 6 IL2CPP x64；此工具不会联网下载加载器。" TextWrapping="Wrap" Foreground="#A7B7BD" FontSize="12" Margin="0,8,0,13"/>
        <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="12"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions><Button x:Name="CheckButton" Content="检查安装环境 / Check"/><Button x:Name="InstallButton" Grid.Column="2" Content="安装或更新 / Install" Background="#77B4A2" Foreground="#122622" BorderBrush="#77B4A2" FontWeight="SemiBold"/></Grid>
        <ProgressBar x:Name="ProgressBar" Height="5" Foreground="#79BEAB" Background="#263B44" BorderThickness="0" Margin="0,15,0,8" Minimum="0" Maximum="100"/>
        <TextBlock x:Name="StatusText" Text="准备就绪。建议先正常保存并退出游戏。" TextWrapping="Wrap" Foreground="#B5CFCA" FontSize="12"/>
      </StackPanel>
    </Border>
    <Border Grid.Row="6" Background="#151F26" BorderBrush="#293A43" BorderThickness="1" CornerRadius="9" Padding="17,12" MinHeight="110">
      <Grid><Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions><TextBlock Text="安装记录  /  LOCAL ACTIVITY" Foreground="#829BA3" FontSize="11" FontWeight="SemiBold"/><TextBox x:Name="LogBox" Grid.Row="1" Text="等待操作。&#10;只管理本 Mod 文件；音乐、网页登录目录和游戏存档保留。" IsReadOnly="True" BorderThickness="0" Background="Transparent" FontSize="11" Padding="0,7,0,0" TextWrapping="Wrap" VerticalScrollBarVisibility="Auto"/></Grid>
    </Border>
    <Grid Grid.Row="7" Margin="0,17,0,0"><Grid.ColumnDefinitions><ColumnDefinition Width="Auto"/><ColumnDefinition Width="10"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="*"/><ColumnDefinition Width="Auto"/><ColumnDefinition Width="8"/><ColumnDefinition Width="Auto"/></Grid.ColumnDefinitions><Button x:Name="RollbackButton" Content="回退上次安装" FontSize="12" Padding="14,9"/><Button x:Name="UninstallButton" Grid.Column="2" Content="移除 Mod 文件" Foreground="#D6B7AB" FontSize="12" Padding="14,9"/><Button x:Name="GuideButton" Grid.Column="4" Content="安装教程" FontSize="12" Padding="13,9"/><Button x:Name="LoaderButton" Grid.Column="6" Content="加载器文档 ↗" FontSize="12" Padding="13,9"/></Grid>
  </Grid>
</Window>
'@
    $reader=New-Object Xml.XmlNodeReader $xaml
    $script:SPWindow=[Windows.Markup.XamlReader]::Load($reader)
    $script:SPUI=@{}
    foreach ($name in @('PathBox','BrowseButton','DetectButton','PathHint','PackageHint','CheckButton','InstallButton','ProgressBar','StatusText','LogBox','RollbackButton','UninstallButton','GuideButton','LoaderButton')) { $script:SPUI[$name]=$script:SPWindow.FindName($name) }
    $script:SPBusy=$false
    function Add-SPLog([string]$Message) {
        $script:SPUI.LogBox.AppendText("`r`n" + (Get-Date -Format 'HH:mm:ss') + '  ' + $Message)
        $script:SPUI.LogBox.ScrollToEnd()
    }
    function Set-SPBusy([bool]$Busy) {
        $script:SPBusy=$Busy
        foreach ($name in @('BrowseButton','DetectButton','CheckButton','InstallButton','RollbackButton','UninstallButton','GuideButton','LoaderButton','PathBox')) { $script:SPUI[$name].IsEnabled = -not $Busy }
    }
    $script:SPProgress={ param($message,$percent)
        $script:SPUI.StatusText.Text=[string]$message
        $script:SPUI.ProgressBar.Value=[double]$percent
        $script:SPWindow.Dispatcher.Invoke([Action]{},[Windows.Threading.DispatcherPriority]::Render)
    }
    function Set-SPDetectedPath {
        $found=@(Find-SPGameFolders)
        if ($found.Count -gt 0) { $script:SPUI.PathBox.Text=$found[0]; $script:SPUI.PathHint.Text='已找到 Steam 安装。请核对这就是要安装的游戏目录。' }
        else { $script:SPUI.PathHint.Text='没有找到默认 Steam 安装；请点“选择文件夹”或粘贴游戏根路径。' }
    }
    function Invoke-SPUIAction([string]$Kind) {
        if ($script:SPBusy) { return }
        Set-SPBusy $true
        try {
            $script:SPUI.ProgressBar.Value=0
            if ($Kind -eq 'Check') {
                $root=Assert-SPGame $script:SPUI.PathBox.Text
                Assert-SPNotRunning $root
                & $script:SPProgress '校验全部安装文件 / Verifying payload' 25
                $m=Read-SPManifest $PackageRoot
                Assert-SPTargetLayout $root $m.files
                $null=Get-SPState $root
                $result=[pscustomobject]@{ message="检查通过。SpacePhone $($m.version)，共 $($m.files.Count) 个文件；可以安装。" }
                & $script:SPProgress $result.message 100
            } elseif ($Kind -eq 'Install') { $result=Invoke-SPInstall $script:SPUI.PathBox.Text $PackageRoot $false 0 $script:SPProgress }
            elseif ($Kind -eq 'Rollback') { $result=Invoke-SPRollback $script:SPUI.PathBox.Text $script:SPProgress }
            elseif ($Kind -eq 'Uninstall') { $result=Invoke-SPUninstall $script:SPUI.PathBox.Text $script:SPProgress }
            $script:SPUI.StatusText.Text=$result.message
            Add-SPLog $result.message
            if ($result.PSObject.Properties['backupPath'] -and $result.backupPath) { Add-SPLog ('备份：'+$result.backupPath) }
        } catch {
            $script:SPUI.ProgressBar.Value=0
            $script:SPUI.StatusText.Text=$_.Exception.Message
            Add-SPLog ('未完成：'+$_.Exception.Message)
            if ($_.Exception -is [UnauthorizedAccessException] -or $_.Exception.Message -match 'Access|拒绝|权限') { Add-SPLog '若游戏目录需要管理员写权限，请关闭工具，对 Start-Installer.cmd 右键选择“以管理员身份运行”。' }
        } finally { Set-SPBusy $false }
    }
    $script:SPUI.BrowseButton.Add_Click({
        $dialog=New-Object Windows.Forms.FolderBrowserDialog
        $dialog.Description='选择 Probably Stolen 的游戏根目录 / Select the game root folder'
        $dialog.ShowNewFolderButton=$false
        if (Test-Path -LiteralPath $script:SPUI.PathBox.Text -PathType Container) { $dialog.SelectedPath=$script:SPUI.PathBox.Text }
        if ($dialog.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) { $script:SPUI.PathBox.Text=$dialog.SelectedPath; $script:SPUI.PathHint.Text='已选择文件夹。点击“检查安装环境”核对游戏与加载器。' }
        $dialog.Dispose()
    })
    $script:SPUI.DetectButton.Add_Click({ Set-SPDetectedPath })
    $script:SPUI.CheckButton.Add_Click({ Invoke-SPUIAction 'Check' })
    $script:SPUI.InstallButton.Add_Click({ Invoke-SPUIAction 'Install' })
    $script:SPUI.RollbackButton.Add_Click({
        $answer=[Windows.MessageBox]::Show('恢复本工具上一次安装前的组件？这会保留存档、音乐和网页登录目录。','SpacePhone · 回退',[Windows.MessageBoxButton]::OKCancel,[Windows.MessageBoxImage]::Question)
        if ($answer -eq [Windows.MessageBoxResult]::OK) { Invoke-SPUIAction 'Rollback' }
    })
    $script:SPUI.UninstallButton.Add_Click({
        $answer=[Windows.MessageBox]::Show('移除本工具记录的 Mod 文件？存档、音乐、网页登录资料和其他 Mod 保留。安装文件会先备份。','SpacePhone · 移除',[Windows.MessageBoxButton]::OKCancel,[Windows.MessageBoxImage]::Question)
        if ($answer -eq [Windows.MessageBoxResult]::OK) { Invoke-SPUIAction 'Uninstall' }
    })
    $script:SPUI.GuideButton.Add_Click({
        $guide=Join-Path $PackageRoot '安装教程.html'
        if (-not (Test-Path -LiteralPath $guide -PathType Leaf)) { $guide=Join-Path $PackageRoot 'payload\BepInEx\plugins\SpacePhone\安装教程.html' }
        if (Test-Path -LiteralPath $guide -PathType Leaf) { Start-Process -FilePath $guide }
        else { Add-SPLog '安装器旁边缺少教程。请重新完整解压 ZIP，或查看发布套件中的安装教程。' }
    })
    $script:SPUI.LoaderButton.Add_Click({ Start-Process 'https://docs.bepinex.dev/master/articles/user_guide/installation/unity_il2cpp.html' })
    $script:SPWindow.Add_Closing({ param($sender,$e) if ($script:SPBusy) { $e.Cancel=$true } })
    if ($GamePath) { $script:SPUI.PathBox.Text=$GamePath } else { Set-SPDetectedPath }
    try {
        $manifest=Read-SPManifest $PackageRoot
        $label=if ($manifest.components -contains 'BrowserHost') { '完整包：含可选网页窗口' } else { '基础包：不改变已有网页窗口组件' }
        $script:SPUI.PackageHint.Text="$label · $($manifest.files.Count) 个文件。需要 BepInEx 6 IL2CPP x64；此工具不会下载加载器。"
    } catch { $script:SPUI.PackageHint.Text=$_.Exception.Message; Add-SPLog $_.Exception.Message }
    if ($GuiSmokeTest) {
        # Render the content directly; no visible native window is opened during QA.
        $script:SPWindow.ApplyTemplate() | Out-Null
        $layout=$script:SPWindow.Content
        $layout.Measure((New-Object Windows.Size(900,680)))
        $layout.Arrange((New-Object Windows.Rect(0,0,900,680)))
        $layout.UpdateLayout()
        $controls=@()
        foreach ($key in $script:SPUI.Keys) {
            $control=$script:SPUI[$key]
            $controls += [pscustomobject]@{ name=$key; type=$control.GetType().Name; width=$control.ActualWidth; height=$control.ActualHeight; initialized=[bool]$control }
        }
        if (@($controls | Where-Object { -not $_.initialized -or $_.width -le 0 -or $_.height -le 0 }).Count -gt 0) { throw 'GUI smoke test: control has no rendered bounds.' }
        $result=[pscustomobject]@{ status='GuiInitialized'; title=$script:SPWindow.Title; controls=$controls; automaticLayoutOnly=$true; interactiveMouseTest=$false }
        if ($ReportPath) {
            Write-SPJson $ReportPath $result
            $png=[IO.Path]::ChangeExtension([IO.Path]::GetFullPath($ReportPath),'.png')
            $bitmap=New-Object Windows.Media.Imaging.RenderTargetBitmap(900,710,96,96,[Windows.Media.PixelFormats]::Pbgra32)
            $bitmap.Render($layout)
            $encoder=New-Object Windows.Media.Imaging.PngBitmapEncoder
            $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
            $stream=[IO.File]::Create($png)
            try { $encoder.Save($stream) } finally { $stream.Dispose() }
        }
        Write-Output ($result | ConvertTo-Json -Depth 8)
    } else { $script:SPWindow.ShowDialog() | Out-Null }
} catch {
    $message=$_.Exception.Message
    if ($GuiSmokeTest) { Write-Output ([pscustomobject]@{status='Error';message=$message} | ConvertTo-Json); exit 2 }
    try { [Windows.MessageBox]::Show("安装器无法启动：$message`n请完整解压 ZIP，并使用 Windows PowerShell 5.1。",'SpacePhone 安装器') | Out-Null }
    catch { Write-Error $message }
    exit 2
}

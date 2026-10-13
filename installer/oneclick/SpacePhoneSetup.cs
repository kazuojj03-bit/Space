using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpacePhoneSetup {
 static class Program {
  public static readonly string Root=AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
  public static readonly JavaScriptSerializer Json=new JavaScriptSerializer();
  public static string GameArgument="";
  public static string ResumeAction="";
  [STAThread] static int Main(string[] args) {
   // Some launch environments contain both PATH and Path. .NET Framework's
   // child environment dictionary rejects that duplication. Normalize this
   // process's copy only; no user/system environment settings are persisted.
   string inheritedPath=Environment.GetEnvironmentVariable("Path")??"";
   for(int pass=0;pass<3;pass++)Environment.SetEnvironmentVariable("Path",null);
   Environment.SetEnvironmentVariable("Path",inheritedPath);
   Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
   string selfTest="",checkReport="";
   for(int i=0;i<args.Length;i++) {
    if(args[i]=="--game"&&i+1<args.Length) GameArgument=args[++i];
    else if(args[i]=="--resume"&&i+1<args.Length) ResumeAction=args[++i];
    else if(args[i]=="--selftest"&&i+1<args.Length) selfTest=args[++i];
    else if(args[i]=="--check-report"&&i+1<args.Length) checkReport=args[++i];
   }
   try {
    if(selfTest!="") { using(var form=new SetupForm()){form.RenderTest(selfTest);} return 0; }
    if(checkReport!="") { var r=Backend.Run("Check",GameArgument,null); File.WriteAllText(checkReport,Json.Serialize(r),new UTF8Encoding(false)); return Convert.ToString(r["status"])=="Error"?2:0; }
    if(!Environment.Is64BitOperatingSystem) throw new Exception("此安装器适用于 Windows 64 位系统。请使用游戏的 x64 版本。");
    Application.Run(new SetupForm()); return 0;
   } catch(Exception e) {
    if(selfTest!=""||checkReport!="") {File.WriteAllText(selfTest!=""?selfTest:checkReport,Json.Serialize(new {status="Error",message=e.Message}));return 2;}
    MessageBox.Show(e.Message,"深空手机安装器",MessageBoxButtons.OK,MessageBoxIcon.Error);return 2;
   }
  }
  public static string Quote(string s) {
   var b=new StringBuilder("\""); int slashes=0;
   foreach(char c in s) { if(c=='\\'){slashes++;continue;} if(c=='"'){b.Append('\\',slashes*2+1);b.Append(c);}else{b.Append('\\',slashes);b.Append(c);}slashes=0; }
   b.Append('\\',slashes*2);b.Append('"');return b.ToString();
  }
  public static bool Admin() {using(var id=WindowsIdentity.GetCurrent()){return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);}}
  public static string Hash(string p) {using(var stream=File.OpenRead(p))using(var h=SHA256.Create()){return BitConverter.ToString(h.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}}
 }
 static class Backend {
  public static void VerifyEngine() {
   foreach(var item in Integrity.Engine) {
    string p=Path.Combine(Program.Root,item.Key.Replace('/',Path.DirectorySeparatorChar));
    if(!File.Exists(p)||Program.Hash(p)!=item.Value) throw new Exception("安装器文件缺失或校验失败。请完整解压原 ZIP，不要单独移动 EXE。");
   }
  }
  public static Dictionary<string,object> Run(string action,string game,Action<string,int> progress) {
   VerifyEngine();
   string shell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"System32\WindowsPowerShell\v1.0\powershell.exe");
   var start=new ProcessStartInfo(shell,"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "+Program.Quote(Path.Combine(Program.Root,@"engine\OneClick.ps1"))+" -Action "+action+" -GamePath "+Program.Quote(game)+" -PackageRoot "+Program.Quote(Program.Root)+" -ProgressJson");
   start.UseShellExecute=false;start.CreateNoWindow=true;start.WindowStyle=ProcessWindowStyle.Hidden;
   // Native Explorer launches and shells can supply different module paths.
   // Use the selected Windows PowerShell's built-in modules for this child only.
   start.EnvironmentVariables["PSModulePath"]=Path.Combine(Path.GetDirectoryName(shell),"Modules");
   start.RedirectStandardOutput=true;start.RedirectStandardError=true;start.StandardOutputEncoding=Encoding.UTF8;start.StandardErrorEncoding=Encoding.UTF8;
   Dictionary<string,object> final=null;var error=new StringBuilder();
   using(var process=new Process()){process.StartInfo=start;process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(error)error.AppendLine(e.Data);};process.Start();process.BeginErrorReadLine();string line;
    while((line=process.StandardOutput.ReadLine())!=null) {
     if(String.IsNullOrWhiteSpace(line))continue;
     try {var item=Program.Json.Deserialize<Dictionary<string,object>>(line);object kind;
      if(item.TryGetValue("event",out kind)&&Convert.ToString(kind)=="progress") {if(progress!=null)progress(Convert.ToString(item["message"]),Convert.ToInt32(item["percent"]));}
      else if(item.ContainsKey("status"))final=item;
     }catch(ArgumentException){lock(error)error.AppendLine(line);}
    }
    process.WaitForExit();
    if(final==null)throw new Exception("安装检查未能完成。请点“更多选项 → 安装记录”查看详情。\r\n"+error.ToString());
    if(process.ExitCode!=0||Convert.ToString(final["status"])=="Error")throw new Exception(Convert.ToString(final["message"]));
   }
   return final;
  }
 }
 static class WebRuntime {
  const string Key=@"Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";
  public static bool ValidVersion(string v){Version version;return Version.TryParse(v,out version)&&version>new Version(0,0,0,0);}
  public static string InstalledVersion() {
   foreach(RegistryHive hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser})foreach(RegistryView view in new[]{RegistryView.Registry32,RegistryView.Registry64}) {
    try {using(var root=RegistryKey.OpenBaseKey(hive,view))using(var key=root.OpenSubKey(Key)){if(key!=null){string pv=Convert.ToString(key.GetValue("pv"));if(ValidVersion(pv))return pv;}}}catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}
   }
   return "";
  }
  public static bool NeedsInstall(string installedVersion){return !ValidVersion(installedVersion);}
  public static void Ensure(Action<string,int> progress) {
   string current=InstalledVersion();if(!NeedsInstall(current)){if(progress!=null)progress("网页运行组件已就绪，将保留现有版本。",30);return;}
   string file=Path.Combine(Program.Root,@"package\runtime\MicrosoftEdgeWebView2RuntimeInstallerX64.exe");
   if(!File.Exists(file)||Program.Hash(file)!=Integrity.WebViewSha256)throw new Exception("微软网页组件缺失或校验失败。请重新完整解压安装包。");
   if(progress!=null)progress("正在安装随包的微软网页组件，请稍候…",30);
   var start=new ProcessStartInfo(file,"/silent /install"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden};
   int code;using(var p=Process.Start(start)){p.WaitForExit();code=p.ExitCode;}
   if(InstalledVersion()=="")throw new Exception("微软网页组件未能完成安装（代码 "+code+"）。游戏文件尚未改动。请稍后重试；无需卸载已有的 Edge 浏览器。");
   if(progress!=null)progress("微软网页组件安装完成。",35);
  }
 }
 static class SteamFolders {
  public static string Find() {
   var steam=new List<string>();
   foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine})foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
    try {using(var r=RegistryKey.OpenBaseKey(hive,view))using(var k=r.OpenSubKey(@"Software\Valve\Steam")){if(k!=null)foreach(var n in new[]{"SteamPath","InstallPath"}){var value=Convert.ToString(k.GetValue(n));if(value!="")steam.Add(value);}}}catch{}
   }
   steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));var roots=new List<string>(steam);
   foreach(string p in steam){string vdf=Path.Combine(p,@"steamapps\libraryfolders.vdf");if(!File.Exists(vdf))continue;try{foreach(System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\"")){roots.Add(m.Groups[1].Value.Replace(@"\\",@"\"));}}catch{}}
   foreach(string p in roots){string candidate=Path.Combine(p,@"steamapps\common\Probably Stolen Demo");if(File.Exists(Path.Combine(candidate,"Probably Stolen.exe"))&&File.Exists(Path.Combine(candidate,"GameAssembly.dll")))return candidate;}
   return "";
  }
 }
 sealed class SetupForm:Form {
  readonly Color background=Color.FromArgb(10,27,29),panel=Color.FromArgb(23,47,51),ink=Color.FromArgb(231,232,200),muted=Color.FromArgb(154,176,167),green=Color.FromArgb(128,201,184);
  TextBox game;Label hint,status;ProgressBar progress;Button install,browse;LinkLabel more,license;bool busy,renderTesting;readonly StringBuilder log=new StringBuilder();
  protected override bool ShowWithoutActivation { get { return renderTesting; } }
  public SetupForm() {
   Text="深空手机 · SpacePhone 3.1 一键安装";Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);ClientSize=new Size(900,615);MinimumSize=new Size(916,654);MaximumSize=new Size(1100,760);BackColor=background;ForeColor=ink;StartPosition=FormStartPosition.CenterScreen;Font=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel);AutoScaleMode=AutoScaleMode.None;
   var logo=new PictureBox{Location=new Point(30,28),Size=new Size(64,72),Image=PhoneMark(),SizeMode=PictureBoxSizeMode.CenterImage};Controls.Add(logo);
   Label title=LabelAt("深空手机，一键装好。",112,29,700,48,27,ink,true);Controls.Add(title);Controls.Add(LabelAt("SPACEPHONE 3.1 / WINDOWS STEAM DEMO",114,84,700,24,10,green,false));
   var location=new Panel{Location=new Point(30,135),Size=new Size(840,132),BackColor=panel,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(location);
   location.Controls.Add(LabelAt("游戏安装位置",20,15,770,26,13,ink,true));
   game=new TextBox{Location=new Point(20,51),Size=new Size(646,30),Font=new Font(Font.FontFamily,14,FontStyle.Regular,GraphicsUnit.Pixel),BackColor=background,ForeColor=ink,BorderStyle=BorderStyle.FixedSingle,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};location.Controls.Add(game);
   browse=ButtonAt("选择位置…",684,47,136,39,false);browse.Anchor=AnchorStyles.Top|AnchorStyles.Right;location.Controls.Add(browse);
   hint=LabelAt("自动寻找 Steam 游戏，也可以手动选择文件夹。",20,96,790,25,9,muted,false);location.Controls.Add(hint);
   var contents=new Panel{Location=new Point(30,287),Size=new Size(840,83),BackColor=panel,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(contents);
   contents.Controls.Add(LabelAt("手机、加载器、网页组件，都在这个包里。",20,14,790,27,13,ink,true));contents.Controls.Add(LabelAt("自动补齐缺少的组件，已有 Mod、音乐与登录资料会保留。",20,46,790,24,10,muted,false));
   status=LabelAt("先正常保存、退出游戏，然后点击安装。",30,390,840,52,12,ink,false);status.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(status);
   progress=new ProgressBar{Location=new Point(30,453),Size=new Size(840,9),Minimum=0,Maximum=100,Style=ProgressBarStyle.Continuous,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};Controls.Add(progress);
   license=new LinkLabel{Text="点击安装表示同意随附的组件许可 · 查看许可",Location=new Point(30,474),Size=new Size(840,23),LinkColor=muted,ActiveLinkColor=green,Font=new Font(Font.FontFamily,12,FontStyle.Regular,GraphicsUnit.Pixel)};Controls.Add(license);
   install=ButtonAt("安装深空手机",30,507,840,51,true);install.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(install);
   Controls.Add(LabelAt("安装后进入店铺，按 F6 打开手机。",30,576,640,25,10,muted,false));
   more=new LinkLabel{Text="更多选项 ▾",Location=new Point(756,576),Size=new Size(114,24),TextAlign=ContentAlignment.MiddleRight,LinkColor=green,ActiveLinkColor=ink,Anchor=AnchorStyles.Top|AnchorStyles.Right};Controls.Add(more);
   string detected=Program.GameArgument!=""?Program.GameArgument:SteamFolders.Find();game.Text=detected;hint.Text=detected==""?"没有找到默认游戏，请点“选择位置”。":"已找到游戏位置；确认后点击安装。";
   browse.Click+=(s,e)=>{using(var d=new FolderBrowserDialog()){d.Description="选择 Probably Stolen.exe 所在的游戏文件夹";d.ShowNewFolderButton=false;if(Directory.Exists(game.Text))d.SelectedPath=game.Text;if(d.ShowDialog(this)==DialogResult.OK){game.Text=d.SelectedPath;hint.Text="已选择游戏文件夹，点击安装即可。";}}};
   install.Click+=(s,e)=>BeginAction("Install");license.LinkClicked+=(s,e)=>OpenLocal(Path.Combine(Program.Root,@"licenses\许可说明.html"));
   var menu=new ContextMenuStrip();menu.Items.Add("回退上次安装",null,(s,e)=>{if(MessageBox.Show(this,"恢复这套一键工具上次安装前的版本？","回退",MessageBoxButtons.OKCancel)==DialogResult.OK)BeginAction("Rollback");});menu.Items.Add("移除手机 Mod",null,(s,e)=>{if(MessageBox.Show(this,"移除手机文件，保留其他 Mod 和共享组件？","移除",MessageBoxButtons.OKCancel)==DialogResult.OK)BeginAction("Uninstall");});menu.Items.Add("查看安装记录",null,(s,e)=>ShowLog());menu.Items.Add("查看简单教程",null,(s,e)=>OpenLocal(Path.Combine(Program.Root,"三步安装.html")));more.LinkClicked+=(s,e)=>menu.Show(more,new Point(0,more.Height));
   FormClosing+=(s,e)=>{if(busy)e.Cancel=true;};
   Shown+=(s,e)=>{if(Program.ResumeAction!=""){string action=Program.ResumeAction;Program.ResumeAction="";BeginAction(action);}};
  }
  Label LabelAt(string text,int x,int y,int w,int h,float size,Color color,bool bold){return new Label{Text=text,Location=new Point(x,y),Size=new Size(w,h),ForeColor=color,Font=new Font("Microsoft YaHei UI",size*1.333f,bold?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel),AutoEllipsis=false};}
  Button ButtonAt(string text,int x,int y,int w,int h,bool main){var b=new Button{Text=text,Location=new Point(x,y),Size=new Size(w,h),BackColor=main?green:Color.FromArgb(41,68,72),ForeColor=main?background:ink,FlatStyle=FlatStyle.Flat,Font=new Font("Microsoft YaHei UI",main?20:14,main?FontStyle.Bold:FontStyle.Regular,GraphicsUnit.Pixel),Cursor=Cursors.Hand};b.FlatAppearance.BorderSize=0;return b;}
  Bitmap PhoneMark(){var b=new Bitmap(64,72);using(var g=Graphics.FromImage(b)){g.SmoothingMode=SmoothingMode.AntiAlias;using(var p=new Pen(green,3)){g.DrawRectangle(p,14,3,37,61);g.DrawLine(p,24,56,42,56);}using(var f=new Font("Consolas",20,FontStyle.Bold,GraphicsUnit.Pixel))using(var brush=new SolidBrush(ink))g.DrawString("SP",f,brush,18,23);}return b;}
  void UpdateStatus(string text,int percent){if(IsDisposed)return;if(InvokeRequired){BeginInvoke((Action)(()=>UpdateStatus(text,percent)));return;}status.Text=text;progress.Value=Math.Max(0,Math.Min(100,percent));log.AppendLine(DateTime.Now.ToString("HH:mm:ss")+"  "+text);}
  void SetBusy(bool value,string action="Install"){busy=value;install.Enabled=!value;browse.Enabled=!value;game.ReadOnly=value;more.Enabled=!value;license.Enabled=!value;install.Text=value?(action=="Rollback"?"正在恢复，请稍候…":action=="Uninstall"?"正在移除，请稍候…":"正在安装，请稍候…"):"安装深空手机";}
  async void BeginAction(string action) {
   if(busy)return;if(String.IsNullOrWhiteSpace(game.Text)){status.Text="先点击“选择位置”，选游戏安装文件夹。";return;}
   string selected=game.Text.Trim();
   if(!Program.Admin()) {
    try {var p=new ProcessStartInfo(Application.ExecutablePath,"--resume "+action+" --game "+Program.Quote(selected)){UseShellExecute=true,Verb="runas",WorkingDirectory=Program.Root};Process.Start(p);Close();}catch(System.ComponentModel.Win32Exception e){status.Text=e.NativeErrorCode==1223?"已取消管理员授权，没有安装。":"无法取得安装权限："+e.Message;}return;
   }
   SetBusy(true,action);UpdateStatus("正在检查游戏和安装文件…",1);
   try {
    Dictionary<string,object> result=await Task.Run(()=>{
     if(action=="Install"){
      Backend.Run("Check",selected,(m,p)=>UpdateStatus(m,Math.Min(25,p/4)));
      WebRuntime.Ensure(UpdateStatus);
      return Backend.Run("Install",selected,(m,p)=>UpdateStatus(m,35+p*60/100));
     }
     return Backend.Run(action,selected,(m,p)=>UpdateStatus(m,p));
    });
    string message=Convert.ToString(result["message"]);UpdateStatus(action=="Install"?"安装完成！进入店铺，按 F6 打开深空手机。":message,100);log.AppendLine(Program.Json.Serialize(result));
   }catch(Exception e){string brief=e.Message;if(brief.Contains("恢复未完成")||brief.Contains("安全恢复也未完成")||brief.Contains("先不要启动"))brief="自动恢复未完成，请先不要启动游戏。点“更多选项 → 查看安装记录”，按备份位置恢复。";else if(brief.Length>110)brief="操作未完成。点“更多选项 → 查看安装记录”查看完整原因和恢复信息。";UpdateStatus(brief,0);log.AppendLine(e.ToString());}
   finally{SetBusy(false);}
  }
  void OpenLocal(string path){if(!File.Exists(path)){MessageBox.Show(this,"请完整解压 ZIP，帮助文件现在不在安装器旁边。");return;}Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
  void ShowLog(){using(var window=new Form{Text="安装记录",ClientSize=new Size(760,420),StartPosition=FormStartPosition.CenterParent}){var box=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Font=new Font("Microsoft YaHei UI",14,FontStyle.Regular,GraphicsUnit.Pixel),Text=log.Length>0?log.ToString():"尚未安装。此程序只在你点击安装后修改目标游戏文件。"};window.Controls.Add(box);window.ShowDialog(this);}}
  public void RenderTest(string report) {
   renderTesting=true;ShowInTaskbar=false;Opacity=0;StartPosition=FormStartPosition.Manual;Location=new Point(-20000,-20000);FormBorderStyle=FormBorderStyle.None;MinimumSize=Size.Empty;ClientSize=new Size(900,615);Show();Application.DoEvents();CreateControl();PerformLayout();foreach(Control c in Controls)c.CreateControl();
   string png=Path.ChangeExtension(report,".png");using(var image=new Bitmap(ClientSize.Width,ClientSize.Height)){DrawToBitmap(image,new Rectangle(Point.Empty,ClientSize));image.Save(png,System.Drawing.Imaging.ImageFormat.Png);}
   var checks=new[]{Program.Quote(@"C:\Steam Games\Demo")=="\"C:\\Steam Games\\Demo\"",Program.Quote("trailing\\")=="\"trailing\\\\\"",WebRuntime.ValidVersion("145.0.3800.0"),!WebRuntime.ValidVersion("0.0.0.0"),!WebRuntime.ValidVersion(""),WebRuntime.NeedsInstall(""),!WebRuntime.NeedsInstall("145.0.0.0"),game.Width>0,install.Width>0,progress.Width>0};
   foreach(bool value in checks)if(!value)throw new Exception("EXE self-test failed");
   File.WriteAllText(report,Program.Json.Serialize(new {status="PASS",checks=checks.Length,exeGuiRendered=true,nativeMouseTest=false,systemRuntimeInstalled=false,gameFilesWritten=false,installedWebViewVersion=WebRuntime.InstalledVersion(),gameFolder=game.Text,clientWidth=ClientSize.Width,clientHeight=ClientSize.Height}),new UTF8Encoding(false));
   Hide();
  }
 }
}

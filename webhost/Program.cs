using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SpacePhone.WebViewHost;
internal record Command(string Action,long Parent=0,int X=0,int Y=0,int Width=360,int Height=490,string Platform="");
internal record Reply(string State,string Detail);
internal static class Program
{
    [STAThread] private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string Arg(string key,string fallback=""){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        string profile=Arg("--profile",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpacePhone","WebProfiles"));
        Application.Run(new Host(Arg("--pipe"),profile,Arg("--selftest"),Arg("--platform"),Arg("--test-capture-dir")));
    }
}
internal sealed class Host:Form
{
    private readonly WebView2 browser=new(){Dock=DockStyle.Fill};
    private readonly string pipeName,profile,testPath,testPlatform,captureDir;
    private NamedPipeClientStream? pipe;
    private StreamWriter? writer;
    private CoreWebView2Environment? environment;
    private readonly System.Windows.Forms.Timer parentWatch=new(){Interval=500};
    private readonly List<Form> popups=new();
    private Form? expandedWindow;
    private WebView2? expandedView;
    private bool suppressReturn,closing;
    private IntPtr parent;
    private string platform="douyin";
    private bool attached;
    private bool navigated;
    private Command? last;
    [DllImport("user32.dll")] private static extern IntPtr SetParent(IntPtr child,IntPtr parent);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd,int index,IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int w,int h,uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    public Host(string pipeName,string profile,string testPath,string testPlatform,string captureDir)
    {
        this.pipeName=pipeName;this.profile=profile;this.testPath=testPath;this.testPlatform=testPlatform;this.captureDir=captureDir;
        Text="深空手机 · 官方网页";ClientSize=new Size(900,720);FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-30000,-30000);
        Controls.Add(browser);
        Shown+=async(_,_)=>await Initialize();
        FormClosed+=(_,_)=>{closing=true;foreach(var popup in popups.ToArray())popup.Close();pipe?.Dispose();parentWatch.Dispose();};
        parentWatch.Tick+=(_,_)=>{if(parent!=IntPtr.Zero && !IsWindow(parent))Close();};
    }
    private async Task Initialize()
    {
        try
        {
            Directory.CreateDirectory(profile);
            environment=await CoreWebView2Environment.CreateAsync(null,profile,new CoreWebView2EnvironmentOptions{AdditionalBrowserArguments="--autoplay-policy=no-user-gesture-required"});
            await browser.EnsureCoreWebView2Async(environment);Configure(browser,!string.IsNullOrEmpty(testPath)&&string.IsNullOrEmpty(testPlatform));
            browser.ZoomFactor=.8;
            browser.CoreWebView2.NavigationCompleted+=async(_,e)=>await Send(e.IsSuccess?"ready":"page_error",e.IsSuccess?"官方网页已就绪，请在网页内登录":"网站加载失败："+e.WebErrorStatus);
            browser.CoreWebView2.NewWindowRequested+=async(_,e)=>
            {
                var deferral=e.GetDeferral();e.Handled=true;
                try
                {
                    var popup=new Form{Text="平台官方登录",Width=980,Height=780,StartPosition=FormStartPosition.CenterScreen};
                    popups.Add(popup);popup.FormClosed+=(_,_)=>popups.Remove(popup);
                    var view=new WebView2{Dock=DockStyle.Fill};popup.Controls.Add(view);popup.Show();
                    await view.EnsureCoreWebView2Async(environment);Configure(view);e.NewWindow=view.CoreWebView2;
                }
                finally{deferral.Complete();}
            };
            parentWatch.Start();
            if(!string.IsNullOrEmpty(testPath)){await SelfTest();return;}
            pipe=new NamedPipeClientStream(".",pipeName,PipeDirection.InOut,PipeOptions.Asynchronous);await pipe.ConnectAsync(15000);
            writer=new StreamWriter(pipe){AutoFlush=true};var reader=new StreamReader(pipe);
            await Send("host_ready","网页引擎已启动");
            while(!IsDisposed && await reader.ReadLineAsync() is string line)
            {
                var command=JsonSerializer.Deserialize<Command>(line);if(command!=null)await Apply(command);
            }
            Close();
        }
        catch(Exception ex)
        {
            if(!string.IsNullOrEmpty(testPath))File.WriteAllText(testPath,"FAIL "+ex);
            else await Send("host_error",ex.Message);
            Close();
        }
    }
    private static void Configure(WebView2 view,bool fixture=false)
    {
        var settings=view.CoreWebView2.Settings;settings.IsPasswordAutosaveEnabled=false;settings.IsGeneralAutofillEnabled=false;settings.AreDevToolsEnabled=false;settings.IsStatusBarEnabled=false;
        view.CoreWebView2.NavigationStarting+=(_,e)=>{if(!fixture&&Uri.TryCreate(e.Uri,UriKind.Absolute,out var u)&&u.Scheme!="https"&&u.Scheme!="http"&&u.Scheme!="about")e.Cancel=true;};
        view.CoreWebView2.DownloadStarting+=(_,e)=>e.Cancel=true;
    }
    private async Task Apply(Command command)
    {
        switch(command.Action)
        {
            case "show":
                if(command.Platform!="douyin"&&command.Platform!="tiktok")throw new InvalidDataException("Unsupported platform");
                parent=new IntPtr(command.Parent);if(!IsWindow(parent))throw new InvalidOperationException("Game window not available");
                CloseExpanded();last=command;Attach(command);PhoneZoom(command.Platform,command.Width);
                if(!navigated || platform!=command.Platform){platform=command.Platform;navigated=true;browser.CoreWebView2.Navigate(Url(platform));}
                browser.CoreWebView2.Resume();browser.CoreWebView2.IsMuted=false;Show();await Send("attached","官方网页已嵌入手机");break;
            case "bounds":if(attached && command.Width>0){SetWindowPos(Handle,IntPtr.Zero,command.X,command.Y,Math.Clamp(command.Width,100,2000),Math.Clamp(command.Height,100,1600),0x14);PhoneZoom(platform,command.Width);last=command;}break;
            case "hide":CloseExpanded();browser.CoreWebView2.IsMuted=true;Hide();try{await browser.CoreWebView2.TrySuspendAsync();}catch{}await Send("hidden","网页已暂停");break;
            case "reload":browser.Reload();break;
            case "back":if(browser.CanGoBack)browser.GoBack();break;
            case "expand":await Expand();break;
            case "attach":CloseExpanded();await ReturnToPhone();break;
            case "logout":foreach(var popup in popups.ToArray())popup.Close();await browser.CoreWebView2.Profile.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite);browser.Reload();await Send("logged_out","网页登录数据已清除");break;
            case "capture":
                if(!string.IsNullOrEmpty(captureDir)){await Task.Delay(6500);using(var output=File.Create(Path.Combine(captureDir,"embedded-"+platform+".png")))await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);var viewport=await browser.CoreWebView2.ExecuteScriptAsync("JSON.stringify({w:innerWidth,h:innerHeight,dpr:devicePixelRatio})");File.WriteAllText(Path.Combine(captureDir,"embedded-"+platform+".json"),JsonSerializer.Serialize(new{Size=Size.ToString(),ClientSize=ClientSize.ToString(),BrowserSize=browser.ClientSize.ToString(),Zoom=browser.ZoomFactor,DeviceDpi,Bounds=last,Viewport=viewport}));await Send("captured","隔离测试网页截图完成");}break;
            case "capture_expanded":
                if(!string.IsNullOrEmpty(captureDir)&&expandedView!=null){await Task.Delay(6500);using(var output=File.Create(Path.Combine(captureDir,"expanded-"+platform+".png")))await expandedView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,output);await Send("captured_expanded","隔离测试大窗口截图完成");}break;
            case "close":Close();break;
        }
    }
    private void Attach(Command command)
    {
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;
        if(GetParent(Handle)!=parent)SetParent(Handle,parent);SetWindowLongPtr(Handle,-16,new IntPtr(unchecked((long)0x56000000)));
        if(GetParent(Handle)!=parent)throw new InvalidOperationException("网页窗口无法嵌入游戏，请使用放大窗口");
        SetWindowPos(Handle,IntPtr.Zero,command.X,command.Y,Math.Clamp(command.Width,100,2000),Math.Clamp(command.Height,100,1600),0x14);
        attached=true;
    }
    private void CloseExpanded()
    {
        suppressReturn=true;expandedWindow?.Close();expandedWindow=null;expandedView=null;suppressReturn=false;
    }
    private async Task ReturnToPhone()
    {
        if(closing||IsDisposed||last==null||!IsWindow(parent))return;
        Attach(last);PhoneZoom(platform,last.Width);browser.CoreWebView2.Resume();browser.CoreWebView2.IsMuted=false;Show();browser.Reload();
        SetForegroundWindow(parent);await Send("attached","官方网页已返回手机");
    }
    private async Task Expand()
    {
        if(expandedWindow!=null){expandedWindow.Activate();await Send("expanded","网页已放大");return;}
        var working=Screen.PrimaryScreen!.WorkingArea;
        var window=new Form{Text="深空手机 · "+(platform=="douyin"?"抖音":"TikTok")+" 官方网页",Width=Math.Min(1240,working.Width-60),Height=Math.Min(900,working.Height-60),StartPosition=FormStartPosition.CenterScreen};
        var view=new WebView2{Dock=DockStyle.Fill};
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Top,Height=48,BackColor=Color.FromArgb(18,40,46),Padding=new Padding(8)};
        var back=new Button{Text="回到游戏手机",Width=160,Height=30};back.Click+=(_,_)=>window.Close();toolbar.Controls.Add(back);
        // Separate controls keep the game-parented host stable and share the same normal site profile.
        window.Controls.Add(view);window.Controls.Add(toolbar);expandedWindow=window;expandedView=view;popups.Add(window);
        window.FormClosed+=async(_,_)=>{popups.Remove(window);expandedWindow=null;expandedView=null;if(!suppressReturn&&!closing)await ReturnToPhone();};
        window.Show();await view.EnsureCoreWebView2Async(environment);Configure(view);view.ZoomFactor=platform=="douyin"?.55:.6;
        view.CoreWebView2.NewWindowRequested+=(_,e)=>{e.Handled=true;if(e.Uri.StartsWith("https://",StringComparison.OrdinalIgnoreCase))view.CoreWebView2.Navigate(e.Uri);};
        browser.CoreWebView2.IsMuted=true;Hide();
        view.CoreWebView2.Navigate(browser.Source?.AbsoluteUri??Url(platform));
        attached=false;window.Activate();await Send("expanded","官方网页已放大，点击回到游戏手机可返回");
    }
    private void PhoneZoom(string service,int width)
    {
        // Keep the official desktop layout visible; narrow desktop viewports can show app-download pages.
        double zoom=Math.Clamp(width/Math.Max(1,DeviceDpi/96.0)/(service=="douyin"?1280.0:900.0),.25,.85);
        if(Math.Abs(browser.ZoomFactor-zoom)>.01)browser.ZoomFactor=zoom;
    }
    private Task Send(string state,string detail)=>writer?.WriteLineAsync(JsonSerializer.Serialize(new Reply(state,detail)))??Task.CompletedTask;
    private static string Url(string platform)=>platform=="tiktok"?"https://www.tiktok.com/foryou":"https://www.douyin.com/";
    private async Task SelfTest()
    {
        var completed=new TaskCompletionSource<bool>();string error="";browser.CoreWebView2.NavigationCompleted+=(_,e)=>{error=e.WebErrorStatus.ToString();if(e.IsSuccess)completed.TrySetResult(true);};
        if(string.IsNullOrEmpty(testPlatform))browser.CoreWebView2.NavigateToString("<html><body style='background:#12282e;color:#d7ddbb;font:24px sans-serif'><h1>SpacePhone official-page host</h1><button id='probe' onclick='this.textContent=\"clicked\"'>Interactive test</button></body></html>");
        else browser.CoreWebView2.Navigate(Url(testPlatform));
        if(await Task.WhenAny(completed.Task,Task.Delay(30000))!=completed.Task)throw new TimeoutException("WebView navigation timed out: "+error);
        bool navigation=await completed.Task;await Task.Delay(1500);
        string click=string.IsNullOrEmpty(testPlatform)?await browser.CoreWebView2.ExecuteScriptAsync("document.getElementById('probe').click();document.getElementById('probe').textContent"):"website";
        using(var image=File.Create(testPath+".png"))await browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png,image);
        File.WriteAllText(testPath,JsonSerializer.Serialize(new{Runtime=environment!.BrowserVersionString,Navigation=navigation,Error=error,Interaction=click,PasswordAutosave=browser.CoreWebView2.Settings.IsPasswordAutosaveEnabled,Platform=testPlatform}));
        Close();
    }
}

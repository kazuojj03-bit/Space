using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using UnityEngine;
namespace SpacePhone;
internal sealed class BrowserClient:IDisposable
{
    private sealed record Command(string Action,long Parent=0,int X=0,int Y=0,int Width=360,int Height=490,string Platform="");
    private sealed record Reply(string State,string Detail);
    private readonly ConcurrentQueue<Command> commands=new();
    private readonly ConcurrentQueue<Reply> replies=new();
    private readonly CancellationTokenSource stop=new();
    private NamedPipeServerStream? pipe;
    private Process? process;
    private bool running;
    private IntPtr gameWindow;
    private string platform="";
    private Rect lastBounds;
    private float nextBounds;
    public string Status="选择平台，使用官方网页登录";
    public string State="idle";
    public bool Attached { get; private set; }
    public int LogoutRevision { get; private set; }
    [StructLayout(LayoutKind.Sequential)] private struct Area{public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr handle,out Area rect);
    private Command Bounds(string action,Rect area,string service="")
    {
        gameWindow=Process.GetCurrentProcess().MainWindowHandle;
        if(gameWindow==IntPtr.Zero || !GetClientRect(gameWindow,out var client))throw new InvalidOperationException("未找到游戏窗口，请重试");
        float sx=(client.Right-client.Left)/(float)Math.Max(1,Screen.width),sy=(client.Bottom-client.Top)/(float)Math.Max(1,Screen.height);
        return new Command(action,gameWindow.ToInt64(),(int)(area.x*sx),(int)((Screen.height-area.y-area.height)*sy),(int)(area.width*sx),(int)(area.height*sy),service);
    }
    public void Open(string service,Rect area)
    {
        if(service!="douyin" && service!="tiktok")return;
        Start();platform=service;lastBounds=area;commands.Enqueue(Bounds("show",area,service));Status="正在打开官方网页…";State="loading";
    }
    public void Tick(Rect? bounds)
    {
        while(replies.TryDequeue(out var reply)){Status=reply.Detail;State=reply.State;if(reply.State=="attached")Attached=true;if(reply.State=="hidden"||reply.State=="expanded")Attached=false;if(reply.State=="logged_out")LogoutRevision++;}
        if(running&&process?.HasExited==true){running=false;Attached=false;State="host_error";Status="网页组件已关闭，重新选择平台即可打开";}
        if(bounds.HasValue && running && Time.unscaledTime>=nextBounds)
        {nextBounds=Time.unscaledTime+.2f;lastBounds=bounds.Value;commands.Enqueue(Bounds("bounds",lastBounds,platform));}
    }
    public void Send(string action){if(running)commands.Enqueue(new Command(action));}
    public void Logout(){Start();commands.Enqueue(new Command("logout"));State="loading";Status="正在清除官方网页会话…";}
    public void Hide()=>Send("hide");
    private void Start()
    {
        if(running && process!=null && !process.HasExited)return;
        string executable=Path.Combine(BepInEx.Paths.PluginPath,"SpacePhone","BrowserHost","SpacePhone.WebViewHost.exe");
        if(!File.Exists(executable))throw new FileNotFoundException("网页组件未安装",executable);
        string? isolatedRoot=BrowserIsolationRoot();
        string pipeName="SpacePhone-"+Environment.ProcessId+"-"+Guid.NewGuid().ToString("N");
        pipe?.Dispose();process?.Dispose();
        var sessionPipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);pipe=sessionPipe;
        var start=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable)!};
        start.ArgumentList.Add("--pipe");start.ArgumentList.Add(pipeName);
        if(isolatedRoot!=null){start.ArgumentList.Add("--profile");start.ArgumentList.Add(Path.Combine(isolatedRoot,"browser-test-profile"));start.ArgumentList.Add("--test-capture-dir");start.ArgumentList.Add(isolatedRoot);}
        // Browser owns normal site sessions. No account/token/password is copied into game saves.
        var sessionProcess=Process.Start(start)??throw new InvalidOperationException("无法启动网页组件");process=sessionProcess;running=true;
        _=Task.Run(async()=>
        {
            try
            {
                await sessionPipe.WaitForConnectionAsync(stop.Token);
                var reader=new StreamReader(sessionPipe);var writer=new StreamWriter(sessionPipe){AutoFlush=true};
                var read=Task.Run(async()=>{try{while(!stop.IsCancellationRequested && await reader.ReadLineAsync() is string line){var result=JsonSerializer.Deserialize<Reply>(line);if(result!=null)replies.Enqueue(result);}}catch(Exception ex){if(!stop.IsCancellationRequested)replies.Enqueue(new Reply("host_error","网页连接已关闭："+ex.Message));}});
                while(!stop.IsCancellationRequested && !sessionProcess.HasExited && !read.IsCompleted)
                {while(commands.TryDequeue(out var command))await writer.WriteLineAsync(JsonSerializer.Serialize(command));await Task.Delay(25,stop.Token);}
            }
            catch(Exception ex){if(!stop.IsCancellationRequested)replies.Enqueue(new Reply("host_error","网页组件："+ex.Message));}
            finally{if(ReferenceEquals(process,sessionProcess))running=false;}
        });
    }
    private static string? BrowserIsolationRoot()
    {
        string actual=Path.GetFullPath(BepInEx.Paths.GameRootPath).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        string marker=Path.Combine(actual,".spacephone-test-clone");
        string? requested=Environment.GetEnvironmentVariable("SPACEPHONE_TEST_ROOT");
        bool tester=File.Exists(Path.Combine(BepInEx.Paths.PluginPath,"SpacePhone","SpacePhone.IntegrationTests.dll")) ||
            AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="SpacePhone.IntegrationTests");
        bool marked=File.Exists(marker);
        if(string.IsNullOrWhiteSpace(requested))
        {
            if(marked||tester)throw new InvalidOperationException("测试副本未指定隔离网页目录，已阻止网页组件启动；账号资料未读取");
            return null;
        }
        string expected=Path.GetFullPath(requested).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(!string.Equals(actual,expected,StringComparison.OrdinalIgnoreCase)||!marked)
            throw new InvalidOperationException("网页测试隔离目录不匹配，已阻止启动");
        BrowserNoReparse(actual);BrowserNoReparse(marker);
        using var parsed=JsonDocument.Parse(File.ReadAllText(marker));var m=parsed.RootElement;
        string recorded=Path.GetFullPath(m.GetProperty("cloneRoot").GetString()??"").TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        if(m.GetProperty("schemaVersion").GetInt32()!=1 || m.GetProperty("purpose").GetString()!="spacephone-no-save-tests" ||
            m.GetProperty("allowSave").GetBoolean() || !string.Equals(recorded,actual,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("网页测试标记无效，已阻止启动");
        BrowserNoReparse(Path.Combine(actual,"browser-test-profile"));
        return actual;
    }
    private static void BrowserNoReparse(string target)
    {
        string p=Path.GetFullPath(target);
        while(!string.IsNullOrEmpty(p))
        {
            if((File.Exists(p)||Directory.Exists(p)) && (File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidOperationException("网页测试路径不能包含重定向目录");
            string? parent=Path.GetDirectoryName(p);if(parent==p)break;p=parent??"";
        }
    }
    public void Dispose(){Send("close");stop.Cancel();pipe?.Dispose();stop.Dispose();process?.Dispose();}
}

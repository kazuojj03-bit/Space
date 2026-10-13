using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
namespace SpacePhone;
internal static class NativePointer
{
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Area { public int Left,Top,Right,Bottom; }
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window,ref Point point);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window,out Area area);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
    private static bool wasDown;
    public static bool Position(out Vector2 position)
    {
        position=new Vector2(Input.mousePosition.x,Input.mousePosition.y);
        if(!OperatingSystem.IsWindows())return true;
        IntPtr window=GetForegroundWindow();if(window==IntPtr.Zero)return false;
        GetWindowThreadProcessId(window,out uint processId);if(processId!=(uint)Environment.ProcessId)return false;
        if(!GetCursorPos(out var point)||!ScreenToClient(window,ref point)||!GetClientRect(window,out var area))return false;
        int width=area.Right-area.Left,height=area.Bottom-area.Top;if(width<=0||height<=0)return false;
        position=new Vector2((float)point.X/width*Screen.width,(1f-(float)point.Y/height)*Screen.height);return true;
    }
    public static bool Click(out Vector2 position)
    {
        position=new Vector2(Input.mousePosition.x,Input.mousePosition.y);
        if(!OperatingSystem.IsWindows())return Input.GetMouseButtonDown(0);
        bool down=(GetAsyncKeyState(1)&0x8000)!=0;
        bool edge=down&&!wasDown;wasDown=down;
        if(!edge && !Input.GetMouseButtonDown(0))return false;
        return Position(out position);
    }
}

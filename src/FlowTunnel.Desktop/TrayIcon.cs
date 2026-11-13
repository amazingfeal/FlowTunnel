using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace FlowTunnel.Desktop;

public sealed class TrayIcon : IDisposable
{
    private const int GwlWndProc = -4;
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint CallbackMessage = 0x8001;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int IdiApplication = 32512;
    private readonly Window _window;
    private readonly nint _windowHandle;
    private readonly WindowProcedure _procedure;
    private readonly nint _previousProcedure;
    private NotifyIconData _data;

    public TrayIcon(Window window)
    {
        _window = window;
        _windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        _procedure = WindowProc;
        _previousProcedure = SetWindowLongPtr(_windowHandle, GwlWndProc, Marshal.GetFunctionPointerForDelegate(_procedure));
        _data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = CallbackMessage,
            IconHandle = LoadIcon(0, (nint)IdiApplication),
            Tip = "FlowTunnel",
            Info = "",
            InfoTitle = ""
        };
        if (!ShellNotifyIcon(NimAdd, ref _data)) throw new InvalidOperationException("Could not create the FlowTunnel notification icon.");
    }

    public void HideToTray() => _window.AppWindow.Hide();

    private nint WindowProc(nint windowHandle, uint message, nint wParam, nint lParam)
    {
        if (message == CallbackMessage && (int)lParam == WmLeftButtonDoubleClick)
        {
            _window.AppWindow.Show();
            _window.Activate();
            return 0;
        }
        return CallWindowProc(_previousProcedure, windowHandle, message, wParam, lParam);
    }

    public void Dispose()
    {
        ShellNotifyIcon(NimDelete, ref _data);
        if (_previousProcedure != 0) SetWindowLongPtr(_windowHandle, GwlWndProc, _previousProcedure);
        GC.SuppressFinalize(this);
    }

    private delegate nint WindowProcedure(nint windowHandle, uint message, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid GuidItem;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ShellNotifyIcon(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", EntryPoint = "LoadIconW")] private static extern nint LoadIcon(nint instance, nint iconName);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    [DllImport("user32.dll", EntryPoint = "CallWindowProcW")] private static extern nint CallWindowProc(nint previous, nint window, uint message, nint wParam, nint lParam);
}

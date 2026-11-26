using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cueway.Interop;

internal static unsafe partial class Win32
{
    // Window Styles & Extended Styles
    public const uint WS_OVERLAPPED = 0x00000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_SYSMENU = 0x00080000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_MINIMIZEBOX = 0x00020000;
    public const uint WS_MAXIMIZEBOX = 0x00010000;
    public const uint WS_OVERLAPPEDWINDOW = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU | WS_THICKFRAME | WS_MINIMIZEBOX | WS_MAXIMIZEBOX;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CLIPCHILDREN = 0x02000000;
    public const uint WS_CHILD = 0x40000000;
    public const uint WS_TABSTOP = 0x00010000;
    public const uint ES_AUTOHSCROLL = 0x0080;

    public const uint WM_SETFONT = 0x0030;
    public const uint WM_CTLCOLOREDIT = 0x0133;
    public const uint EM_SETLIMITTEXT = 0x00C5;
    public const uint CC_RGBINIT = 0x00000001;
    public const uint CC_FULLOPEN = 0x00000002;
    public const uint CC_ANYCOLOR = 0x00000100;
    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_UPDATENOW = 0x0100;
    public const uint RDW_FRAME = 0x0400;

    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_LAYERED = 0x00080000;
    public const uint WS_EX_TRANSPARENT = 0x00000020;
    public const uint WS_EX_TOPMOST = 0x00000008;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // Class Styles
    public const uint CS_HREDRAW = 0x0002;
    public const uint CS_VREDRAW = 0x0001;
    public const uint CS_DBLCLKS = 0x0008;

    // ShowWindow Commands
    public const int SW_HIDE = 0;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOW = 5;
    public const int SW_MINIMIZE = 6;
    public const int SW_RESTORE = 9;
    public const int SW_SHOWNOACTIVATE = 4;

    // Window Messages
    public const uint WM_CREATE = 0x0001;
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_MOVE = 0x0003;
    public const uint WM_SIZE = 0x0005;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_SETFOCUS = 0x0007;
    public const uint WM_KILLFOCUS = 0x0008;
    public const int WA_INACTIVE = 0;
    public const uint MSGFLT_ALLOW = 1;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_QUIT = 0x0012;
    public const uint WM_ERASEBKGND = 0x0014;
    public const uint WM_SHOWWINDOW = 0x0018;
    public const uint WM_GETMINMAXINFO = 0x0024;
    public const uint WM_PAINT = 0x000F;
    public const uint WM_COMMAND = 0x0111;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_MOUSEMOVE = 0x0200;
    public const uint WM_LBUTTONDOWN = 0x0201;
    public const uint WM_LBUTTONUP = 0x0202;
    public const uint WM_LBUTTONDBLCLK = 0x0203;
    public const uint WM_RBUTTONDOWN = 0x0204;
    public const uint WM_RBUTTONUP = 0x0205;
    public const uint WM_MOUSEWHEEL = 0x020A;
    public const uint WM_CAPTURECHANGED = 0x0215;
    public const uint WM_MOUSELEAVE = 0x02A3;
    public const uint WM_POWERBROADCAST = 0x0218;
    public const uint WM_WTSSESSION_CHANGE = 0x02B1;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APPCOMMAND = 0x0319;
    public const uint WM_SETICON = 0x0080;
    public const uint WM_USER = 0x0400;
    public const uint WM_APP = 0x8000;

    // App Commands & SendMessageTimeout
    public const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
    public const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 12;
    public const int APPCOMMAND_MEDIA_STOP = 13;
    public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // Power & Session Constants
    public const nuint PBT_APMRESUMESUSPEND = 0x0007;
    public const nuint PBT_APMRESUMEAUTOMATIC = 0x0012;
    public const nuint WTS_SESSION_UNLOCK = 0x8;
    public const uint NOTIFY_FOR_THIS_SESSION = 0;

    // Hooks
    public const int WH_KEYBOARD_LL = 13;
    public const int WH_MOUSE_LL = 14;
    public const uint WM_KEYDOWN = 0x0100;
    public const uint WM_KEYUP = 0x0101;
    public const uint WM_SYSKEYDOWN = 0x0104;
    public const uint WM_SYSKEYUP = 0x0105;

    // Virtual Keys
    public const int VK_BACK = 0x08;
    public const int VK_TAB = 0x09;
    public const int VK_RETURN = 0x0D;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12; // Alt
    public const int VK_PAUSE = 0x13;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_SPACE = 0x20;
    public const int VK_PRIOR = 0x21;
    public const int VK_NEXT = 0x22;
    public const int VK_END = 0x23;
    public const int VK_HOME = 0x24;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;
    public const int VK_SNAPSHOT = 0x2C;
    public const int VK_INSERT = 0x2D;
    public const int VK_DELETE = 0x2E;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;
    public const int VK_APPS = 0x5D;
    public const int VK_NUMPAD0 = 0x60;
    public const int VK_NUMPAD9 = 0x69;
    public const int VK_MULTIPLY = 0x6A;
    public const int VK_ADD = 0x6B;
    public const int VK_SUBTRACT = 0x6D;
    public const int VK_DECIMAL = 0x6E;
    public const int VK_DIVIDE = 0x6F;
    public const int VK_F1 = 0x70;
    public const int VK_F24 = 0x87;
    public const int VK_SCROLL = 0x91;
    public const int VK_LSHIFT = 0xA0;
    public const int VK_RSHIFT = 0xA1;
    public const int VK_LCONTROL = 0xA2;
    public const int VK_RCONTROL = 0xA3;
    public const int VK_LMENU = 0xA4;
    public const int VK_RMENU = 0xA5;
    public const int VK_M = 0x4D;
    public const int VK_S = 0x53;
    public const int VK_C = 0x43;
    public const int VK_MEDIA_NEXT_TRACK = 0xB0;
    public const int VK_MEDIA_PREV_TRACK = 0xB1;
    public const int VK_MEDIA_STOP = 0xB2;
    public const int VK_MEDIA_PLAY_PAUSE = 0xB3;
    public const int VK_OEM_1 = 0xBA;
    public const int VK_OEM_PLUS = 0xBB;
    public const int VK_OEM_COMMA = 0xBC;
    public const int VK_OEM_MINUS = 0xBD;
    public const int VK_OEM_PERIOD = 0xBE;
    public const int VK_OEM_2 = 0xBF;
    public const int VK_OEM_3 = 0xC0;
    public const int VK_OEM_4 = 0xDB;
    public const int VK_OEM_5 = 0xDC;
    public const int VK_OEM_6 = 0xDD;
    public const int VK_OEM_7 = 0xDE;
    public const int VK_OEM_8 = 0xDF;
    public const int VK_OEM_102 = 0xE2;

    public const uint LLKHF_INJECTED = 0x00000010;

    // DWM Attributes
    public const uint DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const uint DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    public const uint DWMWA_BORDER_COLOR = 34;
    public const uint DWMWA_CAPTION_COLOR = 35;
    public const uint DWMWA_TEXT_COLOR = 36;
    public const int DWMWCP_ROUND = 2;

    // Tray NotifyIcon
    public const uint NIM_ADD = 0x00000000;
    public const uint NIM_MODIFY = 0x00000001;
    public const uint NIM_DELETE = 0x00000002;
    public const uint NIM_SETVERSION = 0x00000004;
    public const uint NOTIFYICON_VERSION_4 = 4;
    public const uint NIF_MESSAGE = 0x00000001;
    public const uint NIF_ICON = 0x00000002;
    public const uint NIF_TIP = 0x00000004;
    public const uint NIF_SHOWTIP = 0x00000080;

    // Icons & Cursors
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const uint DI_NORMAL = 0x0003;
    public const nint IDC_ARROW = 32512;
    public const nint IDC_HAND = 32649;
    public const nint IDI_APPLICATION = 32512;
    public const uint IMAGE_ICON = 1;
    public const uint LR_DEFAULTCOLOR = 0x0000;

    // SHGetFileInfo
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_LARGEICON = 0x000000000;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    // GDI / Font / Text
    public const uint SRCCOPY = 0x00CC0020;
    public const int TRANSPARENT = 1;
    public const uint DT_LEFT = 0x00000000;
    public const uint DT_CENTER = 0x00000001;
    public const uint DT_RIGHT = 0x00000002;
    public const uint DT_VCENTER = 0x00000004;
    public const uint DT_SINGLELINE = 0x00000020;
    public const uint DT_WORDBREAK = 0x00000010;
    public const uint DT_END_ELLIPSIS = 0x00008000;
    public const uint DT_NOPREFIX = 0x00000800;
    public const uint DT_CALCRECT = 0x00000400;
    public const int CLEARTYPE_QUALITY = 5;
    public const int FW_NORMAL = 400;
    public const int FW_MEDIUM = 500;
    public const int FW_SEMIBOLD = 600;
    public const int FW_BOLD = 700;

    // SetWindowPos
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public static readonly nint HWND_TOPMOST = new(-1);
    public static readonly nint HWND_NOTOPMOST = new(-2);

    // Registry & Token Elevation
    public static readonly nint HKEY_CURRENT_USER = new(unchecked((nint)0x80000001));
    public const uint KEY_READ = 0x20019;
    public const uint KEY_SET_VALUE = 0x0002;
    public const uint REG_SZ = 1;
    public const uint TOKEN_QUERY = 0x0008;
    public const uint TokenElevation = 20;

    // Layered Window
    public const uint ULW_ALPHA = 0x00000002;
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    // Menu
    public const uint MF_STRING = 0x00000000;
    public const uint MF_SEPARATOR = 0x00000800;
    public const uint MF_CHECKED = 0x00000008;
    public const uint MF_UNCHECKED = 0x00000000;
    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;
    public const uint TPM_NONOTIFY = 0x0080;

    // Process & Monitor
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    public static readonly nint INVALID_HANDLE_VALUE = new(-1);
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    public const uint TME_LEAVE = 0x00000002;
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const uint GW_OWNER = 4;
    public const uint CF_UNICODETEXT = 13;
    public const uint GMEM_MOVEABLE = 0x0002;

    public static readonly nint HWND_MESSAGE = new(-3);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int Width;
        public int Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint hwnd;
        public uint message;
        public nuint wParam;
        public nint lParam;
        public uint time;
        public POINT pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nuint, nint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public RECT rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TRACKMOUSEEVENT
    {
        public uint cbSize;
        public uint dwFlags;
        public nint hwndTrack;
        public uint dwHoverTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public nint hIcon;
        public fixed char szTip[128];
        public uint dwState;
        public uint dwStateMask;
        public fixed char szInfo[256];
        public uint uTimeoutOrVersion;
        public fixed char szInfoTitle[64];
        public uint dwInfoFlags;
        public Guid guidItem;
        public nint hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NOTIFYICONIDENTIFIER
    {
        public uint cbSize;
        public nint hWnd;
        public uint uID;
        public Guid guidItem;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public uint cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SHFILEINFOW
    {
        public nint hIcon;
        public int iIcon;
        public uint dwAttributes;
        public fixed char szDisplayName[260];
        public fixed char szTypeName[80];
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public nuint th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    // Kernel32
    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial nint CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32FirstW(nint hSnapshot, PROCESSENTRY32W* lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32NextW(nint hSnapshot, PROCESSENTRY32W* lppe);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessWorkingSetSize(nint hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint GetModuleHandleW(string? lpModuleName);

    [LibraryImport("kernel32.dll")]
    public static partial uint GetCurrentThreadId();

    [LibraryImport("kernel32.dll")]
    public static partial ulong GetTickCount64();

    [LibraryImport("kernel32.dll")]
    public static partial nint OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(nint hObject);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageNameW(nint hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterApplicationRestart(string? pwzCommandline, uint dwFlags);

    // User32 - Windowing & Messages
    [LibraryImport("user32.dll")]
    public static partial ushort RegisterClassExW(WNDCLASSEXW* lpwcx);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X, int Y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint DefWindowProcW(nint hWnd, uint Msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial int GetMessageW(MSG* lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(MSG* lpMsg);

    [LibraryImport("user32.dll")]
    public static partial nint DispatchMessageW(MSG* lpMsg);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessageW(nint hWnd, uint Msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hWnd, uint Msg, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageTimeoutW(
        nint hWnd, uint Msg, nuint wParam, nint lParam,
        uint fuFlags, uint uTimeout, out nuint lpdwResult);

    [LibraryImport("user32.dll")]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RedrawWindow(nint hWnd, RECT* lprcUpdate, nint hrgnUpdate, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BringWindowToTop(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(uint dwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint FindWindowExW(nint hWndParent, nint hWndChildAfter, string? lpszClass, string? lpszWindow);

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowLongW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hWnd, RECT* lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateLayeredWindow(nint hwnd, nint hdcDst, POINT* pptDst, SIZE* psize, nint hdcSrc, POINT* pptSrc, uint crKey, BLENDFUNCTION* pblend, uint dwFlags);

    [LibraryImport("user32.dll")]
    public static partial nint BeginPaint(nint hWnd, PAINTSTRUCT* lpPaint);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(nint hWnd, PAINTSTRUCT* lpPaint);

    [LibraryImport("user32.dll")]
    public static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    [LibraryImport("user32.dll")]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromWindow(nint hwnd, uint dwFlags);

    [LibraryImport("user32.dll")]
    public static partial nint MonitorFromPoint(POINT pt, uint dwFlags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetMonitorInfoW(nint hMonitor, MONITORINFO* lpmi);

    [LibraryImport("user32.dll")]
    public static partial nint SetCapture(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TrackMouseEvent(TRACKMOUSEEVENT* lpEventTrack);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("user32.dll")]
    public static partial nint LoadCursorW(nint hInstance, nint lpCursorName);

    [LibraryImport("user32.dll")]
    public static partial nint SetCursor(nint hCursor);

    [LibraryImport("user32.dll")]
    public static partial nint LoadIconW(nint hInstance, nint lpIconName);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint LoadImageW(nint hInst, nint name, uint type, int cx, int cy, uint fuLoad);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DrawIconEx(nint hdc, int xLeft, int yTop, nint hIcon, int cxWidth, int cyWidth, uint istepIfAniCur, nint hbrFlickerFreeDraw, uint diFlags);

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(int vKey);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint SetWindowsHookExW(int idHook, delegate* unmanaged[Stdcall]<int, nuint, nint, nint> lpfn, nint hmod, uint dwThreadId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(nint hhk);

    [LibraryImport("user32.dll")]
    public static partial nint CallNextHookEx(nint hhk, int nCode, nuint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ChangeWindowMessageFilterEx(nint hwnd, uint message, uint action, nint pChangeFilterStruct);

    [LibraryImport("user32.dll")]
    public static partial nint CreatePopupMenu();

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AppendMenuW(nint hMenu, uint uFlags, nuint uIDNewItem, string? lpNewItem);

    [LibraryImport("user32.dll")]
    public static partial int TrackPopupMenuEx(nint hMenu, uint uFlags, int x, int y, nint hwnd, nint lptpm);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyMenu(nint hMenu);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint RegisterWindowMessageW(string lpString);

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int DrawTextW(nint hdc, string lpchText, int cchText, RECT* lprc, uint format);

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hWnd, string lpString);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextW(nint hWnd, char* lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetClassNameW(nint hWnd, char* lpClassName, int nMaxCount);

    [LibraryImport("user32.dll")]
    public static partial nint GetShellWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnumWindows(delegate* unmanaged[Stdcall]<nint, nint, int> lpEnumFunc, nint lParam);

    // GDI32
    [LibraryImport("gdi32.dll")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateDIBSection(nint hdc, BITMAPINFO* pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

    [LibraryImport("gdi32.dll")]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [StructLayout(LayoutKind.Sequential)]
    public struct CHOOSECOLORW
    {
        public uint lStructSize;
        public nint hwndOwner;
        public nint hInstance;
        public uint rgbResult;
        public uint* lpCustColors;
        public uint Flags;
        public nint lCustData;
        public nint lpfnHook;
        public char* lpTemplateName;
    }

    [LibraryImport("comdlg32.dll")]
    public static partial int ChooseColorW(CHOOSECOLORW* lpcc);

    [LibraryImport("gdi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation,
        int cWeight, uint bItalic, uint bUnderline, uint bStrikeOut,
        uint iCharSet, uint iOutPrecision, uint iClipPrecision,
        uint iQuality, uint iPitchAndFamily, string pszFaceName);

    // Shell32
    [LibraryImport("shell32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Shell_NotifyIconW(uint dwMessage, NOTIFYICONDATAW* lpData);

    [LibraryImport("shell32.dll")]
    public static partial int Shell_NotifyIconGetRect(NOTIFYICONIDENTIFIER* identifier, out RECT iconLocation);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint ExtractIconExW(string lpszFile, int nIconIndex, nint* phiconLarge, nint* phiconSmall, uint nIcons);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint SHGetFileInfoW(string pszPath, uint dwFileAttributes, SHFILEINFOW* psfi, uint cbFileInfo, uint uFlags);

    // DWM
    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, uint dwAttribute, void* pvAttribute, uint cbAttribute);

    // WTS
    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSRegisterSessionNotification(nint hWnd, uint dwFlags);

    [LibraryImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WTSUnRegisterSessionNotification(nint hWnd);

    // OLE32
    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    public static partial void CoUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(Guid* rclsid, nint pUnkOuter, uint dwClsContext, Guid* riid, out nint ppv);

    [LibraryImport("ole32.dll")]
    public static partial void CoTaskMemFree(nint pv);

    // Advapi32 (Registry & Elevation)
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegOpenKeyExW(nint hKey, string lpSubKey, uint ulOptions, uint samDesired, out nint phkResult);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegCreateKeyExW(nint hKey, string lpSubKey, uint Reserved, string? lpClass, uint dwOptions, uint samDesired, nint lpSecurityAttributes, out nint phkResult, out uint lpdwDisposition);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegSetValueExW(nint hKey, string lpValueName, uint Reserved, uint dwType, byte* lpData, uint cbData);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegDeleteValueW(nint hKey, string lpValueName);

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int RegQueryValueExW(nint hKey, string lpValueName, nint lpReserved, out uint lpType, byte* lpData, ref uint lpcbData);

    [LibraryImport("advapi32.dll")]
    public static partial int RegCloseKey(nint hKey);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenProcessToken(nint ProcessHandle, uint DesiredAccess, out nint TokenHandle);

    [LibraryImport("advapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTokenInformation(nint TokenHandle, uint TokenInformationClass, void* TokenInformation, uint TokenInformationLength, out uint ReturnLength);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetCurrentProcess();

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalAlloc(uint uFlags, nuint dwBytes);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalLock(nint hMem);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GlobalUnlock(nint hMem);

    [LibraryImport("kernel32.dll")]
    public static partial nint GlobalFree(nint hMem);

    // User32 - Clipboard
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport("user32.dll")]
    public static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    public static bool TrySetClipboardText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        nuint bytes = (nuint)((text.Length + 1) * sizeof(char));
        nint hMem = GlobalAlloc(GMEM_MOVEABLE, bytes);
        if (hMem == 0)
            return false;

        bool transferred = false;
        try
        {
            nint ptr = GlobalLock(hMem);
            if (ptr == 0)
                return false;

            try
            {
                var dst = new Span<char>((char*)ptr, text.Length + 1);
                text.AsSpan().CopyTo(dst);
                dst[text.Length] = '\0';
            }
            finally
            {
                GlobalUnlock(hMem);
            }

            if (!OpenClipboard(0))
                return false;

            try
            {
                EmptyClipboard();
                if (SetClipboardData(CF_UNICODETEXT, hMem) != 0)
                {
                    transferred = true;
                    return true;
                }
                return false;
            }
            finally
            {
                CloseClipboard();
            }
        }
        finally
        {
            if (!transferred)
                GlobalFree(hMem);
        }
    }

    public static uint ColorRef(byte r, byte g, byte b) => (uint)(r | (g << 8) | (b << 16));

    public static string? TryGetProcessImagePath(uint pid)
    {
        if (pid == 0) return null;
        nint hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProc == 0) return null;
        try
        {
            const int maxChars = 1024;
            char* buf = stackalloc char[maxChars];
            uint size = maxChars;
            if (QueryFullProcessImageNameW(hProc, 0, buf, ref size) && size > 0)
            {
                return new string(buf, 0, (int)size);
            }
            return null;
        }
        finally
        {
            CloseHandle(hProc);
        }
    }

    public static string GetWindowText(nint hWnd)
    {
        char* buf = stackalloc char[512];
        int len = GetWindowTextW(hWnd, buf, 512);
        return len > 0 ? new string(buf, 0, len) : string.Empty;
    }

    public static void TrimWorkingSet()
    {
        try
        {
            SetProcessWorkingSetSize(GetCurrentProcess(), new nint(-1), new nint(-1));
        }
        catch
        {
            // Best-effort working set trim
        }
    }

    public static string GetWindowClassName(nint hWnd)
    {
        char* buf = stackalloc char[256];
        int len = GetClassNameW(hWnd, buf, 256);
        return len > 0 ? new string(buf, 0, len) : string.Empty;
    }

    public static bool IsWindowClass(nint hWnd, ReadOnlySpan<char> expectedClass)
    {
        char* buf = stackalloc char[64];
        int len = GetClassNameW(hWnd, buf, 64);
        return len == expectedClass.Length &&
               new ReadOnlySpan<char>(buf, len).SequenceEqual(expectedClass);
    }

    public static bool IsDesktopShellWindowClass(nint hWnd)
    {
        char* buf = stackalloc char[64];
        int len = GetClassNameW(hWnd, buf, 64);
        if (len <= 0) return false;
        var cls = new ReadOnlySpan<char>(buf, len);
        return cls.SequenceEqual("Progman") ||
               cls.SequenceEqual("WorkerW") ||
               cls.SequenceEqual("Shell_TrayWnd") ||
               cls.SequenceEqual("Shell_SecondaryTrayWnd") ||
               cls.SequenceEqual("NotifyIconOverflowWindow");
    }

    public readonly record struct VisibleWindowInfo(
        nint Hwnd,
        uint Pid,
        uint CorePid,
        string Title,
        bool IsToolWindow,
        bool HasOwner,
        bool IsIconic,
        int Width,
        int Height);

    public static List<VisibleWindowInfo> SnapshotVisibleWindows()
    {
        var list = new List<VisibleWindowInfo>(64);
        var handle = GCHandle.Alloc(list);
        try
        {
            EnumWindows(&EnumVisibleWindowsProc, GCHandle.ToIntPtr(handle));
            return list;
        }
        finally
        {
            if (handle.IsAllocated)
                handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumVisibleWindowsProc(nint hwnd, nint lParam)
    {
        if (lParam == 0 || !IsWindowVisible(hwnd))
            return 1;

        if (GCHandle.FromIntPtr(lParam).Target is not List<VisibleWindowInfo> list)
            return 0;

        bool iconic = IsIconic(hwnd);
        GetWindowRect(hwnd, out var rc);
        int width = rc.Width;
        int height = rc.Height;
        if (!iconic && (width < 20 || height < 20))
            return 1;

        GetWindowThreadProcessId(hwnd, out uint pid);
        uint corePid = 0;
        if (IsWindowClass(hwnd, "ApplicationFrameWindow"))
        {
            nint coreHwnd = FindWindowExW(hwnd, 0, "Windows.UI.Core.CoreWindow", null);
            if (coreHwnd != 0)
                GetWindowThreadProcessId(coreHwnd, out corePid);
        }

        uint exStyle = unchecked((uint)GetWindowLongW(hwnd, GWL_EXSTYLE));
        bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
        bool hasOwner = GetWindow(hwnd, GW_OWNER) != 0;
        string title = GetWindowText(hwnd);

        list.Add(new VisibleWindowInfo(hwnd, pid, corePid, title, isToolWindow, hasOwner, iconic, width, height));
        return 1;
    }

    public static nint FindMainWindowInSnapshot(
        IReadOnlyList<VisibleWindowInfo> windows,
        IReadOnlyCollection<int> pids,
        string? preferredTitle,
        out string matchedTitle)
    {
        matchedTitle = string.Empty;
        if (pids.Count == 0 || windows.Count == 0)
            return 0;

        var pidSet = new HashSet<uint>();
        foreach (int p in pids)
        {
            if (p > 0) pidSet.Add((uint)p);
        }
        if (pidSet.Count == 0)
            return 0;

        string? pref = string.IsNullOrWhiteSpace(preferredTitle) ? null : preferredTitle.Trim();
        nint bestHwnd = 0;
        int bestScore = int.MinValue;

        foreach (var w in windows)
        {
            bool matches = (w.Pid != 0 && pidSet.Contains(w.Pid)) ||
                           (w.CorePid != 0 && pidSet.Contains(w.CorePid));
            if (!matches)
                continue;

            int score = 0;
            if (!w.IsToolWindow && !w.HasOwner)
                score += 300;
            if (!string.IsNullOrWhiteSpace(w.Title))
                score += 120;
            if (!string.IsNullOrEmpty(pref) &&
                !string.IsNullOrEmpty(w.Title) &&
                w.Title.Contains(pref, StringComparison.OrdinalIgnoreCase))
            {
                score += 1000;
            }

            if (w.IsIconic)
            {
                score += 80;
            }
            else if (w.Width >= 60 && w.Height >= 40)
            {
                score += (int)Math.Clamp(((long)w.Width * w.Height) / 20000L, 1L, 150L);
            }
            else
            {
                score -= 200;
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestHwnd = w.Hwnd;
                matchedTitle = w.Title;
            }
        }

        return bestHwnd;
    }

    private sealed class WindowSearchState
    {
        public required HashSet<uint> TargetPids;
        public string? PreferredTitle;
        public nint BestHwnd;
        public int BestScore = int.MinValue;
    }

    public static nint TryFindMainWindowForPids(IReadOnlyCollection<int> pids, string? preferredTitle = null)
    {
        if (pids.Count == 0)
            return 0;

        var pidSet = new HashSet<uint>();
        foreach (int p in pids)
        {
            if (p > 0) pidSet.Add((uint)p);
        }
        if (pidSet.Count == 0)
            return 0;

        var state = new WindowSearchState
        {
            TargetPids = pidSet,
            PreferredTitle = string.IsNullOrWhiteSpace(preferredTitle) ? null : preferredTitle.Trim()
        };

        var handle = GCHandle.Alloc(state);
        try
        {
            EnumWindows(&EnumAppWindowProc, GCHandle.ToIntPtr(handle));
            return state.BestHwnd;
        }
        finally
        {
            if (handle.IsAllocated)
                handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumAppWindowProc(nint hwnd, nint lParam)
    {
        if (lParam == 0 || !IsWindowVisible(hwnd))
            return 1;

        if (GCHandle.FromIntPtr(lParam).Target is not WindowSearchState state)
            return 0;

        GetWindowThreadProcessId(hwnd, out uint pid);
        bool matches = pid != 0 && state.TargetPids.Contains(pid);

        if (!matches)
        {
            if (IsWindowClass(hwnd, "ApplicationFrameWindow"))
            {
                nint coreHwnd = FindWindowExW(hwnd, 0, "Windows.UI.Core.CoreWindow", null);
                if (coreHwnd != 0)
                {
                    GetWindowThreadProcessId(coreHwnd, out uint corePid);
                    if (corePid != 0 && state.TargetPids.Contains(corePid))
                        matches = true;
                }
            }
        }

        if (!matches)
            return 1;

        uint exStyle = unchecked((uint)GetWindowLongW(hwnd, GWL_EXSTYLE));
        bool isToolWindow = (exStyle & WS_EX_TOOLWINDOW) != 0;
        bool hasOwner = GetWindow(hwnd, GW_OWNER) != 0;
        bool iconic = IsIconic(hwnd);

        GetWindowRect(hwnd, out var rc);
        int width = rc.Width;
        int height = rc.Height;
        if (!iconic && (width < 20 || height < 20))
            return 1;

        int score = 0;
        if (!isToolWindow && !hasOwner)
            score += 300;

        string title = GetWindowText(hwnd);
        if (!string.IsNullOrWhiteSpace(title))
            score += 120;

        if (!string.IsNullOrEmpty(state.PreferredTitle) &&
            !string.IsNullOrEmpty(title) &&
            title.Contains(state.PreferredTitle, StringComparison.OrdinalIgnoreCase))
        {
            score += 1000;
        }

        if (iconic)
        {
            score += 80;
        }
        else if (width >= 60 && height >= 40)
        {
            score += (int)Math.Clamp(((long)width * height) / 20000L, 1L, 150L);
        }
        else
        {
            score -= 200;
        }

        if (score > state.BestScore)
        {
            state.BestScore = score;
            state.BestHwnd = hwnd;
        }

        return 1;
    }

    private sealed class WindowListSearchState
    {
        public required HashSet<uint> TargetPids;
        public required List<nint> Windows;
    }

    public static List<nint> GetVisibleWindowsForPids(IReadOnlyCollection<int> pids)
    {
        var results = new List<nint>();
        if (pids.Count == 0)
            return results;

        var pidSet = new HashSet<uint>();
        foreach (int p in pids)
        {
            if (p > 0) pidSet.Add((uint)p);
        }
        if (pidSet.Count == 0)
            return results;

        var state = new WindowListSearchState
        {
            TargetPids = pidSet,
            Windows = results
        };

        var handle = GCHandle.Alloc(state);
        try
        {
            EnumWindows(&EnumWindowListProc, GCHandle.ToIntPtr(handle));
            return results;
        }
        finally
        {
            if (handle.IsAllocated)
                handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int EnumWindowListProc(nint hwnd, nint lParam)
    {
        if (lParam == 0 || !IsWindowVisible(hwnd))
            return 1;

        if (GCHandle.FromIntPtr(lParam).Target is not WindowListSearchState state)
            return 0;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || !state.TargetPids.Contains(pid))
            return 1;

        uint exStyle = unchecked((uint)GetWindowLongW(hwnd, GWL_EXSTYLE));
        if ((exStyle & WS_EX_TOOLWINDOW) != 0)
            return 1;

        if (GetWindow(hwnd, GW_OWNER) != 0)
            return 1;

        if (!IsIconic(hwnd))
        {
            GetWindowRect(hwnd, out var rc);
            if (rc.Width < 120 || rc.Height < 80)
                return 1;
        }

        state.Windows.Add(hwnd);
        return 1;
    }

    public static bool TryFocusWindow(nint hwnd)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
            return false;

        if (IsIconic(hwnd))
            ShowWindow(hwnd, SW_RESTORE);
        else
            ShowWindow(hwnd, SW_SHOW);

        uint targetTid = GetWindowThreadProcessId(hwnd, out uint targetPid);
        if (targetPid != 0)
            AllowSetForegroundWindow(targetPid);

        BringWindowToTop(hwnd);
        SetForegroundWindow(hwnd);
        if (GetForegroundWindow() == hwnd)
            return true;

        nint fgHwnd = GetForegroundWindow();
        uint fgTid = fgHwnd != 0 ? GetWindowThreadProcessId(fgHwnd, out _) : 0;
        uint curTid = GetCurrentThreadId();
        bool attachedCur = false;
        bool attachedFg = false;

        try
        {
            if (targetTid != 0 && curTid != targetTid)
                attachedCur = AttachThreadInput(curTid, targetTid, true);
            if (targetTid != 0 && fgTid != 0 && fgTid != targetTid && fgTid != curTid)
                attachedFg = AttachThreadInput(fgTid, targetTid, true);

            if (IsIconic(hwnd))
                ShowWindow(hwnd, SW_RESTORE);
            BringWindowToTop(hwnd);
            SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attachedFg)
                AttachThreadInput(fgTid, targetTid, false);
            if (attachedCur)
                AttachThreadInput(curTid, targetTid, false);
        }

        return IsWindow(hwnd);
    }

    public static bool TryFocusProcessWindow(IReadOnlyCollection<int> pids, string? preferredTitle = null)
    {
        nint hwnd = TryFindMainWindowForPids(pids, preferredTitle);
        return hwnd != 0 && TryFocusWindow(hwnd);
    }

    public static bool TrySendAppCommand(nint hwnd, int appCommand)
    {
        if (hwnd == 0 || !IsWindow(hwnd))
            return false;

        nint lParam = (nint)(appCommand << 16);
        return SendMessageTimeoutW(hwnd, WM_APPCOMMAND, (nuint)hwnd, lParam, SMTO_ABORTIFHUNG, 250, out _) != 0;
    }

    private static readonly uint[] _customPickerColors =
    [
        0x181311, 0x201C28, 0x362316, 0x4D3021,
        0xF99C4F, 0xF8BD38, 0x99D334, 0x24BFFB,
        0x5E3FF4, 0xFAA78B, 0xB672F4, 0xEE760F,
        0xF5F5F5, 0xA3A3A3, 0x474747, 0x101010
    ];

    public static bool TryPickColor(nint ownerHwnd, uint initialRgb, out uint pickedRgb)
    {
        uint r = (initialRgb >> 16) & 0xFF;
        uint g = (initialRgb >> 8) & 0xFF;
        uint b = initialRgb & 0xFF;
        uint initialColorRef = r | (g << 8) | (b << 16);

        fixed (uint* pCust = _customPickerColors)
        {
            var cc = new CHOOSECOLORW
            {
                lStructSize = (uint)sizeof(CHOOSECOLORW),
                hwndOwner = ownerHwnd,
                rgbResult = initialColorRef,
                lpCustColors = pCust,
                Flags = CC_RGBINIT | CC_FULLOPEN | CC_ANYCOLOR
            };
            if (ChooseColorW(&cc) != 0)
            {
                uint cr = cc.rgbResult;
                uint pr = cr & 0xFF;
                uint pg = (cr >> 8) & 0xFF;
                uint pb = (cr >> 16) & 0xFF;
                pickedRgb = (pr << 16) | (pg << 8) | pb;
                return true;
            }
        }

        pickedRgb = initialRgb;
        return false;
    }
}

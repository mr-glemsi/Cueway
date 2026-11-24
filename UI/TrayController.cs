using Cueway.Interop;
using Cueway.Localization;
using Cueway.Routing;

namespace Cueway.UI;

internal sealed unsafe class TrayController : IDisposable
{
    public const uint WM_TRAYICON = Win32.WM_APP + 101;
    private const uint TRAY_ICON_ID = 1001;

    private const uint MENU_SHOW = 1;
    private const uint MENU_TOGGLE_LOCK = 2;
    private const uint MENU_TOGGLE_SOLO = 3;
    private const uint MENU_SETTINGS = 4;
    private const uint MENU_EXIT = 5;

    private readonly nint _hwndHost;
    private readonly DynamicIconRenderer _iconRenderer;
    private nint _currentTrayIcon;
    private bool _ownsTrayIcon;
    private bool _added;
    private bool _disposed;
    private string? _lastAppKey;
    private string? _lastExePath;
    private bool _lastIsPlaying;
    private bool _lastIsMuted;
    private bool _lastSoloMode;
    private string? _lastTip;
    private string? _lastThemeId;

    public event Action? ShowMainWindowRequested;
    public event Action? ToggleMainWindowRequested;
    public event Action? OpenSettingsRequested;
    public event Action? ToggleManualLockRequested;
    public event Action? ToggleSoloModeRequested;
    public event Action? ExitRequested;

    public TrayController(nint hwndHost, DynamicIconRenderer iconRenderer)
    {
        _hwndHost = hwndHost;
        _iconRenderer = iconRenderer;
    }

    public void AddOrRestore(RouterStateSnapshot snapshot)
    {
        if (_disposed) return;
        _added = false;
        _lastTip = null;
        Update(snapshot);
    }

    public void Update(RouterStateSnapshot snapshot)
    {
        if (_disposed) return;

        var target = snapshot.ActiveTarget;
        string? appKey = target?.AppKey;
        string? exePath = target?.ExePath;
        bool isPlaying = target?.IsPlaying ?? false;
        bool isMuted = target?.IsMuted ?? false;
        bool soloMode = snapshot.SoloModeEnabled;
        string themeId = Theme.CurrentId;

        string tip = target == null
            ? Strings.TrayTipEmpty
            : $"Cueway — {target.DisplayName} ({(target.IsEffectivelyPlaying ? Strings.StatusPlaying : target.IsMuted ? Strings.StatusMuted : Strings.StatusPaused)})";
        if (soloMode)
            tip += " [Solo]";

        if (tip.Length > 127)
            tip = tip[..127];

        if (_added &&
            _currentTrayIcon != 0 &&
            _lastIsPlaying == isPlaying &&
            _lastIsMuted == isMuted &&
            _lastSoloMode == soloMode &&
            string.Equals(_lastAppKey, appKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_lastExePath, exePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(_lastTip, tip, StringComparison.Ordinal) &&
            string.Equals(_lastThemeId, themeId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        nint newIcon = _iconRenderer.CreateCompositeIcon(
            appKey,
            exePath,
            isPlaying,
            isMuted,
            soloMode,
            size: 32);

        bool ownsIcon = newIcon != 0;
        if (newIcon == 0)
            newIcon = Win32.LoadIconW(0, Win32.IDI_APPLICATION);

        var nid = new Win32.NOTIFYICONDATAW
        {
            cbSize = (uint)sizeof(Win32.NOTIFYICONDATAW),
            hWnd = _hwndHost,
            uID = TRAY_ICON_ID,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP | Win32.NIF_SHOWTIP,
            uCallbackMessage = WM_TRAYICON,
            hIcon = newIcon,
            uTimeoutOrVersion = Win32.NOTIFYICON_VERSION_4
        };

        for (int i = 0; i < tip.Length && i < 127; i++)
            nid.szTip[i] = tip[i];
        nid.szTip[Math.Min(tip.Length, 127)] = '\0';

        if (!_added)
        {
            if (Win32.Shell_NotifyIconW(Win32.NIM_ADD, &nid))
            {
                Win32.Shell_NotifyIconW(Win32.NIM_SETVERSION, &nid);
                _added = true;
            }
        }
        else
        {
            if (!Win32.Shell_NotifyIconW(Win32.NIM_MODIFY, &nid))
            {
                if (Win32.Shell_NotifyIconW(Win32.NIM_ADD, &nid))
                    Win32.Shell_NotifyIconW(Win32.NIM_SETVERSION, &nid);
            }
        }

        nint oldIcon = _currentTrayIcon;
        bool oldOwned = _ownsTrayIcon;
        _currentTrayIcon = newIcon;
        _ownsTrayIcon = ownsIcon;
        _lastAppKey = appKey;
        _lastExePath = exePath;
        _lastIsPlaying = isPlaying;
        _lastIsMuted = isMuted;
        _lastSoloMode = soloMode;
        _lastTip = tip;
        _lastThemeId = themeId;

        if (oldIcon != 0 && oldOwned)
            Win32.DestroyIcon(oldIcon);
    }

    public Win32.RECT? TryGetIconRect()
    {
        if (!_added || _disposed) return null;

        var id = new Win32.NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)sizeof(Win32.NOTIFYICONIDENTIFIER),
            hWnd = _hwndHost,
            uID = TRAY_ICON_ID
        };

        int hr = Win32.Shell_NotifyIconGetRect(&id, out var rect);
        return hr == 0 ? rect : null;
    }

    public void HandleTrayMessage(nuint wParam, nint lParam, RouterStateSnapshot snapshot)
    {
        const uint NIN_SELECT = 0x0400;
        const uint NIN_KEYSELECT = 0x0401;
        const uint WM_CONTEXTMENU = 0x007B;

        uint evt = (uint)(lParam & 0xFFFF);
        switch (evt)
        {
            case NIN_SELECT:
            case NIN_KEYSELECT:
                ToggleMainWindowRequested?.Invoke();
                break;

            case WM_CONTEXTMENU:
                ShowContextMenu(snapshot);
                break;
        }
    }

    private void ShowContextMenu(RouterStateSnapshot snapshot)
    {
        nint hMenu = Win32.CreatePopupMenu();
        if (hMenu == 0) return;

        try
        {
            Win32.AppendMenuW(hMenu, Win32.MF_STRING, MENU_SHOW, Strings.TrayMenuOpen);
            Win32.AppendMenuW(hMenu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenuW(
                hMenu,
                Win32.MF_STRING | (snapshot.IsManualLock ? Win32.MF_CHECKED : Win32.MF_UNCHECKED),
                MENU_TOGGLE_LOCK,
                Strings.TrayMenuLock);
            Win32.AppendMenuW(
                hMenu,
                Win32.MF_STRING | (snapshot.SoloModeEnabled ? Win32.MF_CHECKED : Win32.MF_UNCHECKED),
                MENU_TOGGLE_SOLO,
                Strings.TrayMenuSolo);
            Win32.AppendMenuW(hMenu, Win32.MF_STRING, MENU_SETTINGS, Strings.TrayMenuSettings);
            Win32.AppendMenuW(hMenu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenuW(hMenu, Win32.MF_STRING, MENU_EXIT, Strings.TrayMenuExit);

            Win32.GetCursorPos(out var pt);
            Win32.SetForegroundWindow(_hwndHost);
            int cmd = Win32.TrackPopupMenuEx(
                hMenu,
                Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD | Win32.TPM_NONOTIFY,
                pt.X,
                pt.Y,
                _hwndHost,
                0);
            Win32.PostMessageW(_hwndHost, 0, 0, 0);

            switch ((uint)cmd)
            {
                case MENU_SHOW:
                    ShowMainWindowRequested?.Invoke();
                    break;
                case MENU_TOGGLE_LOCK:
                    ToggleManualLockRequested?.Invoke();
                    break;
                case MENU_TOGGLE_SOLO:
                    ToggleSoloModeRequested?.Invoke();
                    break;
                case MENU_SETTINGS:
                    OpenSettingsRequested?.Invoke();
                    break;
                case MENU_EXIT:
                    ExitRequested?.Invoke();
                    break;
            }
        }
        finally
        {
            Win32.DestroyMenu(hMenu);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_added)
        {
            var nid = new Win32.NOTIFYICONDATAW
            {
                cbSize = (uint)sizeof(Win32.NOTIFYICONDATAW),
                hWnd = _hwndHost,
                uID = TRAY_ICON_ID
            };
            Win32.Shell_NotifyIconW(Win32.NIM_DELETE, &nid);
            _added = false;
        }

        if (_currentTrayIcon != 0)
        {
            if (_ownsTrayIcon)
                Win32.DestroyIcon(_currentTrayIcon);
            _currentTrayIcon = 0;
            _ownsTrayIcon = false;
        }
    }
}

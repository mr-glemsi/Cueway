using System.Diagnostics;
using System.Security;
using System.Security.Principal;
using System.Text;
using Cueway.Interop;
using Cueway.Localization;

namespace Cueway.Startup;

internal sealed class StartupService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string AppValueName = "Cueway";
    private const string TaskName = "Cueway";

    private volatile bool _isScheduledTaskInstalled;
    private volatile bool _isScheduledTaskElevated;
    private readonly bool _isElevated;

    public bool IsElevated => _isElevated;
    public bool IsScheduledTaskInstalled => _isScheduledTaskInstalled;
    public bool IsScheduledTaskElevated => _isScheduledTaskElevated;
    public bool IsStartupEnabled => IsRegistryStartupEnabled() || _isScheduledTaskInstalled;

    public event Action? StatusChanged;

    public StartupService()
    {
        _isElevated = QueryIsElevated();
    }

    private static unsafe bool QueryIsElevated()
    {
        nint hToken = 0;
        try
        {
            if (!Win32.OpenProcessToken(Win32.GetCurrentProcess(), Win32.TOKEN_QUERY, out hToken) || hToken == 0)
                return false;

            uint elevation = 0;
            if (Win32.GetTokenInformation(hToken, Win32.TokenElevation, &elevation, sizeof(uint), out _))
                return elevation != 0;

            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hToken != 0) Win32.CloseHandle(hToken);
        }
    }

    public unsafe bool IsRegistryStartupEnabled()
    {
        nint hKey = 0;
        try
        {
            if (Win32.RegOpenKeyExW(Win32.HKEY_CURRENT_USER, RunSubKey, 0, Win32.KEY_READ, out hKey) != 0 || hKey == 0)
                return false;

            uint size = 0;
            int rc = Win32.RegQueryValueExW(hKey, AppValueName, 0, out _, null, ref size);
            return rc == 0 && size > 2;
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hKey != 0) Win32.RegCloseKey(hKey);
        }
    }

    public async Task RefreshScheduledTaskStatusAsync()
    {
        var (installed, elevated) = await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\" /XML")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false
                };
                using var p = Process.Start(psi);
                if (p == null) return (false, false);
                string xml = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(3000))
                {
                    try { p.Kill(); } catch { }
                    return (false, false);
                }
                bool ok = p.ExitCode == 0;
                bool isHighest = ok && xml.Contains("HighestAvailable", StringComparison.OrdinalIgnoreCase);
                return (ok, isHighest);
            }
            catch
            {
                return (false, false);
            }
        }).ConfigureAwait(false);

        if (_isScheduledTaskInstalled != installed || _isScheduledTaskElevated != elevated)
        {
            _isScheduledTaskInstalled = installed;
            _isScheduledTaskElevated = elevated;
            StatusChanged?.Invoke();
        }
    }

    public bool SetBasicStartup(bool enabled, bool startMinimized)
    {
        if (enabled)
        {
            if (_isScheduledTaskInstalled && (!_isScheduledTaskElevated || _isElevated))
            {
                // Update existing task arguments seamlessly when privilege allows silent update
                _ = InstallScheduledTaskAsync(startMinimized, _isScheduledTaskElevated);
                return true;
            }

            bool ok = SetRegistryRun(true, startMinimized);
            StatusChanged?.Invoke();
            return ok;
        }
        else
        {
            bool ok = SetRegistryRun(false, false);
            if (_isScheduledTaskInstalled)
            {
                _ = RemoveScheduledTaskAsync();
            }
            else
            {
                StatusChanged?.Invoke();
            }
            return ok;
        }
    }

    public void SyncStartupArgumentsIfEnabled(bool startMinimized)
    {
        if (IsRegistryStartupEnabled())
        {
            SetRegistryRun(true, startMinimized);
        }
        if (_isScheduledTaskInstalled && (!_isScheduledTaskElevated || _isElevated))
        {
            _ = InstallScheduledTaskAsync(startMinimized, _isScheduledTaskElevated);
        }
    }

    private static unsafe bool SetRegistryRun(bool enable, bool startMinimized)
    {
        nint hKey = 0;
        try
        {
            int rc = enable
                ? Win32.RegCreateKeyExW(Win32.HKEY_CURRENT_USER, RunSubKey, 0, null, 0, Win32.KEY_SET_VALUE, 0, out hKey, out _)
                : Win32.RegOpenKeyExW(Win32.HKEY_CURRENT_USER, RunSubKey, 0, Win32.KEY_SET_VALUE, out hKey);

            if (rc != 0 || hKey == 0)
                return false;

            if (!enable)
            {
                Win32.RegDeleteValueW(hKey, AppValueName);
                return true;
            }

            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            string cmd = startMinimized ? $"\"{exe}\" --minimized" : $"\"{exe}\"";
            fixed (char* pCmd = cmd)
            {
                uint bytes = (uint)((cmd.Length + 1) * sizeof(char));
                return Win32.RegSetValueExW(hKey, AppValueName, 0, Win32.REG_SZ, (byte*)pCmd, bytes) == 0;
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hKey != 0) Win32.RegCloseKey(hKey);
        }
    }

    public void InstallScheduledTask(bool elevated, bool startMinimized, Action<bool, string> onDone)
    {
        _ = Task.Run(async () =>
        {
            var res = await InstallScheduledTaskAsync(startMinimized, elevated).ConfigureAwait(false);
            onDone(res.Success, res.Message);
        });
    }

    public void RemoveScheduledTask(Action<bool, string> onDone)
    {
        _ = Task.Run(async () =>
        {
            var res = await RemoveScheduledTaskAsync().ConfigureAwait(false);
            onDone(res.Success, res.Message);
        });
    }

    public async Task<(bool Success, string Message)> InstallScheduledTaskAsync(bool startMinimized, bool elevated = true)
    {
        string? exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return (false, Strings.MsgTaskExeMissing);

        string workDir = Path.GetDirectoryName(exe) ?? string.Empty;
        string userId;
        try
        {
            userId = WindowsIdentity.GetCurrent().Name;
        }
        catch
        {
            userId = $@"{Environment.UserDomainName}\{Environment.UserName}";
        }

        string runLevel = elevated ? "HighestAvailable" : "LeastPrivilege";
        string argsXml = startMinimized ? "<Arguments>--minimized</Arguments>" : string.Empty;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Cueway - Windows Media Router ({runLevel})</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{SecurityElement.Escape(userId)}</UserId>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{SecurityElement.Escape(userId)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>{runLevel}</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>true</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{SecurityElement.Escape(exe)}</Command>
                  {argsXml}
                  <WorkingDirectory>{SecurityElement.Escape(workDir)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        string taskDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cueway");
        Directory.CreateDirectory(taskDir);
        string tempXml = Path.Combine(taskDir, $"Cueway_Task_{Guid.NewGuid():N}.xml");
        try
        {
            await File.WriteAllTextAsync(tempXml, xml, Encoding.Unicode).ConfigureAwait(false);
            int code = await RunSchtasksAsync($"/Create /TN \"{TaskName}\" /XML \"{tempXml}\" /F", requireElevation: elevated).ConfigureAwait(false);
            if (code == 0)
            {
                // Avoid duplicate launch from HKCU Run when Task Scheduler handles logon start
                SetRegistryRun(false, false);
                _isScheduledTaskInstalled = true;
                _isScheduledTaskElevated = elevated;
                StatusChanged?.Invoke();
                return (true, elevated ? Strings.MsgTaskInstalledElevated : Strings.MsgTaskInstalledNormal);
            }
            return (false, elevated ? Strings.MsgTaskInstallFail : Strings.MsgTaskInstallFailNormal);
        }
        catch (Exception ex)
        {
            return (false, $"{(elevated ? Strings.MsgTaskInstallFail : Strings.MsgTaskInstallFailNormal)} ({ex.Message})");
        }
        finally
        {
            try { if (File.Exists(tempXml)) File.Delete(tempXml); } catch { }
        }
    }

    public async Task<(bool Success, string Message)> RemoveScheduledTaskAsync()
    {
        try
        {
            int code = await RunSchtasksAsync($"/Delete /TN \"{TaskName}\" /F", requireElevation: _isScheduledTaskElevated).ConfigureAwait(false);
            if (code != 0 && !_isScheduledTaskElevated && !_isElevated)
            {
                code = await RunSchtasksAsync($"/Delete /TN \"{TaskName}\" /F", requireElevation: true).ConfigureAwait(false);
            }
            if (code == 0)
            {
                _isScheduledTaskInstalled = false;
                _isScheduledTaskElevated = false;
                StatusChanged?.Invoke();
                return (true, Strings.MsgTaskRemoved);
            }
            await RefreshScheduledTaskStatusAsync().ConfigureAwait(false);
            return (false, Strings.MsgTaskRemoveFail);
        }
        catch
        {
            return (false, Strings.MsgTaskCancelled);
        }
    }

    private async Task<int> RunSchtasksAsync(string arguments, bool requireElevation)
    {
        return await Task.Run(() =>
        {
            bool useUac = requireElevation && !_isElevated;
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = useUac,
                Verb = useUac ? "runas" : string.Empty,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = !useUac
            };
            using var proc = Process.Start(psi);
            if (proc == null) return -1;
            int waitMs = useUac ? 60_000 : 10_000;
            if (!proc.WaitForExit(waitMs))
                return -1;
            return proc.ExitCode;
        }).ConfigureAwait(false);
    }

    public bool RestartAsAdministrator(bool openSettings = true)
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = openSettings ? "--show-settings" : "--show"
            };
            using var _ = Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void OpenConfigFolder(string filePath)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                using var _ = Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}

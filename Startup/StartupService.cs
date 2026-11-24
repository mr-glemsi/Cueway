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
    private readonly bool _isElevated;

    public bool IsElevated => _isElevated;
    public bool IsScheduledTaskInstalled => _isScheduledTaskInstalled;
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
        bool installed = await Task.Run(() =>
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks.exe", $"/Query /TN \"{TaskName}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.WaitForExit(3000);
                return p.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }).ConfigureAwait(false);

        if (_isScheduledTaskInstalled != installed)
        {
            _isScheduledTaskInstalled = installed;
            StatusChanged?.Invoke();
        }
    }

    public bool SetBasicStartup(bool enabled, bool startMinimized)
    {
        if (enabled)
        {
            if (_isScheduledTaskInstalled && _isElevated)
            {
                // Update existing elevated task arguments seamlessly
                _ = InstallScheduledTaskAsync(startMinimized);
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
        if (_isScheduledTaskInstalled && _isElevated)
        {
            _ = InstallScheduledTaskAsync(startMinimized);
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

    public void ToggleScheduledTask(bool remove, bool startMinimized, Action<bool, string> onDone)
    {
        _ = Task.Run(async () =>
        {
            var res = remove
                ? await RemoveScheduledTaskAsync().ConfigureAwait(false)
                : await InstallScheduledTaskAsync(startMinimized).ConfigureAwait(false);
            onDone(res.Success, res.Message);
        });
    }

    public async Task<(bool Success, string Message)> InstallScheduledTaskAsync(bool startMinimized)
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

        string argsXml = startMinimized ? "<Arguments>--minimized</Arguments>" : string.Empty;
        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.3" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Cueway - Windows 11 Native AOT Media Router (HighestAvailable)</Description>
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
                  <RunLevel>HighestAvailable</RunLevel>
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
            int code = await RunSchtasksAsync($"/Create /TN \"{TaskName}\" /XML \"{tempXml}\" /F").ConfigureAwait(false);
            if (code == 0)
            {
                // Avoid duplicate launch from HKCU Run when Task Scheduler handles elevated logon start
                SetRegistryRun(false, false);
                _isScheduledTaskInstalled = true;
                StatusChanged?.Invoke();
                return (true, Strings.MsgTaskInstalled);
            }
            return (false, Strings.MsgTaskInstallFail);
        }
        catch (Exception ex)
        {
            return (false, $"{Strings.MsgTaskInstallFail} ({ex.Message})");
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
            int code = await RunSchtasksAsync($"/Delete /TN \"{TaskName}\" /F").ConfigureAwait(false);
            if (code == 0)
            {
                _isScheduledTaskInstalled = false;
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

    private async Task<int> RunSchtasksAsync(string arguments)
    {
        return await Task.Run(() =>
        {
            var psi = new ProcessStartInfo("schtasks.exe", arguments)
            {
                UseShellExecute = !_isElevated,
                Verb = _isElevated ? string.Empty : "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = _isElevated
            };
            using var proc = Process.Start(psi);
            if (proc == null) return -1;
            proc.WaitForExit(10_000);
            return proc.ExitCode;
        }).ConfigureAwait(false);
    }

    public bool RestartAsAdministrator()
    {
        try
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe))
                return false;

            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            Process.Start(psi);
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
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }
}

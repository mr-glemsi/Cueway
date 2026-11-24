#Requires -Version 5.1
<#
.SYNOPSIS
    Registers or unregisters the Cueway Windows Task Scheduler logon task with HighestAvailable privileges.
.DESCRIPTION
    Allows Cueway to run elevated without a UAC prompt at user logon so WH_KEYBOARD_LL / WH_MOUSE_LL hooks
    remain active even when fullscreen games or elevated applications hold focus (UIPI compliance).
.PARAMETER ExePath
    Optional explicit path to Cueway.exe. If omitted, searches native publish and build output folders.
.PARAMETER Unregister
    Removes the scheduled task instead of registering it.
.PARAMETER StartNow
    Starts the scheduled task immediately after registration.
#>
[CmdletBinding()]
param(
    [string]$ExePath,
    [switch]$Unregister,
    [switch]$StartNow
)

$ErrorActionPreference = 'Stop'
$TaskName = 'Cueway'

function Test-IsAdmin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Write-Warning "HighestAvailable gorevi kaydetmek icin yonetici yetkisi gerekir. UAC ile yeniden baslatiliyor..."
    $argList = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    if ($ExePath) { $argList += @('-ExePath', "`"$ExePath`"") }
    if ($Unregister) { $argList += '-Unregister' }
    if ($StartNow) { $argList += '-StartNow' }
    Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argList
    exit 0
}

if ($Unregister) {
    $existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    if ($existing) {
        Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
        Write-Host "Gorev kaldirildi: $TaskName" -ForegroundColor Green
    } else {
        Write-Host "Gorev zaten kayitli degil: $TaskName" -ForegroundColor Yellow
    }
    exit 0
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $candidates = @(
        (Join-Path $PSScriptRoot 'bin\Release\net10.0-windows10.0.22621.0\win-x64\publish\Cueway.exe'),
        (Join-Path $PSScriptRoot 'publish\Cueway.exe'),
        (Join-Path $PSScriptRoot 'Cueway.exe'),
        (Join-Path $PSScriptRoot 'bin\Debug\net10.0-windows10.0.22621.0\win-x64\Cueway.exe')
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            $ExePath = (Resolve-Path -LiteralPath $candidate).Path
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($ExePath) -or -not (Test-Path -LiteralPath $ExePath)) {
    throw "Cueway.exe bulunamadi. Once 'dotnet publish -c Release -r win-x64' calistirin veya -ExePath parametresi verin."
}

$workDir = Split-Path -Parent $ExePath
$currentUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name

$action = New-ScheduledTaskAction -Execute $ExePath -Argument '--minimized' -WorkingDirectory $workDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
$principal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 3 `
    -RestartInterval ([TimeSpan]::FromMinutes(1)) `
    -MultipleInstances IgnoreNew `
    -StartWhenAvailable

$existingTask = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existingTask) {
    Stop-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "Cueway - Windows 11 Native AOT Media Router (UIPI uyumlu HighestAvailable)" | Out-Null

Write-Host "Gorev basariyla kaydedildi: $TaskName -> $ExePath (--minimized)" -ForegroundColor Green

if ($StartNow) {
    Start-ScheduledTask -TaskName $TaskName
    Write-Host "Gorev baslatildi: $TaskName" -ForegroundColor Cyan
}

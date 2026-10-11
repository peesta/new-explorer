param([int]$X, [int]$Y, [string]$Out, [int]$Wait = 500, [string]$ProcName = "CanvasExplorer")
# Hovers a point inside the app window, then captures the window (PrintWindow) and restores the cursor.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class H { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); public struct RECT { public int L, T, R, B; } public struct POINT { public int X, Y; } }
"@
$p = Get-Process $ProcName | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[H]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
$r = New-Object H+RECT; [H]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$o = New-Object H+POINT; [H]::GetCursorPos([ref]$o) | Out-Null
[H]::SetCursorPos($r.L + $X - 8, $r.T + $Y) | Out-Null; Start-Sleep -Milliseconds 120
[H]::SetCursorPos($r.L + $X, $r.T + $Y) | Out-Null; Start-Sleep -Milliseconds $Wait
& (Join-Path $PSScriptRoot "shot.ps1") -Out $Out | Out-Null
[H]::SetCursorPos($o.X, $o.Y) | Out-Null

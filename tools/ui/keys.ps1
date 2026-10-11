param([string]$Keys = "", [int]$WheelX = -1, [int]$WheelY = -1, [int]$Wheel = 0, [string]$ProcName = "CanvasExplorer")
# Sends keys (SendKeys syntax) and/or mouse wheel ticks at a window-relative point.
Add-Type -AssemblyName System.Windows.Forms
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class K { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, IntPtr e); public struct RECT { public int L, T, R, B; } public struct POINT { public int X, Y; } }
"@
$p = Get-Process $ProcName | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[K]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 150
if ($Wheel -ne 0) {
  $r = New-Object K+RECT; [K]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
  $o = New-Object K+POINT; [K]::GetCursorPos([ref]$o) | Out-Null
  [K]::SetCursorPos($r.L + $WheelX, $r.T + $WheelY) | Out-Null; Start-Sleep -Milliseconds 100
  $n = [Math]::Abs($Wheel); $d = if ($Wheel -gt 0) { 120 } else { -120 }
  for ($i = 0; $i -lt $n; $i++) { [K]::mouse_event(0x0800, 0, 0, $d, [IntPtr]::Zero); Start-Sleep -Milliseconds 30 }
  Start-Sleep -Milliseconds 200
  [K]::SetCursorPos($o.X, $o.Y) | Out-Null
}
if ($Keys) { [System.Windows.Forms.SendKeys]::SendWait($Keys); Start-Sleep -Milliseconds 300 }

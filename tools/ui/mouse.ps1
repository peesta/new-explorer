param([int]$X1, [int]$Y1, [int]$X2 = -1, [int]$Y2 = -1, [switch]$Right, [switch]$Ctrl, [string]$ProcName = "CanvasExplorer")
# Click or drag inside the app window (coordinates relative to the window). Restores the cursor afterwards.
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class M2 {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  public struct POINT { public int X, Y; }
  public struct RECT { public int L, T, R, B; }
}
"@
if ($X2 -lt 0) { $X2 = $X1; $Y2 = $Y1 }
$p = Get-Process $ProcName | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[M2]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
$r = New-Object M2+RECT; [M2]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$o = New-Object M2+POINT; [M2]::GetCursorPos([ref]$o) | Out-Null
$down = if ($Right) { 0x0008 } else { 0x0002 }; $up = if ($Right) { 0x0010 } else { 0x0004 }
$ax = $r.L + $X1; $ay = $r.T + $Y1; $bx = $r.L + $X2; $by = $r.T + $Y2
[M2]::SetCursorPos($ax, $ay) | Out-Null; Start-Sleep -Milliseconds 120
if ($Ctrl) { [M2]::keybd_event(0x11, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 60 }
[M2]::mouse_event($down, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80
if ($X2 -ne $X1 -or $Y2 -ne $Y1) {
  for ($i = 1; $i -le 20; $i++) { [M2]::SetCursorPos([int]($ax + ($bx - $ax) * $i / 20), [int]($ay + ($by - $ay) * $i / 20)) | Out-Null; Start-Sleep -Milliseconds 15 }
}
[M2]::mouse_event($up, 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80
if ($Ctrl) { [M2]::keybd_event(0x11, 0, 2, [IntPtr]::Zero) }
Start-Sleep -Milliseconds 150
[M2]::SetCursorPos($o.X, $o.Y) | Out-Null

param([string]$Points, [string]$Button = "left", [string]$ProcName = "CanvasExplorer")
# Presses a mouse button at the first point, moves through the rest ("x,y[,waitMs];..."), releases at the last.
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class P { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y); [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p); [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, int d, IntPtr e); public struct RECT { public int L, T, R, B; } public struct POINT { public int X, Y; } }
"@
$p = Get-Process $ProcName | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
[P]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
$r = New-Object P+RECT; [P]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
$o = New-Object P+POINT; [P]::GetCursorPos([ref]$o) | Out-Null
$codes = @{ left = @(0x2, 0x4); middle = @(0x20, 0x40); right = @(0x8, 0x10) }[$Button]
$pts = @(); foreach ($seg in $Points.Split(';')) { $v = $seg.Split(''); $v = $seg.Split(','); $w = 0; if ($v.Count -gt 2) { $w = [int]$v[2] }; $pts += ,@([int]$v[0], [int]$v[1], $w) }
$cx = $r.L + $pts[0][0]; $cy = $r.T + $pts[0][1]
[P]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 120
[P]::mouse_event($codes[0], 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 80
for ($k = 1; $k -lt $pts.Count; $k++) {
  $tx = $r.L + $pts[$k][0]; $ty = $r.T + $pts[$k][1]
  for ($i = 1; $i -le 15; $i++) { [P]::SetCursorPos([int]($cx + ($tx - $cx) * $i / 15), [int]($cy + ($ty - $cy) * $i / 15)) | Out-Null; Start-Sleep -Milliseconds 15 }
  $cx = $tx; $cy = $ty
  # keep nudging during waits so drag-and-drop keeps receiving DragOver
  $wait = [double]$pts[$k][2]; $end = (Get-Date).AddMilliseconds($wait)
  while ((Get-Date) -lt $end) { [P]::SetCursorPos($cx + 1, $cy) | Out-Null; Start-Sleep -Milliseconds 50; [P]::SetCursorPos($cx, $cy) | Out-Null; Start-Sleep -Milliseconds 50 }
}
[P]::mouse_event($codes[1], 0, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 200
[P]::SetCursorPos($o.X, $o.Y) | Out-Null



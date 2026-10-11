param([string]$Out, [int]$X = 0, [int]$Y = 0, [int]$W = 0, [int]$H = 0, [string]$ProcName = "CanvasExplorer")
# Captures the screen area of the app window (includes popups/menus). Optional sub-rectangle relative to the window.
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class S { [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r); public struct RECT { public int L, T, R, B; } }
"@
$p = Get-Process $ProcName | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
$r = New-Object S+RECT; [S]::GetWindowRect($p.MainWindowHandle, [ref]$r) | Out-Null
if ($W -le 0) { $W = $r.R - $r.L - $X }
if ($H -le 0) { $H = $r.B - $r.T - $Y }
$bmp = New-Object System.Drawing.Bitmap $W, $H
$g = [System.Drawing.Graphics]::FromImage($bmp); $g.CopyFromScreen($r.L + $X, $r.T + $Y, 0, 0, $bmp.Size); $g.Dispose()
$bmp.Save($Out); $bmp.Dispose()

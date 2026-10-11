param([string]$Combo)
# Presses a key combination by virtual-key codes, independent of the keyboard layout
# (SendKeys fails for letters when a non-Latin layout is active).
# Examples: -Combo "ctrl+t"   -Combo "alt+left"   -Combo "delete"   -Combo "ctrl+shift+n"
Add-Type @"
using System; using System.Runtime.InteropServices;
public static class VK { [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint f, IntPtr e); }
"@
$map = @{
  ctrl = 0x11; shift = 0x10; alt = 0x12; enter = 0x0D; esc = 0x1B; tab = 0x09; space = 0x20; back = 0x08; delete = 0x2E
  left = 0x25; up = 0x26; right = 0x27; down = 0x28; f2 = 0x71; f4 = 0x73; f5 = 0x74; home = 0x24; end = 0x23
}
$codes = foreach ($part in $Combo.ToLower().Split('+')) {
  if ($map.ContainsKey($part)) { [byte]$map[$part] }
  elseif ($part.Length -eq 1) { [byte][char]$part.ToUpper() }
  else { throw "Unknown key: $part" }
}
foreach ($c in $codes) { [VK]::keybd_event($c, 0, 0, [IntPtr]::Zero); Start-Sleep -Milliseconds 30 }
[array]::Reverse($codes)
foreach ($c in $codes) { [VK]::keybd_event($c, 0, 2, [IntPtr]::Zero); Start-Sleep -Milliseconds 30 }
Start-Sleep -Milliseconds 300

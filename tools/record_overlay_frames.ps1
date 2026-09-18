param(
    [string]$OutDir,
    [int]$Frames = 180,
    [int]$DelayMs = 33
)
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class Pw {
  public delegate bool EnumProc(IntPtr hWnd, IntPtr l);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
}
"@
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$p = Get-Process NotifyIsland -ErrorAction Stop | Select-Object -First 1
$global:NiPid = [uint32]$p.Id
$script:hwnd = [IntPtr]::Zero
[void][Pw]::EnumWindows({
    param($h, $l)
    $wp = [uint32]0
    [void][Pw]::GetWindowThreadProcessId($h, [ref]$wp)
    if ($wp -eq $global:NiPid) {
        $rr = New-Object Pw+RECT
        [void][Pw]::GetWindowRect($h, [ref]$rr)
        if (($rr.R - $rr.L) -gt 80) { $script:hwnd = $h; return $false }
    }
    return $true
}, [IntPtr]::Zero)
if ($script:hwnd -eq [IntPtr]::Zero) { throw 'no hwnd' }
$hwnd = $script:hwnd
$r = New-Object Pw+RECT
[void][Pw]::GetWindowRect($hwnd, [ref]$r)
$w = [Math]::Max(1, $r.R - $r.L)
$h = [Math]::Max(1, $r.B - $r.T)
for ($i = 0; $i -lt $Frames; $i++) {
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [void][Pw]::PrintWindow($hwnd, $hdc, 2)
    $g.ReleaseHdc($hdc)
    $g.Dispose()
    $bmp.Save((Join-Path $OutDir ('f{0:0000}.png' -f $i)), [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Start-Sleep -Milliseconds $DelayMs
}
Write-Output "frames=$Frames size=${w}x${h}"

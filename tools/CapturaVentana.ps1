# Captura lo que Windows realmente muestra de la ventana de un proceso (no lo que WPF cree que dibuja), aunque esté tapada por otras.
#   .\tools\CapturaVentana.ps1 -Proceso 1234 -Salida captura.png
param(
    [Parameter(Mandatory)][int]$Proceso,
    [Parameter(Mandatory)][string]$Salida
)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Ventana {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
}
'@
$p = Get-Process -Id $Proceso
$h = $p.MainWindowHandle
if ($h -eq [IntPtr]::Zero) { throw 'El proceso aún no tiene ventana.' }
$r = New-Object Ventana+RECT
[void][Ventana]::GetWindowRect($h, [ref]$r)
$w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
$bmp = New-Object System.Drawing.Bitmap $w, $hh
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
[void][Ventana]::PrintWindow($h, $dc, 2)   # PW_RENDERFULLCONTENT
$g.ReleaseHdc($dc)
$g.Dispose()
$bmp.Save($Salida, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Host "ventana capturada: $Salida ($w x $hh)"

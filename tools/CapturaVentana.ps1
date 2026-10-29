# Captura lo que Windows realmente muestra de la ventana de un proceso (no lo que WPF cree que dibuja), aunque esté tapada por otras.
#   .\tools\CapturaVentana.ps1 -Proceso 1234 -Salida captura.png
#   .\tools\CapturaVentana.ps1 -Proceso 1234 -Salida captura.png -Titulo 'Manual'   (otra ventana del mismo proceso: la primera cuyo título contenga el texto)
param(
    [Parameter(Mandatory)][int]$Proceso,
    [Parameter(Mandatory)][string]$Salida,
    [string]$Titulo = ''
)
Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class Ventana {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    public static IntPtr Buscar(uint pid, string titulo) {
        IntPtr res = IntPtr.Zero;
        EnumWindows((h, l) => {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid || !IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString().IndexOf(titulo, StringComparison.OrdinalIgnoreCase) >= 0) { res = h; return false; }
            return true;
        }, IntPtr.Zero);
        return res;
    }
}
'@
$p = Get-Process -Id $Proceso
$h = if ($Titulo) { [Ventana]::Buscar([uint32]$Proceso, $Titulo) } else { $p.MainWindowHandle }
if ($h -eq [IntPtr]::Zero) { throw 'No se encontró la ventana.' }
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

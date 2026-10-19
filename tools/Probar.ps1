# Ejecuta Velora real con una lista de órdenes automáticas y muestra el registro.
# Ejemplo:  .\tools\Probar.ps1 -Pdf pruebas\demo.pdf -Auto "listo;zoom:150;captura:pruebas\salida\a.png"
param(
    [string]$Pdf = '',
    [Parameter(Mandatory)][string]$Auto,
    [string]$Tema = '',
    [string]$Tam = '1200x800',
    [string]$Salida = (Join-Path $PSScriptRoot '..\pruebas\salida'),
    [string]$Exe = '',
    [string]$Clave = '',
    [int]$Espera = 120,
    [switch]$Limpiar
)
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
if (-not $Exe) {
    $Exe = (Get-ChildItem "$root\src\Velora.App\bin" -Recurse -Filter Velora.exe | Sort-Object LastWriteTime -Descending | Select-Object -First 1).FullName
}
New-Item -ItemType Directory -Force $Salida | Out-Null
$Salida = (Resolve-Path $Salida).Path
$log = Join-Path $Salida 'log.txt'
Remove-Item $log -ErrorAction SilentlyContinue
$datos = Join-Path $Salida 'datos'
if ($Limpiar) { Remove-Item $datos -Recurse -Force -ErrorAction SilentlyContinue }
$a = @()
if ($Pdf) { $a += "`"$((Resolve-Path $Pdf).Path)`"" }
$a += @('--auto', "`"$Auto`"", '--registro', "`"$log`"", '--datos', "`"$datos`"", '--silencioso', '--tam', $Tam)
if ($Tema) { $a += @('--tema', $Tema) }
if ($Clave) { $a += @('--clave', $Clave) }
$sw = [Diagnostics.Stopwatch]::StartNew()
$p = Start-Process -FilePath $Exe -ArgumentList $a -PassThru
if (-not $p.WaitForExit($Espera * 1000)) { $p.Kill(); Write-Host 'TIEMPO AGOTADO (se cerró el programa)' -ForegroundColor Red }
Write-Host ("Duración total: {0:0.0} s, código de salida: {1}" -f $sw.Elapsed.TotalSeconds, $p.ExitCode)
if (Test-Path $log) { Get-Content $log }
$err = Join-Path $datos 'errores.log'
if (Test-Path $err) { Write-Host '--- errores.log ---' -ForegroundColor Yellow; Get-Content $err }

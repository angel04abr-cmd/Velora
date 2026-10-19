# Desinstala Velora de tu usuario.
#
#   .\Desinstalar.ps1                  Quita el programa, el acceso directo y las asociaciones (conserva tus ajustes)
#   .\Desinstalar.ps1 -BorrarAjustes   Además borra %APPDATA%\Velora (ajustes y documentos recientes)
param(
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'Programs\Velora'),
    [switch]$BorrarAjustes,
    [switch]$Silencioso,
    [switch]$YaCopiado,
    [string]$RaizClases = 'HKCU:\Software\Classes',
    [string]$RaizSoftware = 'HKCU:\Software',
    [string]$CarpetaInicio = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)
$ErrorActionPreference = 'Stop'
function Escribir($texto, $color = 'Gray') { if (-not $Silencioso) { Write-Host $texto -ForegroundColor $color } }

$destinoCompleto = [IO.Path]::GetFullPath($Destino)

# Si se ejecuta desde dentro de la carpeta que se va a borrar, se vuelve a lanzar desde una copia temporal.
if (-not $YaCopiado -and $PSScriptRoot -and ($PSScriptRoot.TrimEnd('\') -ieq $destinoCompleto.TrimEnd('\'))) {
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('Desinstalar-Velora-' + [guid]::NewGuid().ToString('N') + '.ps1')
    Copy-Item $PSCommandPath $tmp
    $args2 = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $tmp, '-Destino', $destinoCompleto, '-YaCopiado',
        '-RaizClases', $RaizClases, '-RaizSoftware', $RaizSoftware, '-CarpetaInicio', $CarpetaInicio)
    if ($BorrarAjustes) { $args2 += '-BorrarAjustes' }
    if ($Silencioso) { $args2 += '-Silencioso' }
    # Start-Process no entrecomilla solo: los argumentos con espacios (rutas) hay que protegerlos
    $args2 = $args2 | ForEach-Object { if ($_ -match '\s') { '"' + $_ + '"' } else { $_ } }
    $estilo = if ($Silencioso) { 'Hidden' } else { 'Normal' }
    Start-Process powershell.exe -ArgumentList $args2 -Wait -WindowStyle $estilo
    return
}

$exe = Join-Path $destinoCompleto 'Velora.exe'
$enMarcha = @(Get-Process -Name Velora -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $exe) })
if ($enMarcha.Count -gt 0) { throw 'Velora está abierto. Ciérralo y vuelve a intentarlo.' }

Remove-Item "$RaizClases\Velora.Documento" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item "$RaizClases\Applications\Velora.exe" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "$RaizClases\.pdf\OpenWithProgids" -Name 'Velora.Documento' -ErrorAction SilentlyContinue
Remove-Item "$RaizSoftware\Velora" -Recurse -Force -ErrorAction SilentlyContinue
Remove-ItemProperty -Path "$RaizSoftware\RegisteredApplications" -Name 'Velora' -ErrorAction SilentlyContinue
Remove-Item "$RaizSoftware\Microsoft\Windows\CurrentVersion\Uninstall\Velora" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $CarpetaInicio 'Velora.lnk') -Force -ErrorAction SilentlyContinue

if (Test-Path (Join-Path $destinoCompleto 'Velora.exe')) {
    Remove-Item $destinoCompleto -Recurse -Force
    Escribir "Programa eliminado de $destinoCompleto."
} elseif (Test-Path $destinoCompleto) {
    Escribir "No toco $destinoCompleto porque no parece una instalación de Velora." 'Yellow'
}

if ($BorrarAjustes) {
    $datos = Join-Path $env:APPDATA 'Velora'
    if (Test-Path $datos) { Remove-Item $datos -Recurse -Force; Escribir "Ajustes borrados ($datos)." }
}
Escribir 'Velora se ha desinstalado.' 'Green'

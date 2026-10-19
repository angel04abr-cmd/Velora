# Instala (o actualiza) Velora para tu usuario. No necesita permisos de administrador.
#
#   Ejecútalo desde la carpeta del programa:   .\Instalar.ps1
#   Si ya había una versión instalada, se reemplaza por esta (tus ajustes y documentos recientes se conservan).
#
# Qué hace:
#   1. Copia el programa a %LOCALAPPDATA%\Programs\Velora
#   2. Crea un acceso directo en el menú Inicio
#   3. Registra Velora como programa que puede abrir archivos .pdf («Abrir con» y «Aplicaciones predeterminadas»)
#   4. Lo añade a «Aplicaciones instaladas» para poder desinstalarlo desde Configuración
#
# Opciones:  -Destino <carpeta>  -SinAsociacion  -SinAccesoDirecto  -CerrarProgramas  -Silencioso
param(
    [string]$Destino = (Join-Path $env:LOCALAPPDATA 'Programs\Velora'),
    [switch]$SinAsociacion,
    [switch]$SinAccesoDirecto,
    [switch]$CerrarProgramas,
    [switch]$Silencioso,
    # Solo para pruebas: dónde se escribe en el registro y dónde va el acceso directo.
    [string]$RaizClases = 'HKCU:\Software\Classes',
    [string]$RaizSoftware = 'HKCU:\Software',
    [string]$CarpetaInicio = (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs')
)
$ErrorActionPreference = 'Stop'
$origen = $PSScriptRoot
$exeOrigen = Join-Path $origen 'Velora.exe'
if (-not (Test-Path $exeOrigen)) { throw "No encuentro Velora.exe en $origen. Ejecuta este script desde la carpeta del programa." }

function Escribir($texto, $color = 'Gray') { if (-not $Silencioso) { Write-Host $texto -ForegroundColor $color } }

$destinoCompleto = [IO.Path]::GetFullPath($Destino)
if ([IO.Path]::GetPathRoot($destinoCompleto) -eq $destinoCompleto) { throw 'El destino no puede ser la raíz de una unidad.' }
if ((Test-Path $destinoCompleto) -and (Get-ChildItem $destinoCompleto -Force | Measure-Object).Count -gt 0 -and -not (Test-Path (Join-Path $destinoCompleto 'Velora.exe'))) {
    throw "La carpeta $destinoCompleto ya existe y no parece una instalación de Velora. Elige otra con -Destino."
}
if ($origen.TrimEnd('\') -ieq $destinoCompleto.TrimEnd('\')) { throw 'El origen y el destino son la misma carpeta.' }

# 1) Programa en ejecución
$exeDestino = Join-Path $destinoCompleto 'Velora.exe'
$enMarcha = @(Get-Process -Name Velora -ErrorAction SilentlyContinue | Where-Object { $_.Path -and ($_.Path -ieq $exeDestino) })
if ($enMarcha.Count -gt 0) {
    if ($CerrarProgramas) {
        Escribir 'Cerrando Velora…'
        $enMarcha | ForEach-Object { $_.CloseMainWindow() | Out-Null }
        Start-Sleep -Seconds 2
        $enMarcha | Where-Object { -not $_.HasExited } | Stop-Process -Force
    } else {
        throw 'Velora está abierto. Ciérralo (guarda antes tus anotaciones) o vuelve a ejecutar con -CerrarProgramas.'
    }
}

# 2) Copia de archivos (se refleja la carpeta: lo que ya no exista en la versión nueva se elimina)
$version = (Get-Item $exeOrigen).VersionInfo.ProductVersion
if (-not $version) { $version = '1.0.0' }
$version = ($version -split '\+')[0]
$anterior = $null
if (Test-Path $exeDestino) { $anterior = ((Get-Item $exeDestino).VersionInfo.ProductVersion -split '\+')[0] }
Escribir ("Instalando Velora {0} en {1}…" -f $version, $destinoCompleto) 'Cyan'
New-Item -ItemType Directory -Force $destinoCompleto | Out-Null
$null = robocopy $origen $destinoCompleto /MIR /NFL /NDL /NJH /NJS /NP /XF Instalar.ps1 Desinstalar.ps1
if ($LASTEXITCODE -ge 8) { throw "No se pudieron copiar los archivos (robocopy código $LASTEXITCODE)." }
Copy-Item (Join-Path $origen 'Desinstalar.ps1') $destinoCompleto -Force
Copy-Item (Join-Path $origen 'Instalar.ps1') $destinoCompleto -Force

# 3) Acceso directo en el menú Inicio
if (-not $SinAccesoDirecto) {
    New-Item -ItemType Directory -Force $CarpetaInicio | Out-Null
    $lnk = Join-Path $CarpetaInicio 'Velora.lnk'
    $sh = New-Object -ComObject WScript.Shell
    $acceso = $sh.CreateShortcut($lnk)
    $acceso.TargetPath = $exeDestino
    $acceso.WorkingDirectory = $destinoCompleto
    $acceso.Description = 'Visor de PDF rápido y ligero'
    $acceso.IconLocation = "$exeDestino,0"
    $acceso.Save()
    Escribir 'Acceso directo creado en el menú Inicio.'
}

# 4) Asociación con los archivos .pdf («Abrir con» y «Aplicaciones predeterminadas»)
if (-not $SinAsociacion) {
    $progId = 'Velora.Documento'
    $comando = "`"$exeDestino`" `"%1`""
    New-Item -Path "$RaizClases\$progId" -Force | Out-Null
    Set-ItemProperty -Path "$RaizClases\$progId" -Name '(default)' -Value 'Documento PDF'
    New-Item -Path "$RaizClases\$progId\DefaultIcon" -Force | Out-Null
    Set-ItemProperty -Path "$RaizClases\$progId\DefaultIcon" -Name '(default)' -Value "`"$exeDestino`",0"
    New-Item -Path "$RaizClases\$progId\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path "$RaizClases\$progId\shell\open\command" -Name '(default)' -Value $comando

    New-Item -Path "$RaizClases\Applications\Velora.exe\shell\open\command" -Force | Out-Null
    Set-ItemProperty -Path "$RaizClases\Applications\Velora.exe\shell\open\command" -Name '(default)' -Value $comando
    New-Item -Path "$RaizClases\Applications\Velora.exe\SupportedTypes" -Force | Out-Null
    New-ItemProperty -Path "$RaizClases\Applications\Velora.exe\SupportedTypes" -Name '.pdf' -Value '' -PropertyType String -Force | Out-Null

    New-Item -Path "$RaizClases\.pdf\OpenWithProgids" -Force | Out-Null
    New-ItemProperty -Path "$RaizClases\.pdf\OpenWithProgids" -Name $progId -Value ([byte[]]@()) -PropertyType None -Force | Out-Null

    # Para que aparezca en Configuración → Aplicaciones → Aplicaciones predeterminadas
    New-Item -Path "$RaizSoftware\Velora\Capabilities\FileAssociations" -Force | Out-Null
    Set-ItemProperty -Path "$RaizSoftware\Velora\Capabilities" -Name 'ApplicationName' -Value 'Velora'
    Set-ItemProperty -Path "$RaizSoftware\Velora\Capabilities" -Name 'ApplicationDescription' -Value 'Visor de PDF rápido y ligero'
    Set-ItemProperty -Path "$RaizSoftware\Velora\Capabilities\FileAssociations" -Name '.pdf' -Value $progId
    New-Item -Path "$RaizSoftware\RegisteredApplications" -Force | Out-Null
    Set-ItemProperty -Path "$RaizSoftware\RegisteredApplications" -Name 'Velora' -Value 'Software\Velora\Capabilities'
    Escribir 'Velora registrado como programa para abrir archivos .pdf.'
}

# 5) «Aplicaciones instaladas»
$clave = "$RaizSoftware\Microsoft\Windows\CurrentVersion\Uninstall\Velora"
New-Item -Path $clave -Force | Out-Null
$tamKb = [int]((Get-ChildItem $destinoCompleto -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
$valores = @{
    DisplayName = 'Velora'; DisplayVersion = $version; Publisher = 'Velora'; InstallLocation = $destinoCompleto
    DisplayIcon = $exeDestino; EstimatedSize = $tamKb
    UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$(Join-Path $destinoCompleto 'Desinstalar.ps1')`""
}
foreach ($k in $valores.Keys) { Set-ItemProperty -Path $clave -Name $k -Value $valores[$k] }
New-ItemProperty -Path $clave -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $clave -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null

# Avisar al Explorador de que cambiaron las asociaciones
if (-not $SinAsociacion -and $RaizClases -eq 'HKCU:\Software\Classes') {
    Add-Type -Namespace Win32 -Name Shell -MemberDefinition '[System.Runtime.InteropServices.DllImport("shell32.dll")] public static extern void SHChangeNotify(int e, uint f, IntPtr a, IntPtr b);'
    [Win32.Shell]::SHChangeNotify(0x08000000, 0, [IntPtr]::Zero, [IntPtr]::Zero)
}

if ($anterior) { Escribir ("Actualizado de la versión {0} a la {1}." -f $anterior, $version) 'Green' }
else { Escribir ("Velora {0} instalado." -f $version) 'Green' }
Escribir ''
Escribir 'Para abrir los PDF con Velora siempre: Configuración → Aplicaciones → Aplicaciones predeterminadas → Velora → .pdf.'
Escribir 'Windows no permite que un programa se ponga solo como predeterminado: ese último paso es tuyo.'

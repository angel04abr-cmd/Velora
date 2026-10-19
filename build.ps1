# Compila, prueba y empaqueta Velora.
#
#   .\build.ps1                       Compila en Release y deja el programa en dist\Velora-<versión>-win-x64\ (copia portátil)
#   .\build.ps1 -Probar               Antes de empaquetar, ejecuta las pruebas automáticas
#   .\build.ps1 -Zip                  Además crea dist\Velora-<versión>-win-x64.zip (copia portátil) y su huella SHA-256
#   .\build.ps1 -Instalador           Además crea el instalador dist\Velora-<versión>-x64.msi (se instala en C:\Program Files\Velora
#                                     para todo el equipo, aparece en «Aplicaciones instaladas» y se puede desinstalar desde allí)
#   .\build.ps1 -Instalar             Crea el instalador y lo ejecuta (Windows pedirá permiso de administrador)
#   .\build.ps1 -Publicar             Prueba, empaqueta y publica la versión en GitHub (etiqueta vX.Y.Z + Release con el .msi, el .zip y sus huellas).
#                                     Las instalaciones de Velora la detectan y se actualizan solas. Necesita `gh` con sesión iniciada.
#   .\build.ps1 -Independiente        Incluye .NET dentro de la copia portátil (más grande, pero no exige tenerlo instalado)
param(
    [switch]$Probar,
    [switch]$Zip,
    [switch]$Instalador,
    [switch]$Instalar,
    [switch]$Independiente,
    [switch]$Publicar
)
$ErrorActionPreference = 'Stop'
$raiz = $PSScriptRoot
Set-Location $raiz

$csproj = Join-Path $raiz 'src\Velora.App\Velora.App.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No se encontró <Version> en el proyecto.' }
$nombre = "Velora-$version-win-x64" + $(if ($Independiente) { '-independiente' } else { '' })
$salida = Join-Path $raiz "dist\$nombre"

if ($Publicar) { $Probar = $true; $Zip = $true; $Instalador = $true }
if ($Instalar) { $Instalador = $true }
if ($Independiente -and $Instalador) { throw 'El instalador solo se crea con el paquete normal (sin -Independiente).' }

function Escribir-Huella([string]$archivo) {
    $hash = (Get-FileHash $archivo -Algorithm SHA256).Hash.ToLower()
    "$hash  $(Split-Path $archivo -Leaf)" | Set-Content "$archivo.sha256" -Encoding ascii
    return $hash
}

if ($Probar) {
    Write-Host '==> Ejecutando las pruebas…' -ForegroundColor Cyan
    dotnet test (Join-Path $raiz 'tests\Velora.Tests') -nologo
    if ($LASTEXITCODE -ne 0) { throw 'Las pruebas fallaron; no se empaqueta.' }
}

Write-Host "==> Publicando Velora $version…" -ForegroundColor Cyan
if (Test-Path $salida) { Remove-Item $salida -Recurse -Force }
$sc = if ($Independiente) { 'true' } else { 'false' }
dotnet publish $csproj -c Release -r win-x64 --self-contained $sc -p:PublishReadyToRun=true -p:DebugType=none -p:DebugSymbols=false -o $salida -nologo
if ($LASTEXITCODE -ne 0) { throw 'La publicación falló.' }

# Licencias, avisos y manual junto al programa
Copy-Item (Join-Path $raiz 'README.md') $salida
Copy-Item (Join-Path $raiz 'docs\MANUAL.md') $salida
if (-not (Test-Path (Join-Path $salida 'LICENSES'))) { Copy-Item (Join-Path $raiz 'LICENSES') $salida -Recurse }

$tam = (Get-ChildItem $salida -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Listo: {0} ({1:0.0} MB)" -f $salida, $tam) -ForegroundColor Green

$archivo = $null
if ($Zip) {
    $archivo = Join-Path $raiz "dist\$nombre.zip"
    if (Test-Path $archivo) { Remove-Item $archivo }
    Compress-Archive -Path $salida -DestinationPath $archivo -CompressionLevel Optimal
    $hash = Escribir-Huella $archivo
    Write-Host ("Zip: {0} ({1:0.0} MB)  SHA256 {2}" -f $archivo, ((Get-Item $archivo).Length / 1MB), $hash) -ForegroundColor Green
}

$msi = $null
if ($Instalador) {
    # WiX 6 (última versión sin cuota OSMF). Se instala en .tools\ la primera vez.
    $wix = Join-Path $raiz '.tools\wix.exe'
    if (-not (Test-Path $wix)) {
        dotnet tool install wix --version 6.0.2 --tool-path (Join-Path $raiz '.tools')
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo instalar WiX.' }
    }
    & $wix extension add WixToolset.UI.wixext/6.0.2 | Out-Null
    & $wix extension add WixToolset.Netfx.wixext/6.0.2 | Out-Null
    $msi = Join-Path $raiz "dist\Velora-$version-x64.msi"
    if (Test-Path $msi) { Remove-Item $msi }
    & $wix build installer\Velora.wxs -arch x64 -d "Version=$version" -d "Source=$salida" -ext WixToolset.UI.wixext -ext WixToolset.Netfx.wixext -culture es-ES -o $msi
    if ($LASTEXITCODE -ne 0) { throw 'Fallo al crear el instalador MSI.' }
    $hash = Escribir-Huella $msi
    Write-Host ("Instalador: {0} ({1:0.0} MB)  SHA256 {2}" -f $msi, ((Get-Item $msi).Length / 1MB), $hash) -ForegroundColor Green
}

if ($Instalar) {
    Write-Host '==> Ejecutando el instalador (Windows pedirá permiso de administrador)…' -ForegroundColor Cyan
    Start-Process msiexec -ArgumentList '/i', "`"$msi`"" -Wait
}

if ($Publicar) {
    $etiqueta = "v$version"
    if (git status --porcelain) { throw 'Hay cambios sin guardar en git: haz un commit antes de publicar.' }
    if (git tag --list $etiqueta) { throw "La etiqueta $etiqueta ya existe: sube la versión en los .csproj antes de publicar." }

    # Las notas de la versión salen de CHANGELOG.md
    $registro = Get-Content (Join-Path $raiz 'CHANGELOG.md') -Raw -Encoding utf8
    $m = [regex]::Match($registro, "(?ms)^## \[$([regex]::Escape($version))\][^\r\n]*\r?\n(.*?)(?=^## \[|\z)")
    if (-not $m.Success) { throw "No hay una sección [$version] en CHANGELOG.md." }
    $notas = Join-Path $raiz "dist\notas-$version.md"
    $m.Groups[1].Value.Trim() | Set-Content $notas -Encoding utf8

    Write-Host "==> Publicando $etiqueta en GitHub…" -ForegroundColor Cyan
    # git escribe su progreso por stderr: se ejecuta con errores no fatales y se comprueba el código de salida
    function Invocar-Git { $antes = $ErrorActionPreference; $ErrorActionPreference = 'Continue'; & git.exe @args 2>&1 | ForEach-Object { "$_" } | Write-Host; $ErrorActionPreference = $antes; if ($LASTEXITCODE -ne 0) { throw "git $args falló" } }
    Invocar-Git tag -a $etiqueta -m "Velora $version"
    Invocar-Git push origin HEAD
    Invocar-Git push origin $etiqueta
    gh release create $etiqueta $msi "$msi.sha256" $archivo "$archivo.sha256" --title "Velora $version" --notes-file $notas --verify-tag
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la versión en GitHub.' }
    Write-Host "Publicada: $(gh release view $etiqueta --json url --jq .url)" -ForegroundColor Green
}

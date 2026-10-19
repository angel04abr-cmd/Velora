# Compila, prueba y empaqueta Velora.
#
#   .\build.ps1                       Compila en Release y deja el programa en dist\Velora-<versión>-win-x64\
#   .\build.ps1 -Probar               Antes de empaquetar, ejecuta las pruebas automáticas
#   .\build.ps1 -Zip                  Además crea dist\Velora-<versión>-win-x64.zip (y su suma SHA256)
#   .\build.ps1 -Independiente        Incluye .NET dentro del paquete (más grande, pero no exige tenerlo instalado)
#   .\build.ps1 -Instalar             Tras empaquetar, instala esa versión en tu usuario (actualiza la que tengas)
#   .\build.ps1 -Publicar             Prueba, empaqueta y publica la versión en GitHub (etiqueta vX.Y.Z + Release con el .zip y su huella).
#                                     Las instalaciones de Velora la detectan y se actualizan solas. Necesita `gh` con sesión iniciada.
param(
    [switch]$Probar,
    [switch]$Zip,
    [switch]$Independiente,
    [switch]$Instalar,
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

if ($Publicar) { $Probar = $true; $Zip = $true }

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

# Licencias, avisos y scripts de instalación junto al programa
Copy-Item (Join-Path $raiz 'tools\Instalar.ps1') $salida
Copy-Item (Join-Path $raiz 'tools\Desinstalar.ps1') $salida
Copy-Item (Join-Path $raiz 'README.md') $salida
Copy-Item (Join-Path $raiz 'docs\MANUAL.md') $salida
if (-not (Test-Path (Join-Path $salida 'LICENSES'))) { Copy-Item (Join-Path $raiz 'LICENSES') $salida -Recurse }
Get-ChildItem $salida -Filter 'LICENSE*' | Out-Null

$tam = (Get-ChildItem $salida -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
Write-Host ("Listo: {0} ({1:0.0} MB)" -f $salida, $tam) -ForegroundColor Green

if ($Zip) {
    $archivo = Join-Path $raiz "dist\$nombre.zip"
    if (Test-Path $archivo) { Remove-Item $archivo }
    Compress-Archive -Path $salida -DestinationPath $archivo -CompressionLevel Optimal
    $hash = (Get-FileHash $archivo -Algorithm SHA256).Hash.ToLower()
    "$hash  $nombre.zip" | Set-Content (Join-Path $raiz "dist\$nombre.zip.sha256") -Encoding ascii
    Write-Host ("Zip: {0} ({1:0.0} MB)  SHA256 {2}" -f $archivo, ((Get-Item $archivo).Length / 1MB), $hash) -ForegroundColor Green
}

if ($Instalar) {
    & (Join-Path $salida 'Instalar.ps1')
}

if ($Publicar) {
    if ($Independiente) { throw 'Publica solo el paquete normal (sin -Independiente).' }
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
    gh release create $etiqueta $archivo "$archivo.sha256" --title "Velora $version" --notes-file $notas --verify-tag
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear la versión en GitHub.' }
    Write-Host "Publicada: $(gh release view $etiqueta --json url --jq .url)" -ForegroundColor Green
}

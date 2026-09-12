[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$frontendPath = Join-Path $repositoryRoot 'src\frontend'
$apiProject = Join-Path $repositoryRoot 'src\backend\NexoMail.Api\NexoMail.Api.csproj'
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath 'ionos'

Push-Location $frontendPath
try {
    pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw 'pnpm install fallo.' }

    pnpm lint
    if ($LASTEXITCODE -ne 0) { throw 'pnpm lint fallo.' }

    pnpm build
    if ($LASTEXITCODE -ne 0) { throw 'pnpm build fallo.' }
}
finally {
    Pop-Location
}

dotnet test (Join-Path $repositoryRoot 'NexoMail.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet test fallo.' }

if (Test-Path $publishPath) {
    Remove-Item $publishPath -Recurse -Force
}
dotnet publish $apiProject -c Release -o $publishPath --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish fallo.' }

if (-not (Test-Path (Join-Path $publishPath 'wwwroot\index.html'))) {
    throw 'El frontend no fue incorporado al paquete publicado.'
}

Write-Host ''
Write-Host 'Publicacion creada correctamente, sin comprimir:' -ForegroundColor Green
Write-Host $publishPath
Write-Host 'Ejecute scripts\configure-ionos-package.ps1 antes de subirla al servidor.'

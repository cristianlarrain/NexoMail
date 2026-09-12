[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$frontendPath = Join-Path $repositoryRoot 'src\frontend'
$apiProject = Join-Path $repositoryRoot 'src\backend\NexoMail.Api\NexoMail.Api.csproj'
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath 'ionos'
$zipPath = Join-Path $artifactsPath 'NexoMail-IONOS.zip'

Push-Location $frontendPath
try {
    pnpm install --frozen-lockfile
    if ($LASTEXITCODE -ne 0) { throw 'pnpm install falló.' }

    pnpm lint
    if ($LASTEXITCODE -ne 0) { throw 'pnpm lint falló.' }

    pnpm build
    if ($LASTEXITCODE -ne 0) { throw 'pnpm build falló.' }
}
finally {
    Pop-Location
}

dotnet test (Join-Path $repositoryRoot 'NexoMail.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'dotnet test falló.' }

if (Test-Path $publishPath) {
    Remove-Item $publishPath -Recurse -Force
}
dotnet publish $apiProject -c Release -o $publishPath --no-restore
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falló.' }

if (-not (Test-Path (Join-Path $publishPath 'wwwroot\index.html'))) {
    throw 'El frontend no fue incorporado al paquete publicado.'
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ''
Write-Host 'Paquete creado correctamente:' -ForegroundColor Green
Write-Host $zipPath
Write-Host 'No lo suba hasta configurar web.config con los secretos de producción.'

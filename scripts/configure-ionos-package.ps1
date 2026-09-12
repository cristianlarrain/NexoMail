[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath 'ionos'
$webConfigPath = Join-Path $publishPath 'web.config'
$zipPath = Join-Path $artifactsPath 'NexoMail-IONOS.zip'

if (-not (Test-Path $webConfigPath)) {
    throw 'No existe artifacts\ionos\web.config. Ejecute primero scripts\publish-ionos.ps1.'
}

$securePassword = Read-Host 'Contraseña MSSQL de dbo1111108584' -AsSecureString
$smtpAddress = Read-Host 'Cuenta IONOS remitente (por ejemplo, contacto@eidosdigital.cl)'
if ([string]::IsNullOrWhiteSpace($smtpAddress)) {
    throw 'La cuenta IONOS remitente no puede estar vacía.'
}
$ownerEmail = Read-Host 'Correo de la cuenta propietaria de NexoMail'
if ([string]::IsNullOrWhiteSpace($ownerEmail)) {
    throw 'El correo de la cuenta propietaria no puede estar vacío.'
}
$secureSmtpPassword = Read-Host 'Contraseña de la cuenta de correo IONOS' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$smtpPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSmtpPassword)

try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $plainSmtpPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($smtpPasswordPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'La contraseña MSSQL no puede estar vacía.'
    }
    if ([string]::IsNullOrWhiteSpace($plainSmtpPassword)) {
        throw 'La contraseña de la cuenta de correo IONOS no puede estar vacía.'
    }

    [xml]$configuration = Get-Content $webConfigPath -Raw
    $aspNetCore = $configuration.configuration.location.'system.webServer'.aspNetCore
    if ($null -eq $aspNetCore) {
        throw 'El web.config publicado no contiene la sección aspNetCore.'
    }

    $environmentVariables = $aspNetCore.environmentVariables
    if ($null -eq $environmentVariables) {
        $environmentVariables = $configuration.CreateElement('environmentVariables')
        [void]$aspNetCore.AppendChild($environmentVariables)
    }

    function Set-EnvironmentVariable([string]$name, [string]$value) {
        $entry = @($environmentVariables.environmentVariable) |
            Where-Object { $_.name -eq $name } |
            Select-Object -First 1

        if ($null -eq $entry) {
            $entry = $configuration.CreateElement('environmentVariable')
            [void]$environmentVariables.AppendChild($entry)
        }

        $entry.SetAttribute('name', $name)
        $entry.SetAttribute('value', $value)
    }

    $connectionString = 'Server=tcp:db1111108584.hosting-data.io;Database=db1111108584;User ID=dbo1111108584;Password=' +
        $plainPassword +
        ';Encrypt=True;TrustServerCertificate=False;MultipleActiveResultSets=True;'

    Set-EnvironmentVariable 'ASPNETCORE_ENVIRONMENT' 'Production'
    Set-EnvironmentVariable 'ASPNETCORE_FORWARDEDHEADERS_ENABLED' 'true'
    Set-EnvironmentVariable 'ConnectionStrings__NexoMail' $connectionString
    Set-EnvironmentVariable 'Bootstrap__OwnerEmail' $ownerEmail.Trim().ToLowerInvariant()
    Set-EnvironmentVariable 'RecoveryEmail__Host' 'smtp.ionos.com'
    Set-EnvironmentVariable 'RecoveryEmail__Port' '587'
    Set-EnvironmentVariable 'RecoveryEmail__UseSsl' 'true'
    Set-EnvironmentVariable 'RecoveryEmail__UserName' $smtpAddress
    Set-EnvironmentVariable 'RecoveryEmail__Password' $plainSmtpPassword
    Set-EnvironmentVariable 'RecoveryEmail__FromAddress' $smtpAddress
    Set-EnvironmentVariable 'RecoveryEmail__FromName' 'NexoMail'

    $xmlSettings = [System.Xml.XmlWriterSettings]::new()
    $xmlSettings.Indent = $true
    $xmlSettings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $writer = [System.Xml.XmlWriter]::Create($webConfigPath, $xmlSettings)
    try {
        $configuration.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}
finally {
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    if ($smtpPasswordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($smtpPasswordPointer)
    }
    $plainPassword = $null
    $plainSmtpPassword = $null
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $publishPath '*') -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ''
Write-Host 'Paquete configurado correctamente:' -ForegroundColor Green
Write-Host $zipPath
Write-Host 'El ZIP contiene la contraseña MSSQL. No lo comparta ni lo suba a Git.'

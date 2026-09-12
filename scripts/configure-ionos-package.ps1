[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath 'ionos'
$webConfigPath = Join-Path $publishPath 'web.config'

if (-not (Test-Path $webConfigPath)) {
    throw 'No existe artifacts\ionos\web.config. Ejecute primero scripts\publish-ionos.ps1.'
}

Write-Host ''
Write-Host 'CUENTAS DE PRODUCCION' -ForegroundColor Cyan
$smtpAddress = Read-Host '1. Cuenta IONOS que enviara codigos (ejemplo: contacto@eidosdigital.cl)'
if ([string]::IsNullOrWhiteSpace($smtpAddress)) {
    throw 'La cuenta IONOS remitente no puede estar vacia.'
}
$ownerEmail = Read-Host '2. Cuenta de usuario NexoMail con acceso total (NO ingrese la cuenta remitente)'
if ([string]::IsNullOrWhiteSpace($ownerEmail)) {
    throw 'La cuenta propietaria de NexoMail no puede estar vacia.'
}

Write-Host ''
Write-Host 'CONTRASENAS DE PRODUCCION' -ForegroundColor Cyan
$securePassword = Read-Host '3. Contrasena MSSQL del usuario dbo1111108584' -AsSecureString
$secureSmtpPassword = Read-Host "4. Contrasena del correo IONOS $smtpAddress" -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$smtpPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSmtpPassword)

try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $plainSmtpPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($smtpPasswordPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'La contrasena MSSQL no puede estar vacia.'
    }
    if ([string]::IsNullOrWhiteSpace($plainSmtpPassword)) {
        throw 'La contrasena de la cuenta de correo IONOS no puede estar vacia.'
    }

    [xml]$configuration = Get-Content $webConfigPath -Raw
    $aspNetCore = $configuration.configuration.location.'system.webServer'.aspNetCore
    if ($null -eq $aspNetCore) {
        throw 'El web.config publicado no contiene la seccion aspNetCore.'
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

Write-Host ''
Write-Host 'Publicacion configurada correctamente, sin comprimir:' -ForegroundColor Green
Write-Host $publishPath
Write-Host 'La carpeta contiene secretos en web.config. No la comparta ni la suba a Git.'

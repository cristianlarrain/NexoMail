[CmdletBinding()]
param([switch]$SaveSecrets)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$artifactsPath = Join-Path $repositoryRoot 'artifacts'
$publishPath = Join-Path $artifactsPath 'ionos'
$webConfigPath = Join-Path $publishPath 'web.config'
$secretsModulePath = Join-Path $PSScriptRoot 'ionos-secure-secrets.psm1'

Import-Module $secretsModulePath -Force
$secretsPath = Get-NexoMailProductionSecretsPath

if (-not (Test-Path $webConfigPath)) {
    throw 'No existe artifacts\ionos\web.config. Ejecute primero scripts\publish-ionos.ps1.'
}

if ($SaveSecrets) {
    Write-Host ''
    Write-Host 'GUARDAR CREDENCIALES CIFRADAS DE PRODUCCION' -ForegroundColor Cyan
    $smtpAddress = Read-Host '1. Cuenta IONOS que enviara codigos (ejemplo: contacto@eidosdigital.cl)'
    $ownerEmail = Read-Host '2. Cuenta de usuario NexoMail con acceso total (NO ingrese la cuenta remitente)'
    $securePassword = Read-Host '3. Contrasena MSSQL del usuario dbo1111108584' -AsSecureString
    $secureSmtpPassword = Read-Host "4. Contrasena del correo IONOS $smtpAddress" -AsSecureString
    $googleClientId = Read-Host '5. Google OAuth Client ID'
    $secureGoogleClientSecret = Read-Host '6. Google OAuth Client Secret' -AsSecureString
    $secureOpenAiApiKey = Read-Host '7. OpenAI API Key' -AsSecureString

    if ([string]::IsNullOrWhiteSpace($smtpAddress)) { throw 'La cuenta IONOS remitente no puede estar vacia.' }
    if ([string]::IsNullOrWhiteSpace($ownerEmail)) { throw 'La cuenta propietaria de NexoMail no puede estar vacia.' }
    if ([string]::IsNullOrWhiteSpace($googleClientId)) { throw 'El Google OAuth Client ID no puede estar vacio.' }

    $productionSecrets = [pscustomobject]@{
        SmtpAddress        = $smtpAddress.Trim().ToLowerInvariant()
        OwnerEmail         = $ownerEmail.Trim().ToLowerInvariant()
        MssqlPassword      = $securePassword
        SmtpPassword       = $secureSmtpPassword
        GoogleClientId     = $googleClientId.Trim()
        GoogleClientSecret = $secureGoogleClientSecret
        OpenAiApiKey       = $secureOpenAiApiKey
    }
    Save-NexoMailProductionSecrets -Path $secretsPath -Secrets $productionSecrets
    Write-Host "Credenciales cifradas guardadas en: $secretsPath" -ForegroundColor Green
}
elseif (-not (Test-Path $secretsPath)) {
    throw "No existen credenciales cifradas. Ejecute primero: .\scripts\configure-ionos-package.ps1 -SaveSecrets"
}

$productionSecrets = Get-NexoMailProductionSecrets -Path $secretsPath
$smtpAddress = $productionSecrets.SmtpAddress
$ownerEmail = $productionSecrets.OwnerEmail
$securePassword = $productionSecrets.MssqlPassword
$secureSmtpPassword = $productionSecrets.SmtpPassword
$googleClientId = $productionSecrets.GoogleClientId
$secureGoogleClientSecret = $productionSecrets.GoogleClientSecret
$secureOpenAiApiKey = $productionSecrets.OpenAiApiKey

Write-Host ''
Write-Host "Usando credenciales cifradas de: $secretsPath" -ForegroundColor Cyan

$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$smtpPasswordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSmtpPassword)
$googleSecretPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureGoogleClientSecret)
$openAiKeyPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureOpenAiApiKey)

try {
    $plainPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $plainSmtpPassword = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($smtpPasswordPointer)
    $plainGoogleClientSecret = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($googleSecretPointer)
    $plainOpenAiApiKey = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($openAiKeyPointer)
    if ([string]::IsNullOrWhiteSpace($plainPassword)) {
        throw 'La contrasena MSSQL no puede estar vacia.'
    }
    if ([string]::IsNullOrWhiteSpace($plainSmtpPassword)) {
        throw 'La contrasena de la cuenta de correo IONOS no puede estar vacia.'
    }
    if ([string]::IsNullOrWhiteSpace($plainGoogleClientSecret)) {
        throw 'El Google OAuth Client Secret no puede estar vacio.'
    }
    if ([string]::IsNullOrWhiteSpace($plainOpenAiApiKey)) {
        throw 'La OpenAI API Key no puede estar vacia.'
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
    Set-EnvironmentVariable 'DOTNET_SYSTEM_NET_DISABLEIPV6' '1'
    Set-EnvironmentVariable 'HTTP_PROXY' 'http://winproxyus1.server.lan:3128'
    Set-EnvironmentVariable 'HTTPS_PROXY' 'http://winproxyus1.server.lan:3128'
    Set-EnvironmentVariable 'NO_PROXY' 'localhost,127.0.0.1'
    Set-EnvironmentVariable 'ConnectionStrings__NexoMail' $connectionString
    Set-EnvironmentVariable 'Bootstrap__OwnerEmail' $ownerEmail.Trim().ToLowerInvariant()
    Set-EnvironmentVariable 'RecoveryEmail__Host' 'smtp.ionos.com'
    Set-EnvironmentVariable 'RecoveryEmail__Port' '587'
    Set-EnvironmentVariable 'RecoveryEmail__UseSsl' 'true'
    Set-EnvironmentVariable 'RecoveryEmail__UserName' $smtpAddress
    Set-EnvironmentVariable 'RecoveryEmail__Password' $plainSmtpPassword
    Set-EnvironmentVariable 'RecoveryEmail__FromAddress' $smtpAddress
    Set-EnvironmentVariable 'RecoveryEmail__FromName' 'NexoMail'
    Set-EnvironmentVariable 'Google__ClientId' $googleClientId.Trim()
    Set-EnvironmentVariable 'Google__ClientSecret' $plainGoogleClientSecret
    Set-EnvironmentVariable 'Google__RedirectUri' 'https://nexomail.eidosdigital.cl/api/oauth/google/callback'
    Set-EnvironmentVariable 'AI__ApiKey' $plainOpenAiApiKey

    $systemWebServer = $configuration.configuration.location.'system.webServer'
    $rewrite = $systemWebServer.rewrite
    if ($null -ne $rewrite) {
        [void]$systemWebServer.RemoveChild($rewrite)
    }

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
    if ($googleSecretPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($googleSecretPointer)
    }
    if ($openAiKeyPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($openAiKeyPointer)
    }
    $plainPassword = $null
    $plainSmtpPassword = $null
    $plainGoogleClientSecret = $null
    $plainOpenAiApiKey = $null
}

Write-Host ''
Write-Host 'Publicacion configurada correctamente, sin comprimir:' -ForegroundColor Green
Write-Host $publishPath
Write-Host 'La carpeta contiene secretos en web.config. No la comparta ni la suba a Git.'

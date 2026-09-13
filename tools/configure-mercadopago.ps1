param(
    [string]$BackUrl = "http://localhost:5173/settings/plan?billing=return",
    [string]$PublicBaseUrl = ""
)

$ErrorActionPreference = "Stop"

function Convert-SecureStringToPlainText([Security.SecureString]$SecureValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

function Ensure-Property($Object, [string]$Name, $Value) {
    if ($null -eq $Object.PSObject.Properties[$Name]) {
        $Object | Add-Member -MemberType NoteProperty -Name $Name -Value $Value
    }
    return $Object.$Name
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiRoot = Join-Path $repoRoot "src\backend\NexoMail.Api"
$configPath = Join-Path $apiRoot "appsettings.Development.json"
$examplePath = Join-Path $apiRoot "appsettings.Development.example.json"

if (Test-Path $configPath) {
    $config = Get-Content $configPath -Raw | ConvertFrom-Json
}
elseif (Test-Path $examplePath) {
    $config = Get-Content $examplePath -Raw | ConvertFrom-Json
}
else {
    $config = [pscustomobject]@{}
}

$billing = Ensure-Property $config "Billing" ([pscustomobject]@{})
$mercadoPago = Ensure-Property $billing "MercadoPago" ([pscustomobject]@{})

$accessTokenSecure = Read-Host "Access Token de prueba de Mercado Pago" -AsSecureString
$webhookSecretSecure = Read-Host "Clave secreta del Webhook de Mercado Pago" -AsSecureString

$accessToken = Convert-SecureStringToPlainText $accessTokenSecure
$webhookSecret = Convert-SecureStringToPlainText $webhookSecretSecure

if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "El Access Token no puede quedar vacío."
}
if ([string]::IsNullOrWhiteSpace($webhookSecret)) {
    throw "La clave secreta del Webhook no puede quedar vacía."
}

Ensure-Property $mercadoPago "AccessToken" "" | Out-Null
Ensure-Property $mercadoPago "WebhookSecret" "" | Out-Null
Ensure-Property $mercadoPago "BackUrl" "" | Out-Null

$mercadoPago.AccessToken = $accessToken.Trim()
$mercadoPago.WebhookSecret = $webhookSecret.Trim()
$mercadoPago.BackUrl = $BackUrl.Trim()

$config | ConvertTo-Json -Depth 20 | Set-Content $configPath -Encoding UTF8

Write-Host ""
Write-Host "Configuración local de Mercado Pago guardada en:"
Write-Host $configPath
Write-Host ""
Write-Host "Este archivo está excluido de Git y no debe subirse al repositorio."

if (-not [string]::IsNullOrWhiteSpace($PublicBaseUrl)) {
    $base = $PublicBaseUrl.TrimEnd('/')
    Write-Host ""
    Write-Host "Configure en Mercado Pago este Webhook:"
    Write-Host "$base/api/commercial/webhooks/mercadopago"
    Write-Host "Eventos: subscription_preapproval y payment"
}
else {
    Write-Host ""
    Write-Host "Para recibir Webhooks durante pruebas, ejecute nuevamente el script con -PublicBaseUrl usando una URL HTTPS pública que apunte a NexoMail.Api."
    Write-Host "Ejemplo: .\tools\configure-mercadopago.ps1 -PublicBaseUrl https://su-url-publica.example"
}

Write-Host ""
Write-Host "Reinicie NexoMail.Api después de guardar la configuración."

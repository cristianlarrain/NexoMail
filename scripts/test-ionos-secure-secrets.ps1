$ErrorActionPreference = 'Stop'

$modulePath = Join-Path $PSScriptRoot 'ionos-secure-secrets.psm1'
Import-Module $modulePath -Force

$testDirectory = Join-Path ([System.IO.Path]::GetTempPath()) ("nexomail-secrets-test-" + [guid]::NewGuid().ToString('N'))
$testPath = Join-Path $testDirectory 'production-secrets.clixml'

function Convert-SecureStringToPlainText([securestring]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
    }
}

try {
    $secrets = [pscustomobject]@{
        SmtpAddress       = 'sender@example.test'
        OwnerEmail        = 'owner@example.test'
        MssqlPassword     = ConvertTo-SecureString 'mssql-test-secret' -AsPlainText -Force
        SmtpPassword      = ConvertTo-SecureString 'smtp-test-secret' -AsPlainText -Force
        GoogleClientId    = 'google-client-id.apps.googleusercontent.com'
        GoogleClientSecret = ConvertTo-SecureString 'google-test-secret' -AsPlainText -Force
        OpenAiApiKey      = ConvertTo-SecureString 'sk-test-secret' -AsPlainText -Force
    }

    Save-NexoMailProductionSecrets -Path $testPath -Secrets $secrets

    $looseAcl = Get-Acl $testPath
    $looseAcl.SetAccessRuleProtection($false, $true)
    Set-Acl -Path $testPath -AclObject $looseAcl

    $loaded = Get-NexoMailProductionSecrets -Path $testPath

    if (-not (Get-Acl $testPath).AreAccessRulesProtected) {
        throw 'La lectura no reparó los permisos heredados del archivo cifrado.'
    }

    if ($loaded.SmtpAddress -ne $secrets.SmtpAddress) { throw 'No se recuperó la cuenta SMTP.' }
    if ($loaded.OwnerEmail -ne $secrets.OwnerEmail) { throw 'No se recuperó la cuenta propietaria.' }
    if ($loaded.GoogleClientId -ne $secrets.GoogleClientId) { throw 'No se recuperó Google Client ID.' }
    if ((Convert-SecureStringToPlainText $loaded.MssqlPassword) -ne 'mssql-test-secret') { throw 'No se recuperó la contraseña MSSQL.' }
    if ((Convert-SecureStringToPlainText $loaded.SmtpPassword) -ne 'smtp-test-secret') { throw 'No se recuperó la contraseña SMTP.' }
    if ((Convert-SecureStringToPlainText $loaded.GoogleClientSecret) -ne 'google-test-secret') { throw 'No se recuperó Google Client Secret.' }
    if ((Convert-SecureStringToPlainText $loaded.OpenAiApiKey) -ne 'sk-test-secret') { throw 'No se recuperó OpenAI API Key.' }

    $raw = Get-Content $testPath -Raw
    foreach ($plainSecret in @('mssql-test-secret', 'smtp-test-secret', 'google-test-secret', 'sk-test-secret')) {
        if ($raw.Contains($plainSecret)) { throw "El archivo expuso el secreto: $plainSecret" }
    }

    Write-Host 'PASS: credenciales de producción cifradas y recuperables sólo por DPAPI.'
}
finally {
    if (Test-Path $testDirectory) {
        Remove-Item $testDirectory -Recurse -Force
    }
}

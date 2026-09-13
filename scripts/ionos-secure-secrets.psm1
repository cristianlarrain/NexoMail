Set-StrictMode -Version Latest

function Get-NexoMailProductionSecretsPath {
    $localAppData = [Environment]::GetFolderPath('LocalApplicationData')
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        throw 'Windows no informó la carpeta LOCALAPPDATA del usuario actual.'
    }

    return Join-Path (Join-Path $localAppData 'NexoMail') 'production-secrets.clixml'
}

function Save-NexoMailProductionSecrets {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [psobject]$Secrets
    )

    $directory = Split-Path -Parent $Path
    [void](New-Item -ItemType Directory -Path $directory -Force)

    $stored = [pscustomobject]@{
        Version            = 1
        SmtpAddress        = [string]$Secrets.SmtpAddress
        OwnerEmail         = [string]$Secrets.OwnerEmail
        MssqlPassword      = ConvertFrom-SecureString $Secrets.MssqlPassword
        SmtpPassword       = ConvertFrom-SecureString $Secrets.SmtpPassword
        GoogleClientId     = [string]$Secrets.GoogleClientId
        GoogleClientSecret = ConvertFrom-SecureString $Secrets.GoogleClientSecret
        OpenAiApiKey       = ConvertFrom-SecureString $Secrets.OpenAiApiKey
    }

    $stored | Export-Clixml -Path $Path -Force

    if ($IsWindows -or $env:OS -eq 'Windows_NT') {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
        $acl = Get-Acl $Path
        $acl.SetAccessRuleProtection($true, $false)
        $rule = [System.Security.AccessControl.FileSystemAccessRule]::new(
            $identity,
            [System.Security.AccessControl.FileSystemRights]::FullControl,
            [System.Security.AccessControl.AccessControlType]::Allow
        )
        $acl.SetAccessRule($rule)
        Set-Acl -Path $Path -AclObject $acl
    }
}

function Get-NexoMailProductionSecrets {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        throw "No existe el archivo cifrado: $Path"
    }

    $stored = Import-Clixml -Path $Path
    if ($stored.Version -ne 1) {
        throw 'El archivo de credenciales tiene una versión no compatible.'
    }

    foreach ($property in @('SmtpAddress', 'OwnerEmail', 'MssqlPassword', 'SmtpPassword', 'GoogleClientId', 'GoogleClientSecret', 'OpenAiApiKey')) {
        if ([string]::IsNullOrWhiteSpace([string]$stored.$property)) {
            throw "El archivo cifrado no contiene un valor válido para $property."
        }
    }

    return [pscustomobject]@{
        SmtpAddress        = [string]$stored.SmtpAddress
        OwnerEmail         = [string]$stored.OwnerEmail
        MssqlPassword      = ConvertTo-SecureString ([string]$stored.MssqlPassword)
        SmtpPassword       = ConvertTo-SecureString ([string]$stored.SmtpPassword)
        GoogleClientId     = [string]$stored.GoogleClientId
        GoogleClientSecret = ConvertTo-SecureString ([string]$stored.GoogleClientSecret)
        OpenAiApiKey       = ConvertTo-SecureString ([string]$stored.OpenAiApiKey)
    }
}

Export-ModuleMember -Function Get-NexoMailProductionSecretsPath, Save-NexoMailProductionSecrets, Get-NexoMailProductionSecrets

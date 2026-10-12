<#
.SYNOPSIS
    Generates the RSA key pair for the TimeWise Snowflake service user (TIMEWISE_SVC).

.DESCRIPTION
    - keys\rsa_key.p8  : private key, encrypted PKCS#8 (AES-256). Used by the API. Never commit it.
    - keys\rsa_key.pub : public key. Attached to the Snowflake user.
    - Prints the ready-to-run ALTER USER statement and the key fingerprint.
    - Stores the passphrase in the API project's user-secrets, outside the repo.

.EXAMPLE
    .\scripts\New-SnowflakeKeyPair.ps1
    .\scripts\New-SnowflakeKeyPair.ps1 -Force   # replace existing keys
#>
[CmdletBinding()]
param(
    [switch]$Force,
    [string]$SnowflakeUser = 'TIMEWISE_SVC'
)

$ErrorActionPreference = 'Stop'
$repoRoot   = Split-Path -Parent $PSScriptRoot
$keysDir    = Join-Path $repoRoot 'keys'
$privateKey = Join-Path $keysDir 'rsa_key.p8'
$publicKey  = Join-Path $keysDir 'rsa_key.pub'
$apiProject = Join-Path $repoRoot 'src\TimeWise.SnowflakePoc.Api'

$openssl = (Get-Command openssl -ErrorAction SilentlyContinue).Source
if (-not $openssl) {
    # Git for Windows ships openssl but doesn't always put it on PATH.
    $openssl = @(
        "$env:ProgramFiles\Git\mingw64\bin\openssl.exe",
        "$env:ProgramFiles\Git\usr\bin\openssl.exe",
        "${env:ProgramFiles(x86)}\Git\mingw64\bin\openssl.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $openssl) {
    throw 'openssl was not found. Install Git for Windows (it includes openssl) or OpenSSL for Windows.'
}
if ((Test-Path $privateKey) -and -not $Force) {
    throw "Keys already exist in $keysDir. Use -Force to replace them. Snowflake must then be updated with the new public key."
}
New-Item -ItemType Directory -Force -Path $keysDir | Out-Null

$secure = Read-Host -AsSecureString 'Choose a passphrase for the private key (min 8 chars)'
$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try { $passphrase = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
if ($passphrase.Length -lt 8) { throw 'Passphrase must be at least 8 characters.' }

$env:TW_SNOWFLAKE_KEY_PASS = $passphrase
try {
    # Encrypted PKCS#8 private key (2048-bit RSA, AES-256-CBC).
    & $openssl genpkey -algorithm RSA -pkeyopt rsa_keygen_bits:2048 -aes-256-cbc `
        -pass env:TW_SNOWFLAKE_KEY_PASS -out $privateKey
    if ($LASTEXITCODE -ne 0) { throw 'openssl genpkey failed.' }

    & $openssl pkey -in $privateKey -passin env:TW_SNOWFLAKE_KEY_PASS -pubout -out $publicKey
    if ($LASTEXITCODE -ne 0) { throw 'openssl pkey (public key export) failed.' }
}
finally {
    Remove-Item Env:\TW_SNOWFLAKE_KEY_PASS -ErrorAction SilentlyContinue
}

# Public key body = PEM without header/footer, as one line.
$pubBody = ((Get-Content $publicKey) | Where-Object { $_ -notmatch '-----' }) -join ''

# Same fingerprint Snowflake shows in DESC USER (RSA_PUBLIC_KEY_FP).
$sha = [Security.Cryptography.SHA256]::Create()
$fingerprint = 'SHA256:' + [Convert]::ToBase64String($sha.ComputeHash([Convert]::FromBase64String($pubBody)))

# Keep the passphrase out of the repo: store it in the API project's user-secrets.
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    & dotnet user-secrets set 'Snowflake:PrivateKeyPassphrase' $passphrase --project $apiProject | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host 'Passphrase stored in user-secrets (Snowflake:PrivateKeyPassphrase).' -ForegroundColor Green
    } else {
        Write-Warning 'Could not write user-secrets. Put the passphrase in appsettings.Development.json instead.'
    }
}

Write-Host ''
Write-Host "Private key : $privateKey" -ForegroundColor Green
Write-Host "Public key  : $publicKey"  -ForegroundColor Green
Write-Host "Fingerprint : $fingerprint"
Write-Host ''
Write-Host 'Run this in Snowsight (as ACCOUNTADMIN), then compare RSA_PUBLIC_KEY_FP with the fingerprint above:' -ForegroundColor Yellow
Write-Host ''
Write-Host "ALTER USER $SnowflakeUser SET RSA_PUBLIC_KEY = '$pubBody';"
Write-Host "DESC USER $SnowflakeUser;"
Write-Host ''

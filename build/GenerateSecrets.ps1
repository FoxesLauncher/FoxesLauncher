<#
.SYNOPSIS
    Generates Foxes Launcher\Modules\GeneratedSecrets.vb from local
    build configuration (.env) and build environment variables.

.DESCRIPTION
    Values are resolved in this order (first non-empty wins):
        1. Process environment variables (for CI secrets)
        2. Repository-root .env
        3. Repository-root .env.local (overrides .env)
        4. Built-in defaults

    The generated file is ignored by Git. Never commit real secrets to source.

.NOTES
    Keep this file ASCII-only: Windows PowerShell 5.1 reads BOM-less scripts
    using the system ANSI code page, which can corrupt non-ASCII characters.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = '',
    [string]$OutputFile = ''
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    $RepoRoot = Split-Path -Parent $PSScriptRoot
}

if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    $OutputFile = Join-Path $RepoRoot 'Foxes Launcher\Modules\GeneratedSecrets.vb'
}

function Read-DotEnv {
    param([Parameter(Mandatory)][string]$Path)

    $values = @{}
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $values
    }

    foreach ($line in Get-Content -LiteralPath $Path -Encoding UTF8) {
        $entry = $line.Trim()
        if ($entry.Length -eq 0 -or $entry.StartsWith('#')) {
            continue
        }
        if ($entry.StartsWith('export ')) {
            $entry = $entry.Substring(7).TrimStart()
        }

        $separator = $entry.IndexOf('=')
        if ($separator -lt 1) {
            continue
        }

        $key = $entry.Substring(0, $separator).Trim()
        $value = $entry.Substring($separator + 1).Trim()

        if ($value.Length -ge 2) {
            $first = $value[0]
            $last = $value[$value.Length - 1]
            if (($first -eq '"' -and $last -eq '"') -or ($first -eq "'" -and $last -eq "'")) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        $values[$key] = $value
    }

    return $values
}

function Get-GitCommitHash {
    param([Parameter(Mandatory)][string]$Root)

    try {
        $hash = & git -C $Root rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($hash)) {
            return $hash.Trim()
        }
    } catch {
        # Git is optional; an empty commit hash is acceptable for local builds.
    }
    return ''
}

function ConvertTo-VbStringLiteral {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        $Value = ''
    }
    $Value = $Value -replace "`r", '' -replace "`n", ''
    return '"' + $Value.Replace('"', '""') + '"'
}

# Merge .env and .env.local (the latter takes precedence).
$fileValues = @{}
$envFiles = @(
    (Join-Path $RepoRoot '.env'),
    (Join-Path $RepoRoot '.env.local')
)
foreach ($envFile in $envFiles) {
    foreach ($pair in (Read-DotEnv -Path $envFile).GetEnumerator()) {
        $fileValues[$pair.Key] = $pair.Value
    }
}

function Resolve-ConfigValue {
    param(
        [Parameter(Mandatory)][string]$Key,
        [AllowEmptyString()][string]$Default = ''
    )

    $environmentValue = [Environment]::GetEnvironmentVariable($Key, 'Process')
    if (-not [string]::IsNullOrWhiteSpace($environmentValue)) {
        return $environmentValue
    }
    if ($fileValues.ContainsKey($Key) -and -not [string]::IsNullOrWhiteSpace($fileValues[$Key])) {
        return $fileValues[$Key]
    }
    return $Default
}

$versionBranch = Resolve-ConfigValue -Key 'FXL_VERSION_BRANCH' -Default 'OpenSource'
$defaultRegFolder = if ($versionBranch -eq 'Official') { 'FXL' } else { 'FXLDebug' }
$regFolder = Resolve-ConfigValue -Key 'FXL_REG_FOLDER' -Default $defaultRegFolder
$msClientId = Resolve-ConfigValue -Key 'FXL_MS_CLIENT_ID'
$curseForgeApiKey = Resolve-ConfigValue -Key 'FXL_CURSEFORGE_API_KEY'
$clsBaseUrl = Resolve-ConfigValue -Key 'FXL_CLS_BASE_URL'

# Update / telemetry configuration. Every address and key is supplied by .env.
$updateChannel = Resolve-ConfigValue -Key 'FXL_UPDATE_CHANNEL' -Default 'official'
$updatePlatform = Resolve-ConfigValue -Key 'FXL_UPDATE_PLATFORM' -Default 'win-x64'
$updateR2Latest = Resolve-ConfigValue -Key 'FXL_UPDATE_R2_LATEST'
$updateGitHubLatest = Resolve-ConfigValue -Key 'FXL_UPDATE_GITHUB_LATEST'
$updateGitCodeLatest = Resolve-ConfigValue -Key 'FXL_UPDATE_GITCODE_LATEST'
$updateGitCodeApiLatest = Resolve-ConfigValue -Key 'FXL_UPDATE_GITCODE_API_LATEST'
$updateNoticesUrl = Resolve-ConfigValue -Key 'FXL_UPDATE_NOTICES_URL'
$updateNoticesGitCodeUrl = Resolve-ConfigValue -Key 'FXL_UPDATE_NOTICES_GITCODE_URL'
$updateNoticesFallbacks = Resolve-ConfigValue -Key 'FXL_UPDATE_NOTICES_FALLBACK_URLS'
$updateConfigUrl = Resolve-ConfigValue -Key 'FXL_UPDATE_CONFIG_URL'
$updateConfigGitCodeUrl = Resolve-ConfigValue -Key 'FXL_UPDATE_CONFIG_GITCODE_URL'
$updateConfigFallbacks = Resolve-ConfigValue -Key 'FXL_UPDATE_CONFIG_FALLBACK_URLS'
$telemetryUrl = Resolve-ConfigValue -Key 'FXL_TELEMETRY_URL'
$updatePublicKeys = Resolve-ConfigValue -Key 'FXL_UPDATE_PUBLIC_KEYS'
$updateAuthenticode = Resolve-ConfigValue -Key 'FXL_UPDATE_AUTHENTICODE_THUMBPRINT'
$tlsPinnedCas = Resolve-ConfigValue -Key 'FXL_TLS_PINNED_CA_THUMBPRINTS'
$telemetryInterval = Resolve-ConfigValue -Key 'FXL_TELEMETRY_INTERVAL_SECONDS' -Default '15'
$telemetryIntervalValue = 15
if (-not [int]::TryParse($telemetryInterval, [ref]$telemetryIntervalValue) -or $telemetryIntervalValue -lt 5) {
    $telemetryIntervalValue = 15
}

$commitHash = Resolve-ConfigValue -Key 'FXL_COMMIT_HASH'
if ([string]::IsNullOrWhiteSpace($commitHash)) {
    $commitHash = Resolve-ConfigValue -Key 'GITHUB_SHA'
}
if ([string]::IsNullOrWhiteSpace($commitHash)) {
    $commitHash = Get-GitCommitHash -Root $RepoRoot
}

$generated = @"
' <auto-generated>
' Generated by build\GenerateSecrets.ps1 at build time. Do not edit or commit.
' Configure the actual values in the repository-root .env; see .env.example.
' </auto-generated>

Friend Module ModBuildSecrets

    ' FXL_VERSION_BRANCH: branch marker
    Public Const VersionBranchMain As String = $(ConvertTo-VbStringLiteral $versionBranch)
    ' FXL_REG_FOLDER: per-branch registry folder, isolated to avoid data conflicts
    Public Const RegFolder As String = $(ConvertTo-VbStringLiteral $regFolder)
    ' FXL_MS_CLIENT_ID: OAuth client ID used for Microsoft login
    Public Const OAuthClientId As String = $(ConvertTo-VbStringLiteral $msClientId)
    ' FXL_CURSEFORGE_API_KEY: CurseForge API key
    Public Const CurseForgeAPIKey As String = $(ConvertTo-VbStringLiteral $curseForgeApiKey)
    ' FXL_CLS_BASE_URL: anonymous telemetry endpoint; empty disables telemetry
    Public Const ClsBaseUrl As String = $(ConvertTo-VbStringLiteral $clsBaseUrl)
    ' FXL_COMMIT_HASH: commit hash of this build
    Public Const CommitHash As String = $(ConvertTo-VbStringLiteral $commitHash)

    ' ---- Update manifest / telemetry (all values come from .env) ----
    ' FXL_UPDATE_CHANNEL: default update channel (official / snapshot / open-source)
    Public Const UpdateChannel As String = $(ConvertTo-VbStringLiteral $updateChannel)
    ' FXL_UPDATE_PLATFORM: manifest platform key
    Public Const UpdatePlatform As String = $(ConvertTo-VbStringLiteral $updatePlatform)
    ' FXL_UPDATE_R2_LATEST: Cloudflare R2 manifest URL template, may contain {channel} / {platform}
    Public Const UpdateR2Latest As String = $(ConvertTo-VbStringLiteral $updateR2Latest)
    ' FXL_UPDATE_GITHUB_LATEST: GitHub latest.json URL
    Public Const UpdateGitHubLatest As String = $(ConvertTo-VbStringLiteral $updateGitHubLatest)
    ' FXL_UPDATE_GITCODE_LATEST: GitCode latest.json URL
    Public Const UpdateGitCodeLatest As String = $(ConvertTo-VbStringLiteral $updateGitCodeLatest)
    ' FXL_UPDATE_GITCODE_API_LATEST: GitCode release API URL; resolves the latest.json asset dynamically
    Public Const UpdateGitCodeApiLatest As String = $(ConvertTo-VbStringLiteral $updateGitCodeApiLatest)
    ' FXL_UPDATE_NOTICES_URL: signed notices JSON URL (GitHub)
    Public Const UpdateNoticesUrl As String = $(ConvertTo-VbStringLiteral $updateNoticesUrl)
    ' FXL_UPDATE_NOTICES_GITCODE_URL: signed notices JSON URL (GitCode)
    Public Const UpdateNoticesGitCodeUrl As String = $(ConvertTo-VbStringLiteral $updateNoticesGitCodeUrl)
    ' FXL_UPDATE_NOTICES_FALLBACK_URLS: additional notices URLs separated by commas
    Public Const UpdateNoticesFallbacks As String = $(ConvertTo-VbStringLiteral $updateNoticesFallbacks)
    ' FXL_UPDATE_CONFIG_URL: signed dynamic config JSON URL (GitHub)
    Public Const UpdateConfigUrl As String = $(ConvertTo-VbStringLiteral $updateConfigUrl)
    ' FXL_UPDATE_CONFIG_GITCODE_URL: signed dynamic config JSON URL (GitCode)
    Public Const UpdateConfigGitCodeUrl As String = $(ConvertTo-VbStringLiteral $updateConfigGitCodeUrl)
    ' FXL_UPDATE_CONFIG_FALLBACK_URLS: additional config URLs separated by commas
    Public Const UpdateConfigFallbacks As String = $(ConvertTo-VbStringLiteral $updateConfigFallbacks)
    ' FXL_TELEMETRY_URL: telemetry batch endpoint; empty disables telemetry
    Public Const TelemetryUrl As String = $(ConvertTo-VbStringLiteral $telemetryUrl)
    ' FXL_UPDATE_PUBLIC_KEYS: keyId=base64(RSA public key XML), separated by semicolons
    Public Const UpdatePublicKeys As String = $(ConvertTo-VbStringLiteral $updatePublicKeys)
    ' FXL_UPDATE_AUTHENTICODE_THUMBPRINT: expected Authenticode certificate thumbprint; empty skips the check
    Public Const UpdateAuthenticodeThumbprint As String = $(ConvertTo-VbStringLiteral $updateAuthenticode)
    ' FXL_TLS_PINNED_CA_THUMBPRINTS: expected CA thumbprints for soft pinning, separated by semicolons
    Public Const TlsPinnedCaThumbprints As String = $(ConvertTo-VbStringLiteral $tlsPinnedCas)
    ' FXL_TELEMETRY_INTERVAL_SECONDS: telemetry batch flush interval
    Public Const TelemetryIntervalSeconds As Integer = $telemetryIntervalValue

End Module
"@

$outputDirectory = Split-Path -Parent $OutputFile
if (-not (Test-Path -LiteralPath $outputDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

# Avoid touching the file when nothing changed, so incremental builds stay incremental.
$encoding = New-Object System.Text.UTF8Encoding($true)
$existing = ''
if (Test-Path -LiteralPath $OutputFile -PathType Leaf) {
    $existing = [System.IO.File]::ReadAllText($OutputFile, $encoding)
}
if ($existing -ne $generated) {
    [System.IO.File]::WriteAllText($OutputFile, $generated, $encoding)
    Write-Host "[GenerateSecrets] Wrote $OutputFile"
}

$configured = {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { 'not set' } else { 'configured' }
}
Write-Host ("[GenerateSecrets] Branch: {0}; MS client ID: {1}; CurseForge key: {2}; telemetry: {3}; update: {4}" -f `
    $versionBranch, (& $configured $msClientId), (& $configured $curseForgeApiKey), (& $configured $telemetryUrl), (& $configured $updatePublicKeys))

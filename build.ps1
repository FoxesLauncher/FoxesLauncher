param(
    [ValidateSet('Debug', 'Release', 'Snapshot')]
    [string]$Configuration = 'Release',
    [string]$Platform = 'Any CPU',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$solution = Join-Path $root 'Plain Craft Launcher 2.sln'

function Find-MSBuild {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vswhere) {
        $installPath = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
        if ($installPath) {
            $candidate = Join-Path $installPath 'MSBuild\Current\Bin\MSBuild.exe'
            if (Test-Path $candidate) { return $candidate }
        }
    }
    $cmd = Get-Command msbuild -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw 'MSBuild not found. Install Visual Studio with the .NET desktop build tools, or run this script from a Developer Command Prompt.'
}

Write-Host "==> Root:          $root"
Write-Host "==> Configuration: $Configuration"
Write-Host "==> Platform:      $Platform"

# Initialize the MeloongCore submodule when the checkout is empty.
$coreProject = Join-Path $root 'MeloongCore\Shared\MeloongCore.csproj'
if (-not (Test-Path $coreProject)) {
    Write-Host '==> Initializing submodules...'
    & git -C $root submodule update --init --recursive
    if ($LASTEXITCODE -ne 0) { throw "git submodule update failed with exit code $LASTEXITCODE" }
}

$msbuild = Find-MSBuild
Write-Host "==> MSBuild:       $msbuild"

$msbuildArgs = @(
    $solution
    "/p:Configuration=$Configuration"
    "/p:Platform=$Platform"
    '/m'
    '/nologo'
    '/v:m'
)
if ($NoRestore) { $msbuildArgs += '/p:RestorePackages=false' } else { $msbuildArgs = @('/restore') + $msbuildArgs }

Write-Host '==> Building...'
& $msbuild @msbuildArgs
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE" }

$output = Join-Path $root 'Plain Craft Launcher 2\bin\Plain Craft Launcher 2.exe'
Write-Host ''
Write-Host "Build succeeded: $output"
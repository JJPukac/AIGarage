param(
    [switch]$BuildAndroid,
    [switch]$BuildWindows,
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-ProjectDotnet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE." }
}

Push-Location -LiteralPath $repositoryRoot
try {
    foreach ($testProject in @('AIGarage.Core.Tests', 'AIGarage.Mobile.Tests')) {
        $dotnetArguments = @('run', '--project', $testProject, '--configuration', 'Release')
        if ($NoRestore) { $dotnetArguments += '--no-restore' }
        Invoke-ProjectDotnet -Arguments $dotnetArguments
    }

    $frameworks = @()
    if ($BuildAndroid) { $frameworks += 'net10.0-android' }
    if ($BuildWindows) { $frameworks += 'net10.0-windows10.0.19041.0' }
    foreach ($framework in $frameworks) {
        $dotnetArguments = @('build', 'AIGarage.Mobile', '--framework', $framework, '--verbosity', 'minimal')
        if ($NoRestore) { $dotnetArguments += '--no-restore' }
        Invoke-ProjectDotnet -Arguments $dotnetArguments
    }
}
finally { Pop-Location }

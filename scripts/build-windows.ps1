param(
    [ValidateSet("Release", "Release_Portable")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopSolution = Join-Path $repoRoot "desktop/DLSS Swapper.sln"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw ".NET SDK was not found. Install the .NET 10 SDK before building XTR Unlocker."
}

& dotnet build $desktopSolution --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "The XTR desktop build failed."
}

$framework = "net10.0-windows10.0.26100.0"
$desktopOutput = Join-Path $repoRoot "desktop/src/bin/x64/$Configuration/$framework/win-x64"
$bundledRuntime = Join-Path $desktopOutput "Runtime/OptiScaler"
if (-not (Test-Path (Join-Path $bundledRuntime "OptiScaler.dll")) -or
    -not (Test-Path (Join-Path $bundledRuntime "OptiScaler.ini"))) {
    throw "The bundled OptiScaler runtime was not copied beside the XTR application."
}

Write-Host "XTR Unlocker and its bundled OptiScaler runtime were built to $desktopOutput"

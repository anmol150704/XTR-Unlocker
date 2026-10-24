param(
    [ValidateSet("Release", "Release_Portable")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$runtimeRoot = Join-Path $repoRoot "runtime/OptiScaler"
$runtimeSolution = Join-Path $runtimeRoot "OptiScaler.sln"
$runtimePatch = Join-Path $repoRoot "runtime/patches/xtr-overlay-branding.patch"
$desktopSolution = Join-Path $repoRoot "desktop/DLSS Swapper.sln"

Push-Location $runtimeRoot
try {
    & git apply --reverse --check $runtimePatch *> $null
    if ($LASTEXITCODE -ne 0) {
        & git apply $runtimePatch
        if ($LASTEXITCODE -ne 0) {
            throw "Could not apply the XTR OptiScaler overlay patch."
        }
    }
}
finally {
    Pop-Location
}

& msbuild $runtimeSolution /m /p:Configuration=Release /p:Platform=x64 /verbosity:minimal
if ($LASTEXITCODE -ne 0) {
    throw "The OptiScaler native runtime build failed."
}

$runtimePackage = Join-Path $runtimeRoot "x64/Release/a"
if (-not (Test-Path (Join-Path $runtimePackage "OptiScaler.dll")) -or
    -not (Test-Path (Join-Path $runtimePackage "OptiScaler.ini"))) {
    throw "The OptiScaler build completed without producing its distributable package."
}

& dotnet build $desktopSolution --configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "The XTR desktop build failed."
}

$framework = "net10.0-windows10.0.26100.0"
$desktopOutput = Join-Path $repoRoot "desktop/src/bin/x64/$Configuration/$framework/win-x64"
$bundledRuntime = Join-Path $desktopOutput "Runtime/OptiScaler"
New-Item -ItemType Directory -Path $bundledRuntime -Force | Out-Null
Copy-Item -Path (Join-Path $runtimePackage "*") -Destination $bundledRuntime -Recurse -Force

if (-not (Test-Path (Join-Path $bundledRuntime "OptiScaler.dll")) -or
    -not (Test-Path (Join-Path $bundledRuntime "OptiScaler.ini"))) {
    throw "The OptiScaler package was not copied beside the XTR application."
}

Write-Host "XTR Unlocker and its bundled OptiScaler runtime were built to $desktopOutput"

# Builds the native C++20 XTR Emerald HUD ASI Companion (xtr_hud.asi)
# Targets Windows x64 with zero external dependencies.

[CmdletBinding()]
param(
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"

$srcDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = [System.IO.Path]::GetFullPath((Join-Path $srcDir "..\..\desktop\src\Runtime\OptiScaler\plugins"))
} else {
    $OutputDir = [System.IO.Path]::GetFullPath($OutputDir)
}

$ClangPath = "C:\Users\Asus\AppData\Local\Microsoft\WinGet\Packages\MartinStorsjo.LLVM-MinGW.UCRT_Microsoft.Winget.Source_8wekyb3d8bbwe\llvm-mingw-20261117-ucrt-x86_64\bin\clang++.exe"
if (-not (Test-Path $ClangPath)) {
    $found = Get-Command "clang++" -ErrorAction SilentlyContinue
    if ($found) {
        $ClangPath = $found.Source
    } else {
        throw "clang++ compiler not found in WinGet LLVM-MinGW or PATH."
    }
}

Write-Host "Compiling native in-game HUD with $ClangPath..." -ForegroundColor Cyan

$srcDir = $PSScriptRoot
$asiOutput = Join-Path $srcDir "xtr_hud.asi"

& $ClangPath -std=c++20 -O2 -Wall -Wextra -Werror -shared -static -s `
    -DWIN32_LEAN_AND_MEAN -DNOMINMAX `
    -o $asiOutput `
    (Join-Path $srcDir "dllmain.cpp") `
    (Join-Path $srcDir "hud_renderer.cpp") `
    -lkernel32 -luser32

if ($LASTEXITCODE -ne 0) {
    throw "clang++ compilation failed with exit code $LASTEXITCODE."
}

Write-Host "Successfully compiled $asiOutput" -ForegroundColor Green

if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}

$destAsi = Join-Path $OutputDir "xtr_hud.asi"
Copy-Item -Path $asiOutput -Destination $destAsi -Force
Write-Host "Deployed native HUD companion to $destAsi" -ForegroundColor Green

param(
    [ValidateSet("Release", "Release_Portable")]
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SelfContained,
    [switch]$PackageZip,
    [switch]$BuildInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$desktopSolution = Join-Path $repoRoot "desktop/DLSS Swapper.sln"
$desktopProject = Join-Path $repoRoot "desktop/src/DLSS Swapper.csproj"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw ".NET SDK was not found. Install the .NET 10 SDK before building XTR Unlocker."
}

if ($SelfContained) {
    Write-Host "Publishing self-contained standalone XTR Unlocker ($Configuration)..."
    $distDir = Join-Path $repoRoot "dist/XTR-Unlocker-$Configuration-win-x64"
    & dotnet publish $desktopProject --configuration $Configuration -p:Platform=x64 -r win-x64 --self-contained true -o $distDir
    if ($LASTEXITCODE -ne 0) {
        throw "The self-contained publish failed."
    }

    $desktopOutput = $distDir
} else {
    Write-Host "Building XTR Unlocker ($Configuration x64)..."
    & dotnet build $desktopSolution --configuration $Configuration -p:Platform=x64
    if ($LASTEXITCODE -ne 0) {
        throw "The XTR desktop build failed."
    }

    $framework = "net10.0-windows10.0.26100.0"
    $desktopOutput = Join-Path $repoRoot "desktop/src/bin/x64/$Configuration/$framework/win-x64"
}

if (-not $SkipTests) {
    $testsProject = Join-Path $repoRoot "desktop/tests/XTR.Tests/XTR.Tests.csproj"
    if (Test-Path $testsProject) {
        Write-Host "Running automated test suite..."
        & dotnet test $testsProject --configuration $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "The XTR automated test suite failed."
        }
    }
}

$bundledRuntime = Join-Path $desktopOutput "Runtime/OptiScaler"
if (-not (Test-Path (Join-Path $bundledRuntime "OptiScaler.dll")) -or
    -not (Test-Path (Join-Path $bundledRuntime "OptiScaler.ini"))) {
    throw "The bundled OptiScaler runtime was not copied beside the XTR application."
}

Write-Host "XTR Unlocker and its bundled OptiScaler runtime were verified in $desktopOutput"

$distRoot = Join-Path $repoRoot "dist"
if ($PackageZip) {
    if (-not (Test-Path $distRoot)) {
        New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
    }
    $zipPath = Join-Path $distRoot "XTR-Unlocker-$Configuration-win-x64.zip"
    Write-Host "Compressing release package to $zipPath..."
    Compress-Archive -Path "$desktopOutput/*" -DestinationPath $zipPath -Force
    Write-Host "Release package archive created at $zipPath"
}

if ($BuildInstaller -or ($PackageZip -and $Configuration -eq "Release")) {
    $sevenZipExe = "C:\Program Files\7-Zip\7z.exe"
    $sevenZipSfx = "C:\Program Files\7-Zip\7z.sfx"
    if ((Test-Path $sevenZipExe) -and (Test-Path $sevenZipSfx)) {
        if (-not (Test-Path $distRoot)) {
            New-Item -ItemType Directory -Path $distRoot -Force | Out-Null
        }
        Write-Host "Building single-file setup installer XTR-Unlocker-Setup.exe..."
        $payload7z = Join-Path $distRoot "app_payload.7z"
        if (Test-Path $payload7z) { Remove-Item -Path $payload7z -Force }
        & $sevenZipExe a -t7z $payload7z "$desktopOutput\*" -mx=9 | Out-Null

        $sfxConfig = Join-Path $distRoot "sfx_config.txt"
        $sfxConfigContent = @'
;!@Install@!UTF-8!
Title="XTR Unlocker Setup"
BeginPrompt="Install XTR Unlocker (DLSS & Frame Generation Unlocker with In-Game Apple Glass HUD)?"
RunProgram="XTR Unlocker.exe"
;!@InstallEnd@!
'@
        [System.IO.File]::WriteAllText($sfxConfig, $sfxConfigContent, [System.Text.Encoding]::UTF8)

        $installerExe = Join-Path $distRoot "XTR-Unlocker-Setup.exe"
        cmd /c "copy /b `"$sevenZipSfx`" + `"$sfxConfig`" + `"$payload7z`" `"$installerExe`"" | Out-Null

        if (Test-Path $payload7z) { Remove-Item -Path $payload7z -Force }
        if (Test-Path $sfxConfig) { Remove-Item -Path $sfxConfig -Force }

        if (Test-Path $installerExe) {
            $sizeMb = [math]::Round((Get-Item $installerExe).Length / 1MB, 2)
            Write-Host "Clean setup installer created successfully: $installerExe ($sizeMb MB)"
        }
    } else {
        Write-Warning "7-Zip was not found at $sevenZipExe; skipping setup installer generation."
    }
}

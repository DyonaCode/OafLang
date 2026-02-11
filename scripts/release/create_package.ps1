param(
    [string]$Version = "0.1.0",
    [string]$RuntimeId = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Resolve-Path (Join-Path $ScriptDir "..\..")
$DistDir = Join-Path $RootDir "dist"
$PackageName = "oaf-$Version"
$StagingDir = Join-Path $DistDir $PackageName

Write-Host "Creating release package '$PackageName'..."
if (Test-Path $StagingDir) {
    Remove-Item -Recurse -Force $StagingDir
}

New-Item -ItemType Directory -Force -Path (Join-Path $StagingDir "bin") | Out-Null

if ($RuntimeId -eq "") {
    $ridLine = dotnet --info | Select-String -Pattern "RID:\s*(.+)" | Select-Object -First 1
    if ($null -ne $ridLine -and $ridLine.Matches.Count -gt 0) {
        $RuntimeId = $ridLine.Matches[0].Groups[1].Value.Trim()
    }
}

if ($RuntimeId -eq "") {
    throw "Unable to determine Runtime Identifier (RID). Pass one explicitly, e.g. win-x64, osx-arm64, linux-x64."
}

$publishArgs = @(
    (Join-Path $RootDir "Oaf.csproj"),
    "--configuration", "Release",
    "--output", (Join-Path $StagingDir "bin"),
    "--runtime", $RuntimeId,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:DebugSymbols=false",
    "-p:DebugType=None"
)

dotnet publish @publishArgs

$copyTargets = @(
    "docs",
    "examples",
    "SpecOverview.md",
    "SpecSyntax.md",
    "SpecRuntime.md",
    "SpecFileStructure.md",
    "SpecRoadmap.md"
)

foreach ($target in $copyTargets) {
    $sourcePath = Join-Path $RootDir $target
    if (Test-Path $sourcePath) {
        Copy-Item $sourcePath -Destination $StagingDir -Recurse -Force
    }
}

$installShSource = Join-Path $RootDir "scripts\release\install.sh"
if (Test-Path $installShSource) {
    Copy-Item $installShSource -Destination (Join-Path $StagingDir "install.sh") -Force
}

$installPsSource = Join-Path $RootDir "scripts\release\install.ps1"
if (Test-Path $installPsSource) {
    Copy-Item $installPsSource -Destination (Join-Path $StagingDir "install.ps1") -Force
}

$readmePath = Join-Path $StagingDir "README.txt"
@"
Oaf Release Package $Version
Target Runtime: $RuntimeId

Contents:
- bin/: Published CLI and runtime assets
- docs/: Guides and references
- examples/: Sample programs and tutorials
- Spec*.md: Language specification documents

Quick start:
1. Execute '.\bin\oaf.exe --self-test' to validate installation.
2. Run a file: '.\bin\oaf.exe run .\examples\basics\01_hello_and_return.oaf'
3. Build bytecode artifact: '.\bin\oaf.exe build .\examples\basics\01_hello_and_return.oaf'
4. Publish executable: '.\bin\oaf.exe publish .\examples\applications\01_sum_accumulator.oaf'

Install globally:
- macOS/Linux: './install.sh'
- Windows (PowerShell): '.\install.ps1'

Version management:
- Active version: '.\bin\oaf.exe --version' (or 'oaf --version' after install)
- List installed versions: 'oaf version'
- Switch version: 'oaf version <num>'
"@ | Set-Content -Path $readmePath -Encoding UTF8

New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
$zipPath = Join-Path $DistDir "$PackageName.zip"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Compress-Archive -Path $StagingDir -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Package created: $zipPath"

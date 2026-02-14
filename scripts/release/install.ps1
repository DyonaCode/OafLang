param(
    [string]$InstallDir = "$env:LOCALAPPDATA\Oaf\bin",
    [string]$OafHome = "$env:USERPROFILE\.oaf",
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceBinDir = Join-Path $ScriptDir "bin"
$SourceExe = Join-Path $ScriptDir "bin\oaf.exe"
$CurrentFile = Join-Path $OafHome "current.txt"

if (-not (Test-Path $SourceExe)) {
    throw "Could not find '$SourceExe'. Run this script from the extracted Oaf package root."
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    try {
        $candidate = ((& $SourceExe --version) | Select-Object -First 1).Trim()
        if ($candidate -match '^[vV]?([0-9]+(\.[0-9]+){0,3}([\-+][0-9A-Za-z\.]+)?)$') {
            $Version = $Matches[1]
        }
        else {
            $Version = ""
        }
    }
    catch {
        $Version = ""
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    $packageName = Split-Path -Leaf $ScriptDir
    if ($packageName.StartsWith("oaf-", [System.StringComparison]::OrdinalIgnoreCase)) {
        $Version = $packageName.Substring(8)
    }
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Unable to determine Oaf version automatically. Run install.ps1 with -Version <value>."
}

if ($Version.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    $Version = $Version.Substring(1)
}

if (-not ($Version -match '^[0-9]+(\.[0-9]+){0,3}([\-+][0-9A-Za-z\.]+)?$')) {
    throw "Resolved version '$Version' is not valid. Run install.ps1 with -Version <value>."
}

$VersionBinDir = Join-Path $OafHome "versions\$Version\bin"
$TargetExe = Join-Path $VersionBinDir "oaf.exe"

New-Item -ItemType Directory -Force -Path $VersionBinDir | Out-Null
Copy-Item -Path (Join-Path $SourceBinDir "*") -Destination $VersionBinDir -Recurse -Force
New-Item -ItemType Directory -Force -Path $OafHome | Out-Null
Set-Content -Path $CurrentFile -Value $Version -Encoding UTF8

$ShimCmd = Join-Path $InstallDir "oaf.cmd"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
@'
@echo off
setlocal
set "OAF_HOME_DEFAULT=__OAF_HOME_DEFAULT__"
if defined OAF_HOME (
  set "OAF_HOME_PATH=%OAF_HOME%"
) else (
  set "OAF_HOME_PATH=%OAF_HOME_DEFAULT%"
)
if "%OAF_HOME_PATH%"=="" (
  set "OAF_HOME_PATH=%USERPROFILE%\.oaf"
)
set "CURRENT_FILE=%OAF_HOME_PATH%\current.txt"
if not exist "%CURRENT_FILE%" (
  echo No active Oaf version configured. Re-run install.ps1.
  exit /b 1
)
set /p OAF_VERSION=<"%CURRENT_FILE%"
set "TARGET=%OAF_HOME_PATH%\versions\%OAF_VERSION%\bin\oaf.exe"
if not exist "%TARGET%" (
  echo Configured Oaf version '%OAF_VERSION%' is missing: %TARGET%
  exit /b 1
)
"%TARGET%" %*
'@.Replace("__OAF_HOME_DEFAULT__", $OafHome) | Set-Content -Path $ShimCmd -Encoding ASCII

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ([string]::IsNullOrWhiteSpace($userPath)) {
    [Environment]::SetEnvironmentVariable("Path", $InstallDir, "User")
}
else {
    $segments = $userPath.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries)
    $alreadyPresent = $false
    foreach ($segment in $segments) {
        if ([string]::Equals($segment.TrimEnd('\'), $InstallDir.TrimEnd('\'), [System.StringComparison]::OrdinalIgnoreCase)) {
            $alreadyPresent = $true
            break
        }
    }

    if (-not $alreadyPresent) {
        [Environment]::SetEnvironmentVariable("Path", "$userPath;$InstallDir", "User")
    }
}

Write-Host "Installed version $Version at: $TargetExe"
Write-Host "Active version set to: $Version"
Write-Host "Shim installed at: $ShimCmd"
Write-Host "Open a new terminal and run:"
Write-Host "  oaf --version"
Write-Host "  oaf version"

# Ensure callers do not observe a stale non-zero native exit code from prior commands.
$global:LASTEXITCODE = 0

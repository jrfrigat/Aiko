# Aiko installer for a local source checkout (Windows PowerShell).
# Publishes Aiko framework-dependent and installs the `aiko` command into the user PATH.
#
# End users need no clone: GitHub Actions publishes the self-contained win-x64 release that the
# release installer downloads, so the documented install is
#
#     irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex
#
# This script stays as the contributor path: it builds what is in the working tree.

# This is an interactive installer: its progress lines belong on the console, not on the pipeline.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost', '',
    Justification = 'Interactive installer: progress belongs on the console, not the pipeline.')]
[CmdletBinding()]
param(
    [switch]$NoAgents,
    # Start the daemon at sign-in. Not asked for by default: the installer asks, and the question defaults
    # to no, because writing something that runs at every logon is the person's decision and not ours.
    [switch]$Autostart,
    [switch]$NoAutostart
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repo = $PSScriptRoot
$bin = Join-Path $env:LOCALAPPDATA "Aiko\bin"

Write-Host "Publishing Aiko (framework-dependent, Release)..."
dotnet publish (Join-Path $repo "src\Aiko.Server") -c Release -o $bin -p:PublishAot=false --nologo | Out-Host
dotnet publish (Join-Path $repo "src\Aiko.Cli") -c Release -o $bin -p:PublishAot=false --nologo | Out-Host
dotnet publish (Join-Path $repo "src\Aiko.StdioProxy") -c Release -o $bin -p:PublishAot=false --nologo | Out-Host

$userPath = [Environment]::GetEnvironmentVariable("Path", "User")
if ($userPath -notlike "*$bin*") {
    [Environment]::SetEnvironmentVariable("Path", "$bin;$userPath", "User")
    Write-Host "Added $bin to the user PATH (open a new terminal to use 'aiko')."
}

if (-not $NoAgents) {
    Write-Host "Tip: connect your agents globally with: aiko agent install --scope user"
}

# Starting the daemon at sign-in is the person's choice: the question defaults to no, and the entry is
# written only after a yes here or an explicit -Autostart. The entry itself is written by the CLI, so the
# installer and `aiko uninstall` agree on one file.
$startAtSignIn = $Autostart
if (-not $NoAutostart -and -not $Autostart) {
    $answer = ""
    try {
        $answer = Read-Host "Start the Aiko daemon at sign-in? [y/N]"
    }
    catch {
        Write-Host "No console to ask on; the daemon will not start at sign-in." -ForegroundColor DarkGray
    }

    $startAtSignIn = $answer -match "^(y|yes)$"
}

if ($startAtSignIn) {
    & (Join-Path $bin "aiko.exe") autostart enable
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Autostart could not be set up; run 'aiko autostart enable' after fixing it." -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "Aiko installed."
Write-Host "  Start the daemon:  aiko serve"
Write-Host "  Open the UI:       aiko ui"
Write-Host "  Remove it later:   aiko uninstall"

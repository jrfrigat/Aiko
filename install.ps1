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
    [switch]$NoAgents
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

Write-Host ""
Write-Host "Aiko installed."
Write-Host "  Start the daemon:  aiko serve"
Write-Host "  Open the UI:       aiko ui"

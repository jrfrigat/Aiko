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
# A failed publish must not be invisible. $ErrorActionPreference = "Stop" does not cover a native
# command, so the script used to carry on to "Aiko installed." with a half-written directory when a
# publish failed - which is how a build that never happened was announced as an installation. Each
# exit code is checked, and the first failure ends the script. Asking a child process what it
# returned is what scripts/install.ps1 already does after 'autostart enable' and 'agent install'.
foreach ($project in "Aiko.Server", "Aiko.Cli", "Aiko.StdioProxy") {
    dotnet publish (Join-Path $repo "src\$project") -c Release -o $bin -p:PublishAot=false --nologo | Out-Host
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Publishing $project failed (dotnet publish exited with $LASTEXITCODE); nothing was installed." -ForegroundColor Red
        # Almost always an older build of this tree still running: it holds the files under
        # src\<project>\bin, so MSBuild cannot replace them. Name the process rather than killing it -
        # the one running may be the daemon the person is working with, or somebody's own tooling.
        Write-Host "  Something is most likely holding src\$project\bin - find it with:" -ForegroundColor DarkGray
        Write-Host "    Get-CimInstance Win32_Process -Filter `"Name='dotnet.exe'`" | Where-Object { `$_.CommandLine -like '*Aiko.Server.dll*' }" -ForegroundColor DarkGray
        Write-Host "  Stop it ('aiko serve stop' for the daemon) and run the installer again." -ForegroundColor DarkGray
        exit 1
    }
}

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

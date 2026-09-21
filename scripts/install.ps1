<#
.SYNOPSIS
    Installs the Aiko daemon, CLI and stdio proxy (Windows, x64).

.DESCRIPTION
    Downloads the self-contained release build from GitHub, unpacks it into the user's programs
    directory and puts that directory on the user PATH. Nothing is installed machine-wide and no
    administrator rights are needed; .NET does not have to be installed either.

    Run it directly:

        irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex

    To pass options, fetch the script into a scriptblock first:

        & ([scriptblock]::Create((irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1))) -Version v0.3.1

.PARAMETER Version
    The release tag to install, for example "v0.1.0". Defaults to the latest release.

.PARAMETER InstallDir
    Where to unpack. Defaults to %LOCALAPPDATA%\Aiko\bin.

.PARAMETER NoPathUpdate
    Skip adding the install directory to the user PATH.

.PARAMETER Agents
    Connect these agents globally, for example "claude-code,codex". Defaults to asking
    interactively; combine with -NoAgentSetup to skip the question entirely.

.PARAMETER NoAgentSetup
    Do not ask about agent integration. Agents can be connected later with
    "aiko agent install --scope user".

.PARAMETER Autostart
    Start the daemon at sign-in, without asking. The question the installer would ask defaults to no, so
    nothing is written to the machine's startup folder unless it is asked for here or answered yes.

.PARAMETER NoAutostart
    Do not ask about starting the daemon at sign-in. It can be set up later with "aiko autostart enable".
#>
# Write-Host is the right call here and not a lapse: this is an interactive installer whose output is
# meant for the person running it. Write-Output would put those lines on the pipeline, and the
# documented way to run this is `irm ... | iex`, where a polluted pipeline is the caller's problem.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute(
    'PSAvoidUsingWriteHost', '',
    Justification = 'Interactive installer: progress belongs on the console, not the pipeline.')]
[CmdletBinding()]
param(
    [string] $Version = 'latest',
    [string] $InstallDir = (Join-Path $env:LOCALAPPDATA 'Aiko\bin'),
    [string] $Agents,
    [switch] $NoPathUpdate,
    [switch] $NoAgentSetup,
    [switch] $Autostart,
    [switch] $NoAutostart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = 'jrfrigat/Aiko'
$command = 'aiko'

# Agent identifiers accepted by "aiko agent install --scope user".
$agentChoices = [ordered]@{
    '1' = 'claude-code'
    '2' = 'codex'
    '3' = 'cursor'
    '4' = 'zcode'
    '5' = 'cline'
}

function Write-Step([string] $message) { Write-Host "==> $message" -ForegroundColor Cyan }

function ConvertTo-AgentList([string] $answer) {
    # Accepts "1,3", "all", explicit identifiers or any mix of them.
    $selected = [System.Collections.Generic.List[string]]::new()
    foreach ($token in ($answer -split '[,;\s]+' | Where-Object { $_ })) {
        if ($token -eq 'all') {
            foreach ($id in $agentChoices.Values) {
                if (-not $selected.Contains($id)) { $selected.Add($id) }
            }
            continue
        }

        $id = if ($agentChoices.Contains($token)) { $agentChoices[$token] } else { $token }
        if ($id -notin $agentChoices.Values) {
            throw "Unknown agent '$token'. Known agents: $($agentChoices.Values -join ', ')."
        }
        if (-not $selected.Contains($id)) { $selected.Add($id) }
    }

    return ($selected -join ',')
}

function Get-LatestReleaseTag([string] $repository) {
    # The unauthenticated GitHub API is limited to 60 requests per public IP. That limit is often
    # shared by an office, VPN or ISP and can make the installer fail even though releases are
    # available. The regular releases/latest endpoint is not subject to that API limit and redirects
    # to /releases/tag/<tag>, so only inspect its Location header.
    $latestUrl = "https://github.com/$repository/releases/latest"
    $request = [Net.HttpWebRequest]::Create($latestUrl)
    $request.Method = 'HEAD'
    $request.AllowAutoRedirect = $false
    $request.UserAgent = 'aiko-installer'

    try {
        $response = [Net.HttpWebResponse] $request.GetResponse()
    }
    catch {
        throw "Cannot resolve the latest GitHub release ($latestUrl): $($_.Exception.Message)"
    }

    try {
        $location = $response.Headers['Location']
    }
    finally {
        $response.Dispose()
    }

    if (-not $location -or $location -notmatch '/releases/tag/([^/?#]+)') {
        throw "GitHub did not redirect $latestUrl to a release tag. Location: $location"
    }

    return [Uri]::UnescapeDataString($Matches[1])
}

if ([Environment]::Is64BitOperatingSystem -eq $false) {
    throw "Aiko ships for 64-bit Windows only; this system is 32-bit."
}

# TLS 1.2 for Windows PowerShell 5.1, whose default still excludes it on older builds. PowerShell 7
# negotiates on its own and may not expose the knob at all, so failing to set it is not an error.
try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}
catch {
    Write-Verbose "Could not select TLS 1.2 explicitly; using the platform default. $($_.Exception.Message)"
}

$headers = @{ 'User-Agent' = 'aiko-installer' }
$tag = $Version
if ($Version -eq 'latest') {
    Write-Step "Looking up the latest release of $repo"
    $tag = Get-LatestReleaseTag $repo
}

$releaseVersion = ($tag -replace '^v', '').Split('+')[0]
$assetName = "aiko-$releaseVersion-win-x64.zip"
$escapedTag = [Uri]::EscapeDataString($tag)
$downloadUrl = "https://github.com/$repo/releases/download/$escapedTag/$assetName"

# The base project template ships with the release, and the installer is what puts it where the daemon
# looks for it. An existing file is left alone: it is the installation's own copy by then, and an upgrade
# must not overwrite defaults a person edited.
function Install-BaseTemplate([string] $sourceDir, [string] $dataDir) {
    $source = Join-Path $sourceDir 'templates\default\template.json'
    $target = Join-Path $dataDir 'templates\default\template.json'
    if (-not (Test-Path $source) -or (Test-Path $target)) {
        return
    }

    New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
    Copy-Item -Path $source -Destination $target -Force
    Write-Step 'Installed the base project template.'
}

$temp = Join-Path ([IO.Path]::GetTempPath()) ("aiko-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $archive = Join-Path $temp $assetName
    Write-Step "Downloading $assetName"
    try {
        # Basic parsing avoids the Internet Explorer dependency in Windows PowerShell 5.1.
        Invoke-WebRequest -Uri $downloadUrl -OutFile $archive -Headers $headers -UseBasicParsing
    }
    catch {
        throw "Cannot download the win-x64 archive for release $tag ($downloadUrl): $($_.Exception.Message)"
    }

    Write-Step "Unpacking into $InstallDir"
    $staging = Join-Path $temp 'unpacked'
    Expand-Archive -Path $archive -DestinationPath $staging -Force

    # Replace the contents rather than the directory itself: the directory may already be on PATH,
    # and a running shell keeps resolving the path it was given.
    if (Test-Path $InstallDir) {
        Get-ChildItem -Path $InstallDir -Force | Remove-Item -Recurse -Force
    }
    else {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    Copy-Item -Path (Join-Path $staging '*') -Destination $InstallDir -Recurse -Force
}

finally {
    Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
}

$exe = Join-Path $InstallDir "$command.exe"
if (-not (Test-Path $exe)) {
    throw "The archive did not contain $command.exe. Contents: $((Get-ChildItem $InstallDir | ForEach-Object Name) -join ', ')"
}

$daemon = Join-Path $InstallDir 'server\Aiko.Server.exe'
if (-not (Test-Path $daemon)) {
    throw "The archive did not contain server\Aiko.Server.exe. Contents: $((Get-ChildItem $InstallDir -Recurse | ForEach-Object Name) -join ', ')"
}

# The daemon reads project templates from its data directory; seeding the base one here is what makes a
# fresh installation able to create a project without ever having run the daemon.
Install-BaseTemplate -sourceDir $InstallDir -dataDir (Join-Path $env:LOCALAPPDATA 'Aiko')

if (-not $NoPathUpdate) {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $entries = if ($userPath) { $userPath.Split(';', [StringSplitOptions]::RemoveEmptyEntries) } else { @() }
    if ($entries -notcontains $InstallDir) {
        Write-Step "Adding $InstallDir to the user PATH"
        $updated = (@($entries) + $InstallDir) -join ';'
        [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
        # So the current session can run it without reopening the terminal.
        $env:Path = "$env:Path;$InstallDir"
        Write-Host "    Open a new terminal for PATH to apply everywhere." -ForegroundColor DarkGray
    }
}

# Starting the daemon at sign-in is the person's choice: the question defaults to no, and the entry is
# written only after a yes here or an explicit -Autostart. The CLI writes it, so this installer and
# `aiko uninstall` agree on one file.
$startAtSignIn = $Autostart
if (-not $NoAutostart -and -not $Autostart) {
    $answer = ''
    try {
        $answer = Read-Host 'Start the Aiko daemon at sign-in? [y/N]'
    }
    catch {
        Write-Verbose "No interactive console, skipping autostart. $($_.Exception.Message)"
    }

    $startAtSignIn = $answer -match '^(y|yes)$'
}

if ($startAtSignIn) {
    Write-Step 'Setting the daemon to start at sign-in'
    & $exe autostart enable
    if ($LASTEXITCODE -ne 0) {
        Write-Host "    Autostart could not be set up; run 'aiko autostart enable' after fixing it." -ForegroundColor DarkYellow
    }
}

$agentList = $Agents
if (-not $NoAgentSetup -and -not $agentList) {
    Write-Host ""
    Write-Host "Connect Aiko to your agents?" -ForegroundColor Cyan
    Write-Host "It writes the global MCP entry, the /aiko-* skills and the shared memory into each"
    Write-Host "agent's own configuration; nothing else is touched."
    Write-Host "  [1] Claude Code   [2] Codex   [3] Cursor   [4] ZCode   [5] Cline"
    Write-Host "  1,3 = several agents, all = every agent above, Enter = skip" -ForegroundColor DarkGray
    $answer = ''
    try {
        $answer = Read-Host 'Agents to connect (for example 1,3)'
    }
    catch {
        Write-Verbose "No interactive console, skipping agent setup. $($_.Exception.Message)"
    }

    if ($answer) {
        $agentList = ConvertTo-AgentList $answer
    }
}

$agentsConnected = $false
if (-not $NoAgentSetup -and $agentList) {
    Write-Step "Configuring agents: $agentList"
    & $exe agent install --scope user --agent $agentList
    $agentsConnected = $LASTEXITCODE -eq 0
    if (-not $agentsConnected) {
        Write-Host "    Agent setup reported a problem; re-run 'aiko agent install --scope user' after fixing it." -ForegroundColor DarkYellow
    }
}

Write-Host ""
Write-Host "Aiko $tag installed." -ForegroundColor Green
Write-Host "  Start the daemon:  " -NoNewline
Write-Host "aiko serve" -ForegroundColor Yellow
Write-Host "  Open the board:    " -NoNewline
Write-Host "aiko ui" -ForegroundColor Yellow
if (-not $agentsConnected) {
    Write-Host "  Connect an agent:  " -NoNewline
    Write-Host "aiko agent install --scope user" -ForegroundColor Yellow
}

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
    Connect these agents globally, for example "claude-code,codex", instead of the ones found on
    this machine. Combine with -NoAgentSetup to connect nobody.

.PARAMETER NoAgentSetup
    Do not connect the agents found on this machine. Agents can be connected later with
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

function Write-Step([string] $message) { Write-Host "==> $message" -ForegroundColor Cyan }

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

# The directory is the installer's to replace only when it is empty or already holds an installation. Anything
# else - -InstallDir pointed at a folder of other tools, or at the data directory - holds someone else's files,
# and replacing its contents would delete them. Checked before the download, while refusing costs nothing.
function Assert-InstallDirIsAiko([string] $dir) {
    if (-not (Test-Path $dir)) { return }
    if (@(Get-ChildItem -Path $dir -Force).Count -eq 0) { return }
    if ((Test-Path (Join-Path $dir "$command.exe")) -or (Test-Path (Join-Path $dir 'install.json'))) { return }
    throw "$dir is not empty and holds no Aiko installation, so installing there would delete what it holds. " +
        'Choose an empty directory or one Aiko was installed into.'
}

# A running daemon and the agents' aiko-stdio proxies hold their executables open, and a replacement under them
# fails half-way. The daemon is asked to stop; whatever still runs from the directory is stopped with it - an
# agent starts its proxy again on its next call. It supports -WhatIf like any function that stops something; at
# the default confirm impact an install is not asked, so the daemon is still stopped without a prompt.
function Stop-InstalledAiko {
    [CmdletBinding(SupportsShouldProcess)]
    param([string] $dir)

    if (-not $PSCmdlet.ShouldProcess($dir, 'Stop the running Aiko daemon and its proxies')) { return }

    $cli = Join-Path $dir "$command.exe"
    if (Test-Path $cli) {
        try {
            & $cli serve stop | Out-Null
        }
        catch {
            Write-Verbose "The installed daemon did not stop on request: $($_.Exception.Message)"
        }
    }

    $prefix = (Join-Path $dir '').TrimEnd('\') + '\'
    Get-Process -ErrorAction SilentlyContinue |
        Where-Object { $_.Path -and $_.Path.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) } |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

Assert-InstallDirIsAiko $InstallDir

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

    # The archive is checked before anything installed is touched, so a broken download costs nothing.
    if (-not (Test-Path (Join-Path $staging "$command.exe"))) {
        throw "The archive did not contain $command.exe. Contents: $((Get-ChildItem $staging | ForEach-Object Name) -join ', ')"
    }
    if (-not (Test-Path (Join-Path $staging 'server\Aiko.Server.exe'))) {
        throw "The archive did not contain server\Aiko.Server.exe. Contents: $((Get-ChildItem $staging -Recurse | ForEach-Object Name) -join ', ')"
    }

    Stop-InstalledAiko $InstallDir

    # Replace the contents rather than the directory itself: the directory may already be on PATH, and a
    # running shell keeps resolving the path it was given. What is installed is moved aside first - a rename
    # within one volume, which works even on a file something still holds - and comes back if the copy fails.
    $previous = "$InstallDir.previous-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))"
    if (Test-Path $InstallDir) {
        New-Item -ItemType Directory -Path $previous | Out-Null
        try {
            Get-ChildItem -Path $InstallDir -Force | Move-Item -Destination $previous
        }
        catch {
            # Whatever moved aside before the failure goes back, so a refused move leaves the installation whole.
            $failure = $_.Exception.Message
            Get-ChildItem -Path $previous -Force | Move-Item -Destination $InstallDir -Force -ErrorAction SilentlyContinue
            Remove-Item -Path $previous -Recurse -Force -ErrorAction SilentlyContinue
            throw "Could not move the installed files in $InstallDir aside, so nothing was replaced: $failure"
        }
    }
    else {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    try {
        Copy-Item -Path (Join-Path $staging '*') -Destination $InstallDir -Recurse -Force
    }
    catch {
        $failure = $_.Exception.Message
        Get-ChildItem -Path $InstallDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $previous) {
            Get-ChildItem -Path $previous -Force | Move-Item -Destination $InstallDir -Force
            Remove-Item -Path $previous -Recurse -Force -ErrorAction SilentlyContinue
        }
        throw "Installing into $InstallDir failed and the previous installation was put back: $failure"
    }

    # A file still held open keeps the old copy from going; the next run's installer clears it away.
    if (Test-Path $previous) {
        Remove-Item -Path $previous -Recurse -Force -ErrorAction SilentlyContinue
    }
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

# There is no agent menu: "aiko agent install --scope user" already knows which agents are on this machine,
# and a list of five here was a second place holding the same knowledge - one that had to be kept in step with
# the adapters by hand. -Agents names them explicitly; -NoAgentSetup leaves them alone.
$agentsConnected = $false
if (-not $NoAgentSetup) {
    if ($Agents) {
        Write-Step "Connecting the agents you named: $Agents"
        & $exe agent install --scope user --agent $Agents
    }
    else {
        Write-Step 'Connecting the agents found on this machine'
        & $exe agent install --scope user
    }

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

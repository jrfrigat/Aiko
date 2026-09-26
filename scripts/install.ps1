<#
.SYNOPSIS
    Installs the Aiko daemon, CLI and stdio proxy (Windows, x64).

.DESCRIPTION
    Downloads the release from GitHub, verifies its checksum, unpacks it beside the installation, and
    hands the rest to the release's own `aiko.exe install --from`: that replaces the installed files
    atomically, rolls back on failure, records the version in install.json, keeps the directory on the
    user PATH, connects the agents, and repairs and reports. The script does only what has to happen
    before Aiko exists at all - download, verify, unpack.

    Nothing is installed machine-wide and no administrator rights are needed; .NET does not have to be
    installed either.

    Run it directly:

        irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1 | iex

    To pass options, fetch the script into a scriptblock first:

        & ([scriptblock]::Create((irm https://raw.githubusercontent.com/jrfrigat/Aiko/main/scripts/install.ps1))) -Version v0.3.1

    To install from a mirror, set AIKO_RELEASE_BASE_URL to the mirror's base address, for example
    https://mirror.example.com; the default is https://github.com. Authenticity does not rest on the
    host: the checksums are fetched from the same one, so the archive is still verified before it is
    unpacked.

.PARAMETER Version
    The release tag to install, for example "v0.1.0". Defaults to the latest release.

.PARAMETER InstallDir
    Where to install. Defaults to %LOCALAPPDATA%\Aiko\bin.

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

# Where the release is fetched from. GitHub by default; a mirror, or a machine that has to install without
# reaching it, sets AIKO_RELEASE_BASE_URL. Authenticity does not rest on the host: the checksums come from the
# same one, and the archive is verified against them before anything is unpacked.
function Get-ReleaseBaseUrl {
    $configured = $env:AIKO_RELEASE_BASE_URL
    if ([string]::IsNullOrWhiteSpace($configured)) {
        return 'https://github.com'
    }

    return $configured.TrimEnd('/')
}

function Get-LatestReleaseTag([string] $baseUrl, [string] $repository) {
    # The unauthenticated GitHub API is limited to 60 requests per public IP. That limit is often
    # shared by an office, VPN or ISP and can make the installer fail even though releases are
    # available. The regular releases/latest endpoint is not subject to that API limit and redirects
    # to /releases/tag/<tag>, so only inspect its Location header.
    $latestUrl = "$baseUrl/$repository/releases/latest"
    $request = [Net.HttpWebRequest]::Create($latestUrl)
    $request.Method = 'HEAD'
    $request.AllowAutoRedirect = $false
    $request.UserAgent = 'aiko-installer'

    try {
        $response = [Net.HttpWebResponse] $request.GetResponse()
    }
    catch {
        throw "Cannot resolve the latest release ($latestUrl): $($_.Exception.Message)"
    }

    try {
        $location = $response.Headers['Location']
    }
    finally {
        $response.Dispose()
    }

    if (-not $location -or $location -notmatch '/releases/tag/([^/?#]+)') {
        throw "The release index did not redirect $latestUrl to a release tag. Location: $location"
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

# The checksum the release publishes for this asset. SHA256SUMS is the format `shasum` writes: one line per
# asset, "<hash>  <name>", with some tools putting a "*" before a binary's name. A release that does not vouch
# for the asset is refused here, before anything is downloaded twice or unpacked.
function Get-PublishedChecksum([string] $checksumsFile, [string] $assetName, [string] $tag) {
    foreach ($line in Get-Content -Path $checksumsFile) {
        $parts = $line.Trim() -split '\s+', 2
        if ($parts.Count -ne 2) { continue }
        if ($parts[1].TrimStart('*') -eq $assetName) {
            return $parts[0]
        }
    }

    throw "Release $tag publishes no checksum for $assetName, so the download cannot be verified."
}

Assert-InstallDirIsAiko $InstallDir

$baseUrl = Get-ReleaseBaseUrl
$headers = @{ 'User-Agent' = 'aiko-installer' }
$tag = $Version
if ($Version -eq 'latest') {
    Write-Step "Looking up the latest release of $repo"
    $tag = Get-LatestReleaseTag $baseUrl $repo
}

$releaseVersion = ($tag -replace '^v', '').Split('+')[0]
$assetName = "aiko-$releaseVersion-win-x64.zip"
$checksumsName = 'SHA256SUMS'
$escapedTag = [Uri]::EscapeDataString($tag)
$releaseUrl = "$baseUrl/$repo/releases/download/$escapedTag"

# The base project template ships with the release, and something has to put it where the daemon looks for it
# before the daemon has ever run. The CLI seeds it too (SeedReleaseTemplates), but that one overwrites the file
# and this leaves it alone: an upgrade must not throw away defaults a person edited. Bringing the CLI to the
# same rule, and dropping the seed here afterwards, is an open item recorded on TASK-112.
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

# Unpacked beside the installation and never inside it: the engine moves entries within one volume, which is
# what makes the replacement reversible, and the installed version keeps working while this happens.
$staging = "$InstallDir.staging-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))"
$exitCode = 0
try {
    $archive = Join-Path $temp $assetName
    $checksumsFile = Join-Path $temp $checksumsName
    Write-Step "Downloading $assetName"
    try {
        # Basic parsing avoids the Internet Explorer dependency in Windows PowerShell 5.1.
        Invoke-WebRequest -Uri "$releaseUrl/$assetName" -OutFile $archive -Headers $headers -UseBasicParsing
        Invoke-WebRequest -Uri "$releaseUrl/$checksumsName" -OutFile $checksumsFile -Headers $headers -UseBasicParsing
    }
    catch {
        throw "Cannot download the win-x64 release $tag from $releaseUrl`: $($_.Exception.Message)"
    }

    # Checked before anything is unpacked and before the installation is touched: a substituted archive has to
    # be refused while there is still nothing to undo.
    $expected = Get-PublishedChecksum -checksumsFile $checksumsFile -assetName $assetName -tag $tag
    $actual = (Get-FileHash -Path $archive -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        throw "$assetName does not match the checksum release $tag publishes for it " +
            "(expected $expected, and the download is $actual). Nothing was installed."
    }

    Write-Step "Verified $assetName against $checksumsName"
    Write-Step "Unpacking into $staging"
    Expand-Archive -Path $archive -DestinationPath $staging -Force

    # This is not the release's layout rule - the engine validates that before it replaces anything - but the one
    # file this script is about to run, and a plain message here beats a failure from a command that never was.
    if (-not (Test-Path (Join-Path $staging "$command.exe"))) {
        throw "The archive did not contain $command.exe. Contents: $((Get-ChildItem $staging | ForEach-Object Name) -join ', ')"
    }

    Install-BaseTemplate -sourceDir $staging -dataDir (Join-Path $env:LOCALAPPDATA 'Aiko')

    # The daemon and the agents' stdio proxies run out of the install directory and hold their executables open,
    # and the engine - which stops the daemon itself - knows nothing about the proxies.
    Stop-InstalledAiko $InstallDir

    # Everything from here belongs to the release's own aiko.exe: validate the layout, replace the installed
    # files atomically, roll back on failure, record install.json, keep PATH idempotent, connect the agents,
    # repair and report. The script only says what it resolved and what it verified.
    $installArgs = @('install', '--from', $staging, '--tag', $tag, '--sha256', $actual, '--install-dir', $InstallDir)
    if ($NoPathUpdate) { $installArgs += '--no-path' }
    if ($NoAgentSetup) { $installArgs += '--no-agents' }
    elseif ($Agents) { $installArgs += @('--agents', $Agents) }

    Write-Step "Installing $tag"
    & (Join-Path $staging "$command.exe") @installArgs
    $exitCode = $LASTEXITCODE
}

finally {
    Remove-Item -Path $temp -Recurse -Force -ErrorAction SilentlyContinue
    # The engine moves the staged entries into the installation, so what its success leaves behind is an empty
    # directory beside it; after a refusal it is a directory that has to go, so that "nothing was changed" is
    # true on the disk and not only in the report.
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}

# The release's own report has already said why it could not finish, and the exit code is what a caller - the
# documented `irm ... | iex` included - reads as the outcome. A refusal left the previous version in place.
if ($exitCode -ne 0) {
    Write-Host ""
    Write-Host "The installation did not finish (aiko install exited with $exitCode); the previous version is untouched." -ForegroundColor Red
    exit $exitCode
}

$exe = Join-Path $InstallDir "$command.exe"
if (-not (Test-Path $exe)) {
    throw "The installation did not produce $command.exe in $InstallDir."
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

# The agents are connected by `install --from` itself, so there is no menu here and nothing to report about
# them afterwards: a second place holding the same knowledge is exactly what used to drift from the adapters.
Write-Host ""
Write-Host "Aiko $tag installed." -ForegroundColor Green
Write-Host "  Start the daemon:  " -NoNewline
Write-Host "aiko serve" -ForegroundColor Yellow
Write-Host "  Open the board:    " -NoNewline
Write-Host "aiko ui" -ForegroundColor Yellow
Write-Host "  Connect an agent:  " -NoNewline
Write-Host "aiko agent install --scope user" -ForegroundColor Yellow

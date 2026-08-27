param(
    [ValidateSet('build', 'install', 'clean')]
    [string]$Mode = 'build',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$props = Join-Path $root 'Directory.Build.props'
$text = Get-Content -LiteralPath $props -Raw
$version = [regex]::Match($text, '<Version>([^<]+)</Version>').Groups[1].Value
if (-not $version) { throw 'Could not find <Version> in Directory.Build.props.' }

$dist = Join-Path $root 'dist'
$publish = Join-Path $dist 'publish'
$zip = Join-Path $dist "BlindTerm-v$version.zip"
$iss = Join-Path $root 'installer\BlindTerm.iss'

if ($Mode -eq 'clean') {
    if (Test-Path -LiteralPath $dist) { Remove-Item -LiteralPath $dist -Recurse -Force }
    exit 0
}

New-Item -ItemType Directory -Path $dist -Force | Out-Null
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }

dotnet publish (Join-Path $root 'src\BlindTerm.App\BlindTerm.App.csproj') `
    --configuration Release --runtime $Runtime --self-contained true `
    -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
    --output $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

dotnet publish (Join-Path $root 'src\BlindTerm.Update\BlindTerm.Update.csproj') `
    --configuration Release --runtime $Runtime --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None -p:DebugSymbols=false `
    --output $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish of the update worker failed with exit code $LASTEXITCODE." }

$marker = Join-Path $publish '.windows-installed'
'BlindTerm installer-managed installation' | Set-Content -LiteralPath $marker -Encoding UTF8
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal

$iscc = $env:INNO_SETUP_COMPILER
if (-not $iscc) { $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe' }
if (-not (Test-Path -LiteralPath $iscc)) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
if (-not $iscc) { throw 'Inno Setup compiler was not found.' }

& $iscc "/DAppVersion=$version" $iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }

# The one line the update dialog shows before anyone decides to install. Taken from this
# version's first changelog bullet so that it cannot go stale: written down by hand, it was
# still describing the release before last.
$notes = 'A new version of BlindTerm is available.'
$changelog = Join-Path $root 'CHANGELOG.md'
if (Test-Path -LiteralPath $changelog) {
    $pattern = '(?ms)^##\s+v' + [regex]::Escape($version) + '\b.*?\n\s*\n(?<body>.*?)(?=^##\s|\z)'
    $entry = [regex]::Match((Get-Content -LiteralPath $changelog -Raw), $pattern)
    if ($entry.Success) {
        $first = $entry.Groups['body'].Value -split '\r?\n' |
            Where-Object { $_ -match '^\s*-\s+' } | Select-Object -First 1
        if ($first) {
            $first = ($first -replace '^\s*-\s+', '') -replace '`', '' -replace '\*\*', ''
            # One sentence is all the dialog has room to read out.
            $stop = [regex]::Match($first, '^.*?[.!?](\s|$)')
            $notes = if ($stop.Success) { $stop.Value.Trim() } else { $first.Trim() }
        }
    }
}

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$installer = Join-Path $dist "BlindTerm-Setup-v$version.exe"
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    version = "v$version"
    asset = Split-Path -Leaf $zip
    download_url = "https://github.com/serrebidev/BlindTerm/releases/download/v$version/$(Split-Path -Leaf $zip)"
    sha256 = $hash
    published_at = [DateTimeOffset]::UtcNow.ToString('o')
    notes_summary = $notes
    installer = [ordered]@{
        asset = Split-Path -Leaf $installer
        download_url = "https://github.com/serrebidev/BlindTerm/releases/download/v$version/$(Split-Path -Leaf $installer)"
        sha256 = $installerHash
    }
}
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $dist 'BlindTerm-update.json') -Encoding UTF8

Write-Host "Built BlindTerm v$version"
Write-Host "Portable: $zip"
Write-Host "Installer: $installer"

if ($Mode -eq 'install') {
    $process = Start-Process -FilePath $installer -ArgumentList '/SILENT' -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer returned exit code $($process.ExitCode)." }
}

<#
.SYNOPSIS
    Publishes the files of a model as the assets of a GitHub release, append only.

.DESCRIPTION
    Creates (or extends) the release `models/<BaseModel>/v<Version>` of the repository. The release holds the files of one
    model export: the shared tokenizer files, one `model-<quantization>.onnx` per quantization, the license text, and a
    `<file>.sha256` sidecar for every one of them.

    Releases are append only. The script
      * uploads an asset that the release does not have yet,
      * skips an asset whose published SHA-256 equals the local one,
      * stops before it uploads anything when a published asset of the same name has another SHA-256,
      * never replaces or removes an asset, and never removes a release.
    A new release is created as a draft, filled, verified and published without becoming the latest release.

    Run it on a maintainer machine with an authenticated `gh` (write access to the repository). With -DryRun nothing is
    written to GitHub: every step is printed, the files are staged and hashed locally, and the read-only lookup of the
    release is still made (when `gh` is not available the release counts as absent).

.PARAMETER BaseModel
    The base model, the second part of the tag (for example bert-base-cased).

.PARAMETER Version
    The model export, the integer of the last part of the tag (the v<n> of models/<BaseModel>/v<n>).

.PARAMETER File
    The files to publish, each as `<asset-name>=<path>`. Allowed asset names: model-int8-dynamic.onnx, model-int8-static.onnx,
    model-fp16.onnx, model-fp32.onnx, vocab.txt, tokenizer.json, config.json and LICENSE.

.PARAMETER Target
    The full commit SHA the tag is created at when the release is created (default: the default branch).

.PARAMETER VerifyAgainst
    A SHA256SUMS file (`<sha256>  <asset-name>` per line); the SHA-256 of every file to publish must equal its line.

.PARAMETER NotesFile
    The release notes (default: generated from the files).

.PARAMETER StageDir
    The folder the assets and their sidecars are staged in (default: a temporary folder).

.PARAMETER Repo
    The repository (default demaconsulting/WordSense).

.PARAMETER DryRun
    Prints the steps and stages the files, but writes nothing to GitHub.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9._-]*$')][string]$BaseModel,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Version,
    [Parameter(Mandatory)][string[]]$File,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$Target,
    [string]$VerifyAgainst,
    [string]$NotesFile,
    [string]$StageDir,
    [string]$Repo = 'demaconsulting/WordSense',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$AllowedAssets = '^(model-(int8-dynamic|int8-static|fp16|fp32)\.onnx|vocab\.txt|tokenizer\.json|config\.json|LICENSE)$'
$Tag = "models/$BaseModel/v$Version"

function Write-Step([string]$Text) { Write-Host "==> $Text" }

# Runs a command that changes GitHub. In a dry run it only prints the command.
function Invoke-Write([string[]]$Arguments) {
    if ($DryRun) {
        Write-Host "[dry-run] gh $($Arguments -join ' ')"
        return
    }

    & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "gh $($Arguments[0]) $($Arguments[1]) failed with exit code $LASTEXITCODE." }
}

# 1. Validate the tag, the asset names and the source files
Write-Step "Release $Tag in $Repo$(if ($DryRun) { ' (dry run)' })"
if ($Tag -notmatch '^models/[a-z0-9][a-z0-9._-]*/v[0-9]+$') { throw "The tag '$Tag' does not have the form models/<base-model>/v<n>." }
& git check-ref-format "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw "The tag '$Tag' is not a valid git reference name." }

$sources = [ordered]@{}
foreach ($entry in $File) {
    $split = $entry.IndexOf('=')
    if ($split -lt 1) { throw "'$entry' is not <asset-name>=<path>." }
    $asset = $entry.Substring(0, $split)
    $path = $entry.Substring($split + 1)
    if ($asset -notmatch $AllowedAssets) { throw "'$asset' is not an allowed asset name (extend `$AllowedAssets to add a quantization)." }
    if ($sources.Contains($asset)) { throw "The asset '$asset' is listed twice." }
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "The file '$path' for '$asset' does not exist." }
    $sources[$asset] = (Resolve-Path -LiteralPath $path).Path
}

# 2. Stage the files with their sidecars and hash them
if (-not $StageDir) { $StageDir = Join-Path ([IO.Path]::GetTempPath()) ("ws-model-" + $BaseModel + "-v" + $Version) }
New-Item -ItemType Directory -Path $StageDir -Force | Out-Null
$StageDir = (Resolve-Path -LiteralPath $StageDir).Path

$pins = @{}
if ($VerifyAgainst) {
    foreach ($line in Get-Content -LiteralPath $VerifyAgainst) {
        if ($line -match '^([0-9a-f]{64})\s+\*?(\S+)\s*$') { $pins[$Matches[2]] = $Matches[1] }
    }
}

Write-Step "Staging in $StageDir"
$local = [ordered]@{}
$sidecarHash = @{}
$utf8 = [Text.UTF8Encoding]::new($false)
foreach ($asset in $sources.Keys) {
    $staged = Join-Path $StageDir $asset
    Copy-Item -LiteralPath $sources[$asset] -Destination $staged -Force
    $hash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($VerifyAgainst) {
        if (-not $pins.ContainsKey($asset)) { throw "$VerifyAgainst has no line for '$asset'." }
        if ($pins[$asset] -ne $hash) { throw "The SHA-256 of '$asset' ($hash) differs from $VerifyAgainst ($($pins[$asset]))." }
    }

    [IO.File]::WriteAllText("$staged.sha256", "$hash  $asset`n", $utf8)
    $sidecarHash[$asset] = (Get-FileHash -LiteralPath "$staged.sha256" -Algorithm SHA256).Hash.ToLowerInvariant()
    $local[$asset] = [pscustomobject]@{ Name = $asset; Size = (Get-Item -LiteralPath $staged).Length; Sha256 = $hash }
    Write-Host ("{0,-28} {1,12:N0} bytes  {2}" -f $asset, $local[$asset].Size, $hash)
}

# 3. Look the release up (read only)
Write-Step "Looking up $Tag"
$remote = $null
$ghAvailable = $null -ne (Get-Command gh -ErrorAction SilentlyContinue)
if ($ghAvailable) {
    $json = & gh release view $Tag --repo $Repo --json isDraft,assets 2>&1
    if ($LASTEXITCODE -eq 0) {
        $remote = ($json -join "`n") | ConvertFrom-Json
    }
    elseif (($json -join "`n") -notmatch 'release not found') {
        if (-not $DryRun) { throw "gh release view failed: $($json -join ' ')" }
        Write-Warning "The release could not be looked up in a dry run: $($json -join ' ')"
    }
}
elseif (-not $DryRun) { throw 'gh is not installed.' }
else { Write-Warning 'gh is not installed; the release counts as absent.' }

# 4. Decide what to upload; a changed published asset stops everything before any write
$toUpload = [System.Collections.Generic.List[string]]::new()
$publishedAssets = @{}
if ($remote) { foreach ($a in $remote.assets) { $publishedAssets[$a.name] = $a } }

foreach ($asset in $local.Keys) {
    foreach ($name in @($asset, "$asset.sha256")) {
        $expected = if ($name -eq $asset) { $local[$asset].Sha256 } else { $sidecarHash[$asset] }
        if ($publishedAssets.ContainsKey($name)) {
            $digest = [string]$publishedAssets[$name].digest
            if ($digest -ne "sha256:$expected") { throw "The published asset '$name' has the digest '$digest', not 'sha256:$expected'. Releases are append only: nothing was uploaded." }
            Write-Host "skip   $name (already published, identical)"
        }
        else {
            $toUpload.Add($name)
            Write-Host "upload $name"
        }
    }
}

# 5. Create the release as a draft when it is absent
if (-not $remote) {
    $notes = $NotesFile
    if (-not $notes) {
        $notes = Join-Path $StageDir 'release-notes.md'
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add("Files of the model ``$BaseModel``, version $Version, for the WordSense packages.")
        $lines.Add('')
        $lines.Add('| File | Size (bytes) | SHA-256 |')
        $lines.Add('| ---- | ------------ | ------- |')
        foreach ($asset in $local.Keys) { $lines.Add("| ``$asset`` | $($local[$asset].Size) | ``$($local[$asset].Sha256)`` |") }
        $lines.Add('')
        $lines.Add('Assets are only ever added, never replaced. The SHA-256 digests pinned in the NuGet packages are authoritative; the `.sha256` files are for humans and scripts.')
        [IO.File]::WriteAllText($notes, ($lines -join "`n") + "`n", $utf8)
    }

    $create = @('release', 'create', $Tag, '--repo', $Repo, '--draft', '--latest=false', '--title', "Model: $BaseModel v$Version", '--notes-file', $notes)
    if ($Target) { $create += @('--target', $Target) }
    Write-Step 'Creating the draft release'
    Invoke-Write $create
}

# 6. Upload what is new
if ($toUpload.Count -gt 0) {
    Write-Step "Uploading $($toUpload.Count) file(s)"
    Invoke-Write (@('release', 'upload', $Tag, '--repo', $Repo) + @($toUpload | ForEach-Object { Join-Path $StageDir $_ }))
}
else { Write-Step 'Nothing to upload' }

# 7. Verify the release against the local files
Write-Step 'Verifying the published assets'
if ($DryRun) { Write-Host '[dry-run] gh release view: compare size, digest and state of every asset' }
else {
    $after = (& gh release view $Tag --repo $Repo --json isDraft,assets) -join "`n" | ConvertFrom-Json
    $byName = @{}
    foreach ($a in $after.assets) { $byName[$a.name] = $a }
    foreach ($asset in $local.Keys) {
        foreach ($name in @($asset, "$asset.sha256")) {
            $path = Join-Path $StageDir $name
            if (-not $byName.ContainsKey($name)) { throw "The asset '$name' is missing from the release." }
            $published = $byName[$name]
            $sha = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($published.size -ne (Get-Item -LiteralPath $path).Length) { throw "The size of '$name' differs: $($published.size)." }
            if ([string]$published.digest -ne "sha256:$sha") { throw "The digest of '$name' differs: $($published.digest)." }
            if ($published.PSObject.Properties['state'] -and $published.state -ne 'uploaded') { throw "The asset '$name' is in the state '$($published.state)'." }
        }
    }

    # 8. Publish a draft, without making it the latest release
    if ($after.isDraft) {
        Write-Step 'Publishing the release (not the latest)'
        Invoke-Write @('release', 'edit', $Tag, '--repo', $Repo, '--draft=false', '--latest=false')
    }

    $latest = & gh api "repos/$Repo/releases/latest" --jq .tag_name
    if ($latest -eq $Tag) { throw "The release $Tag became the latest release." }
    Write-Host "The latest release is still $latest."
}

if ($DryRun) { Write-Host '[dry-run] nothing was written to GitHub.' }
Write-Step "Done: $Tag. Check the files with the library downloader: dotnet run --project tools/WordSense.Builder -c Release -- fetch-models"

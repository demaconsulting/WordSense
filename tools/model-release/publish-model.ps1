<#
.SYNOPSIS
    Publishes the files of a model as the assets of a GitHub release, append only.

.DESCRIPTION
    Creates (or extends) the release `models/<BaseModel>/v<Version>` of the repository. The release holds the files of one
    model export: the shared tokenizer files, one `model-<quantization>.onnx` per quantization, the license text, the
    documentation (`README.md` of the export and one `model-<quantization>.md` per quantization file), and a
    `<file>.sha256` sidecar for every one of them.

    A new release must include `README.md`, and every `model-<quantization>.onnx` that is published must come with its
    `model-<quantization>.md` (in the same run or already published). The body (description) of a new release is a short
    summary that links to the README and to the quantization documents.

    Releases are append only. The script
      * uploads an asset that the release does not have yet,
      * skips an asset whose published SHA-256 equals the local one,
      * stops before it uploads anything when a published asset of the same name has another SHA-256,
      * never replaces or removes an asset, and never removes a release.
    The body of an existing release is not an asset: with -UpdateBody the script writes the summary again from the
    published assets (or from -NotesFile) and changes nothing else.
    A new release is created as a draft, filled, verified and published without becoming the latest release. The latest
    release is recorded first, watched after the publish and restored if it moved.

    Run it on a maintainer machine with an authenticated `gh` (write access to the repository). With -DryRun nothing is
    written to GitHub: every step is printed, the files are staged and hashed locally, and the read-only lookup of the
    release is still made (when `gh` is not available the release counts as absent).

.PARAMETER BaseModel
    The base model, the second part of the tag (for example bert-base-cased).

.PARAMETER Version
    The model export, the integer of the last part of the tag (the v<n> of models/<BaseModel>/v<n>).

.PARAMETER File
    The files to publish, each as `<asset-name>=<path>`. Allowed asset names: model-int8-dynamic.onnx, model-int8-static.onnx,
    model-fp16.onnx, model-fp32.onnx, the documents README.md and model-<quantization>.md of the same quantizations,
    vocab.txt, tokenizer.json, config.json and LICENSE. Optional with -UpdateBody.

.PARAMETER UpdateBody
    Writes the body of the release again: a short summary that links to the README and to the quantization documents,
    built from the assets that the release has after this run (or -NotesFile). The release must exist. Without -File
    nothing but the body changes.

.PARAMETER Target
    The full commit SHA the tag is created at when the release is created (default: the default branch).

.PARAMETER VerifyAgainst
    A SHA256SUMS file (`<sha256>  <asset-name>` per line); the SHA-256 of every file to publish must equal its line.
    Documents are not listed there and are not checked.

.PARAMETER NotesFile
    The body of the release (default: generated from the assets).

.PARAMETER StageDir
    The folder the assets and their sidecars are staged in (default: a temporary folder).

.PARAMETER Repo
    The repository (default demaconsulting/WordSense).

.PARAMETER LatestWaitSeconds
    How long the latest-release pointer is watched after the publish (default 30). GitHub moves it lazily; whenever it
    differs from the value recorded before the publish, it is restored with `gh release edit <recorded> --latest`.

.PARAMETER LatestIntervalSeconds
    The time between two looks at the pointer (default 3).

.PARAMETER DryRun
    Prints the steps and stages the files, but writes nothing to GitHub.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9._-]*$')][string]$BaseModel,
    [Parameter(Mandatory)][ValidateRange(1, 2147483647)][int]$Version,
    [string[]]$File,
    [switch]$UpdateBody,
    [ValidatePattern('^[0-9a-f]{40}$')][string]$Target,
    [string]$VerifyAgainst,
    [string]$NotesFile,
    [string]$StageDir,
    [string]$Repo = 'demaconsulting/WordSense',
    [ValidateRange(0, 600)][int]$LatestWaitSeconds = 30,
    [ValidateRange(0, 60)][int]$LatestIntervalSeconds = 3,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$AllowedAssets = '^(model-(int8-dynamic|int8-static|fp16|fp32)\.(onnx|md)|README\.md|vocab\.txt|tokenizer\.json|config\.json|LICENSE)$'
$Tag = "models/$BaseModel/v$Version"

function Write-Step([string]$Text) { Write-Host "==> $Text" }

# True for the documents of a release (README.md and model-<quantization>.md); their digests are not listed in SHA256SUMS.
function Test-Document([string]$Name) { return $Name -match '\.md$' }

# Builds the body of the release: a short summary that links to the README and to the quantization documents, then the
# files of the release. $Items holds Name, Size and Sha256 of every asset; sidecars and documents are not listed again.
function New-ReleaseBody([object[]]$Items) {
    $base = "https://github.com/$Repo/releases/download/$Tag"
    $names = @($Items | ForEach-Object { $_.Name })
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("Files of the model ``$BaseModel``, version $Version, for the WordSense packages.")
    $lines.Add('')
    if ($names -contains 'README.md') {
        $lines.Add("- [README.md]($base/README.md): the export: the upstream checkpoint, what was changed in the graph and why, the shared files, the license.")
    }

    foreach ($doc in @($names | Where-Object { $_ -match '^model-.+\.md$' } | Sort-Object)) {
        $quantization = $doc -replace '^model-|\.md$', ''
        $lines.Add("- [$doc]($base/$doc): how ``model-$quantization.onnx`` was made and why this quantization.")
    }

    $lines.Add('')
    $lines.Add('| File | Size (bytes) | SHA-256 |')
    $lines.Add('| ---- | ------------ | ------- |')
    foreach ($item in @($Items | Where-Object { $_.Name -notmatch '\.sha256$' -and -not (Test-Document $_.Name) } | Sort-Object Name)) {
        $lines.Add("| ``$($item.Name)`` | $($item.Size) | ``$($item.Sha256)`` |")
    }

    $lines.Add('')
    $lines.Add('Assets are only ever added, never replaced. The SHA-256 digests pinned in the NuGet packages are authoritative; the `.sha256` files are for humans and scripts.')
    return ($lines -join "`n") + "`n"
}

# Runs a command that changes GitHub. In a dry run it only prints the command.
function Invoke-Write([string[]]$Arguments) {
    if ($DryRun) {
        Write-Host "[dry-run] gh $($Arguments -join ' ')"
        return
    }

    & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "gh $($Arguments[0]) $($Arguments[1]) failed with exit code $LASTEXITCODE." }
}

# Reads the tag of the latest release of the repository (read only); null when there is none or gh cannot tell.
function Get-LatestTag {
    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) { return $null }
    $answer = & gh api "repos/$Repo/releases/latest" --jq .tag_name 2>&1
    if ($LASTEXITCODE -eq 0) { return ([string]($answer -join '')).Trim() }
    if (($answer -join ' ') -match 'Not Found|404') { return $null }
    if ($DryRun) { Write-Warning "The latest release could not be read: $($answer -join ' ')"; return $null }
    throw "Reading the latest release failed: $($answer -join ' ')"
}

# Makes sure that the latest release is the one that was recorded before the publish. GitHub moves the pointer lazily
# (a look right after the publish can still show the old value), so the pointer is watched for $LatestWaitSeconds and put
# back with `gh release edit <recorded> --latest` as soon as it differs; the watch then starts again. A pointer that
# cannot be put back is an error.
function Confirm-Latest([string]$Recorded) {
    if (-not $Recorded) { Write-Host 'There was no latest release before this publish; nothing to restore.' }
    $restores = 0
    $deadline = [DateTime]::UtcNow.AddSeconds($LatestWaitSeconds)
    while ($true) {
        $now = [string](Get-LatestTag)
        if ($now -ne $Recorded) {
            if (-not $Recorded) {
                throw "The release $now became the latest release, and there was no latest release before: nothing to restore. Fix it by hand."
            }

            if ($restores -ge 3) { throw "The latest release is '$now', not '$Recorded', and it could not be restored after $restores attempts. Fix it by hand." }
            Write-Warning "The latest release is '$now', not '$Recorded'. Restoring it."
            $restores++
            & gh release edit $Recorded --repo $Repo --latest
            if ($LASTEXITCODE -ne 0) { throw "gh release edit $Recorded --latest failed with exit code $LASTEXITCODE; the latest release is '$now'. Fix it by hand." }
            $deadline = [DateTime]::UtcNow.AddSeconds($LatestWaitSeconds)
        }

        if ([DateTime]::UtcNow -ge $deadline) { break }
        Start-Sleep -Seconds $LatestIntervalSeconds
    }

    if ($Recorded) { Write-Host "The latest release is still $Recorded." }
    elseif (-not (Get-LatestTag)) { Write-Host 'The repository still has no latest release.' }
}

# 1. Validate the tag, the asset names and the source files
Write-Step "Release $Tag in $Repo$(if ($DryRun) { ' (dry run)' })"
if ($Tag -notmatch '^models/[a-z0-9][a-z0-9._-]*/v[0-9]+$') { throw "The tag '$Tag' does not have the form models/<base-model>/v<n>." }
& git check-ref-format "refs/tags/$Tag"
if ($LASTEXITCODE -ne 0) { throw "The tag '$Tag' is not a valid git reference name." }

$sources = [ordered]@{}
if (-not $File -and -not $UpdateBody) { throw 'Give -File <asset-name>=<path> ... to publish, or -UpdateBody to write the body of the release again.' }
foreach ($entry in @($File)) {
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
    if ($sources[$asset] -ne $staged) { Copy-Item -LiteralPath $sources[$asset] -Destination $staged -Force }
    $hash = (Get-FileHash -LiteralPath $staged -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($VerifyAgainst -and -not (Test-Document $asset)) {
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

# The latest release as it is before anything is written; it is put back if this publish moves it
$latestBefore = Get-LatestTag
if ($ghAvailable) {
    if ($latestBefore) { Write-Host "The latest release is $latestBefore." }
    else { Write-Host 'The repository has no latest release.' }
}

# 4. Decide what to upload; a changed published asset stops everything before any write
$toUpload = [System.Collections.Generic.List[string]]::new()
$publishedAssets = @{}
if ($remote) { foreach ($a in $remote.assets) { $publishedAssets[$a.name] = $a } }

# The documentation is required: a new release has its README.md, and every quantization file comes with its document
if ($UpdateBody -and -not $remote) { throw "-UpdateBody needs an existing release; $Tag was not found." }
if (-not $remote -and -not $sources.Contains('README.md')) { throw "A new release needs README.md: add -File 'README.md=<path>'. Nothing was uploaded." }
foreach ($asset in $sources.Keys) {
    if ($asset -match '^model-(.+)\.onnx$') {
        $document = "model-$($Matches[1]).md"
        if (-not $sources.Contains($document) -and -not $publishedAssets.ContainsKey($document)) {
            throw "'$asset' needs its document '$document': add -File '$document=<path>'. Nothing was uploaded."
        }
    }
}

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
        [IO.File]::WriteAllText($notes, (New-ReleaseBody @($local.Values)), $utf8)
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
if ($DryRun) {
    Write-Host '[dry-run] gh release view: compare size, digest and state of every asset'
    if ($UpdateBody) {
        Write-Step 'Updating the body of the release'
        $items = @($publishedAssets.Values | ForEach-Object { [pscustomobject]@{ Name = $_.name; Size = $_.size; Sha256 = ([string]$_.digest) -replace '^sha256:', '' } }) + @($local.Values)
        $notes = Join-Path $StageDir 'release-notes.md'
        [IO.File]::WriteAllText($notes, $(if ($NotesFile) { Get-Content -LiteralPath $NotesFile -Raw } else { New-ReleaseBody $items }), $utf8)
        Invoke-Write @('release', 'edit', $Tag, '--repo', $Repo, '--notes-file', $notes, '--latest=false')
        Write-Host (Get-Content -LiteralPath $notes -Raw)
    }
}
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

    # 8b. Write the body again from the assets the release has now (the body is not an asset)
    if ($UpdateBody) {
        Write-Step 'Updating the body of the release'
        $items = @($after.assets | ForEach-Object { [pscustomobject]@{ Name = $_.name; Size = $_.size; Sha256 = ([string]$_.digest) -replace '^sha256:', '' } })
        $notes = $NotesFile
        if (-not $notes) {
            $notes = Join-Path $StageDir 'release-notes.md'
            [IO.File]::WriteAllText($notes, (New-ReleaseBody $items), $utf8)
        }

        Invoke-Write @('release', 'edit', $Tag, '--repo', $Repo, '--notes-file', $notes, '--latest=false')
    }

    # 9. GitHub moves the latest pointer lazily, so one early look proves nothing: watch it for a while and put it back
    Confirm-Latest -Recorded $latestBefore
}

if ($DryRun) { Write-Host '[dry-run] nothing was written to GitHub.' }
Write-Step "Done: $Tag. Check the files with the library downloader: dotnet run --project tools/WordSense.Builder -c Release -- fetch-models"

<#
.SYNOPSIS
    Tests the documentation rule of publish-model.ps1 with a fake gh; nothing is written to GitHub.

.DESCRIPTION
    The release must hold README.md, and every model-<quantization>.onnx must come with its model-<quantization>.md. The
    rule is checked over the assets that the release already has together with the files of the run. The script runs
    publish-model.ps1 with -DryRun against a fake `gh` function (a function of the caller is visible to the script it
    runs) that answers the release lookup from a table, and checks which runs pass and which stop.

    Run from any folder: pwsh tools/model-release/test-publish-model.ps1. It exits 1 when a case fails.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$publish = Join-Path $PSScriptRoot 'publish-model.ps1'
$work = Join-Path ([IO.Path]::GetTempPath()) ('publish-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
foreach ($name in 'README.md', 'model-int8-dynamic.md', 'model-int8-dynamic.onnx', 'model-fp32.md', 'model-fp32.onnx', 'vocab.txt') {
    Set-Content -LiteralPath (Join-Path $work $name) -Value "test $name"
}

function Get-File([string[]]$Names) { return @($Names | ForEach-Object { "$_=$(Join-Path $work $_)" }) }

function New-Asset([string]$Name) { return @{ name = $Name; size = 1; digest = 'sha256:' + ('0' * 64) } }

# The release lookup of the fake gh: null is a release that does not exist
$global:ReleaseLookup = $null

function gh {
    if ($args[0] -eq 'api') { $global:LASTEXITCODE = 0; return '0.1.0-beta.8' }
    if ($args[0] -eq 'release' -and $args[1] -eq 'view') {
        if ($null -eq $global:ReleaseLookup) { $global:LASTEXITCODE = 1; return 'release not found' }
        $global:LASTEXITCODE = 0
        return ($global:ReleaseLookup | ConvertTo-Json -Depth 5 -Compress)
    }

    $global:LASTEXITCODE = 1
    return "unexpected gh call: $args"
}

# Runs the script as a dry run; returns whether it passed and what it printed
function Invoke-Publish([string[]]$Files, [switch]$UpdateBody) {
    $parameters = @{ BaseModel = 'bert-base-cased'; Version = 1; StageDir = (Join-Path $work 'stage'); DryRun = $true; LatestWaitSeconds = 0 }
    if ($Files) { $parameters.File = Get-File $Files }
    if ($UpdateBody) { $parameters.UpdateBody = $true }
    try {
        $text = (& $publish @parameters *>&1 | Out-String)
        return [pscustomobject]@{ Passed = $true; Text = $text }
    }
    catch {
        return [pscustomobject]@{ Passed = $false; Text = $_.Exception.Message }
    }
}

$failures = 0
function Assert-Case([string]$Name, [bool]$ExpectPass, $Result, [string]$Mentions = '') {
    $ok = ($Result.Passed -eq $ExpectPass) -and ($Mentions -eq '' -or $Result.Text.Contains($Mentions))
    if ($ok) { Write-Host "ok   $Name" }
    else {
        $script:failures++
        Write-Host "FAIL $Name (passed: $($Result.Passed), expected: $ExpectPass)"
        Write-Host $Result.Text
    }
}

try {
    # A release that was published without documents, as the v1 releases are
    $v1 = @{ isDraft = $false; assets = @('model-int8-dynamic.onnx', 'vocab.txt', 'LICENSE' | ForEach-Object { New-Asset $_ }) }
    $documented = @{ isDraft = $false; assets = @('model-int8-dynamic.onnx', 'model-int8-dynamic.md', 'README.md', 'vocab.txt' | ForEach-Object { New-Asset $_ }) }

    $global:ReleaseLookup = $v1
    Assert-Case 'v1 append: README.md and model-int8-dynamic.md together pass' $true (Invoke-Publish 'README.md', 'model-int8-dynamic.md' -UpdateBody)
    Assert-Case 'v1 append: README.md alone stops (document of the onnx file missing)' $false (Invoke-Publish 'README.md') "needs its document 'model-int8-dynamic.md'"
    Assert-Case 'v1 append: model-int8-dynamic.md alone stops (README.md missing)' $false (Invoke-Publish 'model-int8-dynamic.md') 'needs README.md'
    Assert-Case 'v1: body update alone stops while the documents are missing' $false (Invoke-Publish -UpdateBody) 'needs README.md'
    Assert-Case 'v1 append: a new quantization with its document, without README.md, stops' $false (Invoke-Publish 'model-fp32.onnx', 'model-fp32.md') 'needs README.md'

    $global:ReleaseLookup = $documented
    Assert-Case 'documented release: body update alone passes' $true (Invoke-Publish -UpdateBody)
    Assert-Case 'documented release: a new quantization with its document passes' $true (Invoke-Publish 'model-fp32.onnx', 'model-fp32.md')
    Assert-Case 'documented release: a new quantization without its document stops' $false (Invoke-Publish 'model-fp32.onnx') "needs its document 'model-fp32.md'"

    $global:ReleaseLookup = $null
    Assert-Case 'new release: files with all documents pass' $true (Invoke-Publish 'model-int8-dynamic.onnx', 'model-int8-dynamic.md', 'README.md', 'vocab.txt')
    Assert-Case 'new release: without README.md stops' $false (Invoke-Publish 'model-int8-dynamic.onnx', 'model-int8-dynamic.md') 'needs README.md'
    Assert-Case 'new release: without the document of the onnx file stops' $false (Invoke-Publish 'model-int8-dynamic.onnx', 'README.md') "needs its document 'model-int8-dynamic.md'"
    Assert-Case 'new release: body update stops (no release)' $false (Invoke-Publish 'README.md' -UpdateBody) 'needs an existing release'
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures -gt 0) { Write-Host "$failures case(s) failed."; exit 1 }
Write-Host 'All cases passed.'

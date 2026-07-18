[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$')]
    [string]$SourceCommit,

    [string]$OutputDirectory = "artifacts\catalog"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
}

& python (Join-Path $PSScriptRoot "repository_tool.py") `
    --root $repository `
    stage `
    --commit $SourceCommit `
    --output $resolvedOutput
if ($LASTEXITCODE -ne 0) {
    throw "Catalog staging failed with exit code $LASTEXITCODE."
}

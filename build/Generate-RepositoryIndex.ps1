[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$')]
    [string]$Commit,

    [string]$OutputPath = "artifacts\repo.json",

    [switch]$RequireReleaseIntegrity
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$resolvedOutput = if ([IO.Path]::IsPathRooted($OutputPath)) {
    [IO.Path]::GetFullPath($OutputPath)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repository $OutputPath))
}

$arguments = @(
    (Join-Path $PSScriptRoot "repository_tool.py"),
    "--root",
    $repository,
    "generate",
    "--commit",
    $Commit,
    "--output",
    $resolvedOutput
)
if ($RequireReleaseIntegrity) {
    $arguments += "--require-release-integrity"
}

& python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Repository index generation failed with exit code $LASTEXITCODE."
}

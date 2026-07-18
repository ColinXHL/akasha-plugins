[CmdletBinding()]
param(
    [switch]$RequireReleaseIntegrity
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$arguments = @(
    (Join-Path $PSScriptRoot "repository_tool.py"),
    "--root",
    $repository,
    "validate"
)
if ($RequireReleaseIntegrity) {
    $arguments += "--require-release-integrity"
}

& python @arguments
if ($LASTEXITCODE -ne 0) {
    throw "Plugin manifest validation failed with exit code $LASTEXITCODE."
}

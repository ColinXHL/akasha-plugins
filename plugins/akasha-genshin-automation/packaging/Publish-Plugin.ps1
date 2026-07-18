[CmdletBinding()]
param(
    [ValidateSet("Release")]
    [string]$Configuration = "Release",
    [ValidateSet("win-x64")]
    [string]$Runtime = "win-x64",
    [string]$OutputDirectory = "artifacts\release",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$pluginRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$repository = [IO.Path]::GetFullPath((Join-Path $pluginRoot "..\.."))
$backendRoot = Join-Path $pluginRoot "backend"
$frontendRoot = Join-Path $pluginRoot "frontend"
$workerProject = Join-Path $backendRoot "src\AkashaAutomation.Worker\AkashaAutomation.Worker.csproj"
$solution = Join-Path $backendRoot "AkashaAutomation.sln"
$manifestPath = Join-Path $pluginRoot "manifest.json"
$outputRoot = if ([IO.Path]::IsPathRooted($OutputDirectory)) {
    [IO.Path]::GetFullPath($OutputDirectory)
}
else {
    [IO.Path]::GetFullPath((Join-Path $repository $OutputDirectory))
}

foreach ($requiredPath in @(
        $frontendRoot,
        $workerProject,
        $solution,
        $manifestPath
    )) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required release input was not found: $requiredPath"
    }
}

$pluginManifest =
    Get-Content -LiteralPath $manifestPath -Raw |
    ConvertFrom-Json
$pluginId = [string]$pluginManifest.id
$pluginVersion = [string]$pluginManifest.version
$distribution = $pluginManifest.distribution
$backend = $pluginManifest.backend
$expectedTag = "$pluginId-v$pluginVersion"
$archiveName = "$pluginId-$pluginVersion-$Runtime.zip"

if ($pluginId -notmatch '^[a-z0-9](?:[a-z0-9._-]*[a-z0-9])?$' -or
    $pluginVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$') {
    throw "manifest.json contains an invalid id or version."
}
if ([string]$distribution.type -ne "release" -or
    [string]$distribution.tag -ne $expectedTag -or
    [string]$distribution.asset -ne $archiveName) {
    throw "manifest.json Release naming does not match $expectedTag / $archiveName."
}
if ([string]$backend.entry -ne "runtime/AkashaAutomation.Worker.exe" -or
    [int]$backend.protocolVersion -ne 1 -or
    [string]$backend.integrityLevel -ne "inherit") {
    throw "manifest.json companion contract is invalid."
}

$archivePath =
    [IO.Path]::GetFullPath((Join-Path $outputRoot $archiveName))
$checksumPath = "$archivePath.sha256"
$workingRoot = Join-Path (
    [IO.Path]::GetTempPath()) (
    "AkashaPlugins.AutomationRelease.$([Guid]::NewGuid().ToString('N'))")
$stagingRoot = Join-Path $workingRoot "staging"
$packageRoot = Join-Path $stagingRoot $pluginId
$verificationRoot = Join-Path $workingRoot "verify"

function Assert-ContainedPath {
    param(
        [Parameter(Mandatory)]
        [string]$Root,
        [Parameter(Mandatory)]
        [string]$Candidate
    )

    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar)
    $resolvedCandidate = [IO.Path]::GetFullPath($Candidate)
    $rootPrefix =
        $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedCandidate.StartsWith(
            $rootPrefix,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes its expected root: $resolvedCandidate"
    }
}

function Get-PackageFileRecords {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    return @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            Where-Object { $_.Name -ne "package-manifest.json" } |
            Sort-Object FullName |
            ForEach-Object {
                $relativePath = [IO.Path]::GetRelativePath(
                    $Root,
                    $_.FullName).Replace('\', '/')
                [ordered]@{
                    path = $relativePath
                    size = $_.Length
                    sha256 = (
                        Get-FileHash `
                            -LiteralPath $_.FullName `
                            -Algorithm SHA256
                    ).Hash.ToLowerInvariant()
                }
            }
    )
}

function Assert-PackagePayload {
    param(
        [Parameter(Mandatory)]
        [string]$Root
    )

    $packageManifestPath =
        Join-Path $Root "package-manifest.json"
    foreach ($requiredFile in @(
            "manifest.json",
            "frontend\main.js",
            "frontend\settings_ui.json",
            "runtime\AkashaAutomation.Worker.exe",
            "runtime\Assets\Config\Pick\default_pick_black_lists.json",
            "LICENSE",
            "DERIVATION.md",
            "THIRD_PARTY_NOTICES.md"
        )) {
        if (-not (Test-Path `
                -LiteralPath (Join-Path $Root $requiredFile) `
                -PathType Leaf)) {
            throw "Published package is missing $requiredFile."
        }
    }

    if (-not (Test-Path `
            -LiteralPath (Join-Path $Root "runtime\Assets\Model\PaddleOCR") `
            -PathType Container)) {
        throw "Published package is missing PaddleOCR models."
    }

    $forbiddenFiles = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            Where-Object {
                $_.Extension -in @(".pdb", ".log") -or
                $_.Name -in @("config.json", "library.json") -or
                $_.FullName -match '[\\/](testdata|logs?)[\\/]'
            }
    )
    if ($forbiddenFiles.Count -gt 0) {
        throw "Published package contains forbidden files: $($forbiddenFiles.FullName -join ', ')"
    }

    if (-not (Test-Path `
            -LiteralPath $packageManifestPath `
            -PathType Leaf)) {
        throw "Published package is missing package-manifest.json."
    }

    $packageManifest =
        Get-Content -LiteralPath $packageManifestPath -Raw |
        ConvertFrom-Json
    if ($packageManifest.schemaVersion -ne 1 -or
        $packageManifest.plugin.id -ne $pluginId -or
        $packageManifest.plugin.version -ne $pluginVersion -or
        $packageManifest.runtime -ne $Runtime) {
        throw "package-manifest.json metadata does not match manifest.json."
    }

    $declared = @{}
    foreach ($file in $packageManifest.files) {
        $relativePath = ([string]$file.path).Replace(
            '/',
            [IO.Path]::DirectorySeparatorChar)
        $fullPath =
            [IO.Path]::GetFullPath((Join-Path $Root $relativePath))
        Assert-ContainedPath -Root $Root -Candidate $fullPath
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Manifest file is missing: $($file.path)"
        }
        if ($declared.ContainsKey([string]$file.path)) {
            throw "Manifest contains a duplicate path: $($file.path)"
        }

        $declared[[string]$file.path] = $true
        $actualFile = Get-Item -LiteralPath $fullPath
        $actualHash = (
            Get-FileHash -LiteralPath $fullPath -Algorithm SHA256
        ).Hash.ToLowerInvariant()
        if ($actualFile.Length -ne [long]$file.size -or
            $actualHash -ne ([string]$file.sha256).ToLowerInvariant()) {
            throw "Manifest verification failed: $($file.path)"
        }
    }

    $actualPaths = @(
        Get-ChildItem -LiteralPath $Root -Recurse -File |
            Where-Object { $_.Name -ne "package-manifest.json" } |
            ForEach-Object {
                [IO.Path]::GetRelativePath(
                    $Root,
                    $_.FullName).Replace('\', '/')
            }
    )
    if ($actualPaths.Count -ne $declared.Count -or
        @($actualPaths |
            Where-Object { -not $declared.ContainsKey($_) }).Count -gt 0) {
        throw "Package contents do not match package-manifest.json."
    }
}

try {
    if (-not $SkipTests) {
        dotnet test $solution `
            --configuration $Configuration `
            --nologo
        if ($LASTEXITCODE -ne 0) {
            throw "Tests failed with exit code $LASTEXITCODE."
        }
    }

    New-Item -ItemType Directory -Path $packageRoot -Force |
        Out-Null
    Copy-Item `
        -LiteralPath $manifestPath `
        -Destination (Join-Path $packageRoot "manifest.json")
    Copy-Item `
        -LiteralPath $frontendRoot `
        -Destination (Join-Path $packageRoot "frontend") `
        -Recurse
    Copy-Item `
        -LiteralPath (Join-Path $pluginRoot "README.md") `
        -Destination (Join-Path $packageRoot "README.md")

    $workerOutput = Join-Path $packageRoot "runtime"
    dotnet publish $workerProject `
        --configuration $Configuration `
        --runtime $Runtime `
        --self-contained false `
        --output $workerOutput `
        --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Worker publish failed with exit code $LASTEXITCODE."
    }

    Get-ChildItem -LiteralPath $packageRoot -Recurse -File |
        Where-Object {
            $_.Name -eq ".gitkeep" -or
            $_.Extension -eq ".pdb"
        } |
        Remove-Item -Force

    foreach ($legalFile in @(
            "LICENSE",
            "DERIVATION.md",
            "THIRD_PARTY_NOTICES.md"
        )) {
        Copy-Item `
            -LiteralPath (Join-Path $pluginRoot $legalFile) `
            -Destination (Join-Path $packageRoot $legalFile)
    }

    $packageManifest = [ordered]@{
        schemaVersion = 1
        plugin = [ordered]@{
            id = $pluginId
            version = $pluginVersion
        }
        runtime = $Runtime
        files = Get-PackageFileRecords -Root $packageRoot
    }
    $packageManifest |
        ConvertTo-Json -Depth 6 |
        Set-Content `
            -LiteralPath (Join-Path $packageRoot "package-manifest.json") `
            -Encoding utf8NoBOM

    Assert-PackagePayload -Root $packageRoot

    New-Item -ItemType Directory -Path $outputRoot -Force |
        Out-Null
    foreach ($existingOutput in @($archivePath, $checksumPath)) {
        Assert-ContainedPath `
            -Root $outputRoot `
            -Candidate $existingOutput
        if (Test-Path -LiteralPath $existingOutput) {
            Remove-Item -LiteralPath $existingOutput -Force
        }
    }

    [IO.Compression.ZipFile]::CreateFromDirectory(
        $stagingRoot,
        $archivePath,
        [IO.Compression.CompressionLevel]::Optimal,
        $false)

    [IO.Compression.ZipFile]::ExtractToDirectory(
        $archivePath,
        $verificationRoot)
    Assert-PackagePayload `
        -Root (Join-Path $verificationRoot $pluginId)

    $archiveHash = (
        Get-FileHash -LiteralPath $archivePath -Algorithm SHA256
    ).Hash.ToLowerInvariant()
    "$archiveHash *$archiveName" |
        Set-Content `
            -LiteralPath $checksumPath `
            -Encoding ascii `
            -NoNewline

    $archiveSize = (Get-Item -LiteralPath $archivePath).Length
    Write-Output "Plugin package created: $archivePath"
    Write-Output "Package size: $archiveSize"
    Write-Output "Package SHA-256: $archiveHash"
}
finally {
    if (Test-Path -LiteralPath $workingRoot) {
        $resolvedTempRoot = [IO.Path]::GetFullPath(
            [IO.Path]::GetTempPath()).TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar
        $resolvedWorkingRoot = [IO.Path]::GetFullPath($workingRoot)
        if ($resolvedWorkingRoot.StartsWith(
                $resolvedTempRoot,
                [StringComparison]::OrdinalIgnoreCase) -and
            [IO.Path]::GetFileName($resolvedWorkingRoot).StartsWith(
                "AkashaPlugins.AutomationRelease.",
                [StringComparison]::Ordinal)) {
            Remove-Item `
                -LiteralPath $resolvedWorkingRoot `
                -Recurse `
                -Force
        }
    }
}

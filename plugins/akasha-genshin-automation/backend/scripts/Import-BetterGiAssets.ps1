[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Source,

    [string] $ManifestPath = (Join-Path $PSScriptRoot '..\upstream\bettergi\manifest.json'),

    [switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-ContainedPath {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )

    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "Manifest path must be relative: $RelativePath"
    }

    $normalizedRelativePath = $RelativePath.Replace('/', [IO.Path]::DirectorySeparatorChar)
    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $candidatePath = [IO.Path]::GetFullPath((Join-Path $rootPath $normalizedRelativePath))
    $rootPrefix = $rootPath + [IO.Path]::DirectorySeparatorChar

    if (-not $candidatePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Manifest path escapes its allowed root: $RelativePath"
    }

    return $candidatePath
}

function Assert-NoReparsePoint {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $currentPath = [IO.Path]::GetFullPath($Path)

    while ($currentPath.Length -ge $rootPath.Length) {
        if (Test-Path -LiteralPath $currentPath) {
            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Reparse points and symbolic links are not allowed in an import path: $currentPath"
            }
        }

        if ($currentPath.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }

        $parentPath = [IO.Directory]::GetParent($currentPath)
        if ($null -eq $parentPath) {
            break
        }

        $currentPath = $parentPath.FullName
    }
}

function Get-SevenZipPath {
    $command = Get-Command 7z -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $knownPaths = @(
        (Join-Path $env:ProgramFiles '7-Zip\7z.exe'),
        (Join-Path ${env:ProgramFiles(x86)} '7-Zip\7z.exe')
    )

    foreach ($path in $knownPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path) -and (Test-Path -LiteralPath $path -PathType Leaf)) {
            return $path
        }
    }

    throw 'Importing an archive requires 7z.exe on PATH or in the standard 7-Zip installation directory.'
}

function Assert-ArchiveEntriesAreSafe {
    param(
        [Parameter(Mandatory = $true)][string] $SevenZipPath,
        [Parameter(Mandatory = $true)][string] $ArchivePath
    )

    $listing = & $SevenZipPath l -slt -- $ArchivePath
    if ($LASTEXITCODE -ne 0) {
        throw "7-Zip could not list archive '$ArchivePath'."
    }

    $readingEntries = $false
    foreach ($line in $listing) {
        if ($line -eq '----------') {
            $readingEntries = $true
            continue
        }

        if (-not $readingEntries -or -not $line.StartsWith('Path = ', [StringComparison]::Ordinal)) {
            continue
        }

        $entryPath = $line.Substring(7).Replace('\', '/')
        if ([IO.Path]::IsPathRooted($entryPath) -or $entryPath.Split('/') -contains '..') {
            throw "Archive contains an unsafe entry path: $entryPath"
        }
    }
}

function Get-JsonStringListStats {
    param([Parameter(Mandatory = $true)][string] $Path)

    $options = [Text.Json.JsonDocumentOptions]::new()
    $options.AllowTrailingCommas = $true
    $options.CommentHandling = [Text.Json.JsonCommentHandling]::Skip
    $document = [Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($Path), $options)

    try {
        if ($document.RootElement.ValueKind -ne [Text.Json.JsonValueKind]::Array) {
            throw "JSON list asset must contain an array: $Path"
        }

        $count = 0
        $uniqueValues = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $enumerator = $document.RootElement.EnumerateArray()
        while ($enumerator.MoveNext()) {
            if ($enumerator.Current.ValueKind -ne [Text.Json.JsonValueKind]::String) {
                throw "JSON list asset must contain strings only: $Path"
            }

            $count++
            [void] $uniqueValues.Add($enumerator.Current.GetString())
        }

        return [pscustomobject]@{
            Count = $count
            UniqueCount = $uniqueValues.Count
            DuplicateCount = $count - $uniqueValues.Count
        }
    }
    finally {
        $document.Dispose()
    }
}

$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $resolvedManifestPath) '..\..'))
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json

if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported BetterGI import manifest schema: $($manifest.schemaVersion)"
}

$resolvedSource = (Resolve-Path -LiteralPath $Source).Path
$temporaryDirectory = $null

try {
    if (Test-Path -LiteralPath $resolvedSource -PathType Container) {
        $sourceRoot = $resolvedSource
    }
    elseif (Test-Path -LiteralPath $resolvedSource -PathType Leaf) {
        if ([IO.Path]::GetExtension($resolvedSource) -notin @('.7z', '.zip')) {
            throw 'The source file must be a BetterGI .7z or .zip archive.'
        }

        if (-not [string]::IsNullOrWhiteSpace($manifest.runtimeArtifact.sha256)) {
            $artifactHash = (Get-FileHash -LiteralPath $resolvedSource -Algorithm SHA256).Hash
            if (-not $artifactHash.Equals($manifest.runtimeArtifact.sha256, [StringComparison]::OrdinalIgnoreCase)) {
                throw "BetterGI artifact hash mismatch. Expected $($manifest.runtimeArtifact.sha256), got $artifactHash."
            }
        }

        $sevenZipPath = Get-SevenZipPath
        Assert-ArchiveEntriesAreSafe -SevenZipPath $sevenZipPath -ArchivePath $resolvedSource
        $temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) ("akasha-bettergi-import-" + [Guid]::NewGuid().ToString('N'))
        [void] (New-Item -ItemType Directory -Path $temporaryDirectory)

        $archiveRoot = $manifest.runtimeArtifact.archiveRoot
        if ([string]::IsNullOrWhiteSpace($archiveRoot)) {
            throw 'The runtime artifact manifest must declare archiveRoot.'
        }

        $archiveRootPath = Get-ContainedPath -Root $temporaryDirectory -RelativePath $archiveRoot
        $sourcePaths = @($manifest.assets | ForEach-Object {
            ($archiveRoot.TrimEnd('/', '\') + '/' + $_.sourcePath).Replace('/', '\')
        })
        $arguments = @('x', '-y', "-o$temporaryDirectory", '--', $resolvedSource) + $sourcePaths
        & $sevenZipPath @arguments | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "7-Zip could not extract the declared BetterGI assets from '$resolvedSource'."
        }

        $sourceRoot = $archiveRootPath
    }
    else {
        throw "BetterGI import source does not exist: $Source"
    }

    Assert-NoReparsePoint -Root $sourceRoot -Path $sourceRoot

    $summary = [ordered]@{
        Added = 0
        Modified = 0
        Unchanged = 0
        Removed = 0
        DuplicateEntries = 0
        UndeclaredTargetFiles = 0
    }
    $declaredTargets = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

    foreach ($asset in $manifest.assets) {
        $sourcePath = Get-ContainedPath -Root $sourceRoot -RelativePath $asset.sourcePath
        $targetPath = Get-ContainedPath -Root $repositoryRoot -RelativePath $asset.targetPath
        [void] $declaredTargets.Add([IO.Path]::GetFullPath($targetPath))

        if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            throw "Declared BetterGI asset is missing from the source: $($asset.sourcePath)"
        }

        Assert-NoReparsePoint -Root $sourceRoot -Path $sourcePath
        $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash
        if (-not $sourceHash.Equals($asset.sha256, [StringComparison]::OrdinalIgnoreCase)) {
            throw "BetterGI asset hash mismatch for '$($asset.sourcePath)'. Expected $($asset.sha256), got $sourceHash."
        }

        if ($asset.kind -eq 'json-string-list') {
            $stats = Get-JsonStringListStats -Path $sourcePath
            if ($stats.Count -ne $asset.count -or $stats.UniqueCount -ne $asset.uniqueCount) {
                throw "BetterGI list statistics changed for '$($asset.sourcePath)'. Expected $($asset.count)/$($asset.uniqueCount), got $($stats.Count)/$($stats.UniqueCount)."
            }

            $summary.DuplicateEntries += $stats.DuplicateCount
        }

        $targetExists = Test-Path -LiteralPath $targetPath -PathType Leaf
        if ($targetExists) {
            Assert-NoReparsePoint -Root $repositoryRoot -Path $targetPath
        }

        $targetMatches = $targetExists -and ((Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.Equals($sourceHash, [StringComparison]::OrdinalIgnoreCase))

        if ($targetMatches) {
            $summary.Unchanged++
            continue
        }

        if ($VerifyOnly) {
            if ($targetExists) {
                throw "Imported target has been modified: $($asset.targetPath)"
            }

            throw "Imported target is missing: $($asset.targetPath)"
        }

        $targetDirectory = Split-Path -Parent $targetPath
        [void] (New-Item -ItemType Directory -Path $targetDirectory -Force)
        Assert-NoReparsePoint -Root $repositoryRoot -Path $targetDirectory
        [IO.File]::WriteAllBytes($targetPath, [IO.File]::ReadAllBytes($sourcePath))

        if ($targetExists) {
            $summary.Modified++
        }
        else {
            $summary.Added++
        }
    }

    $managedAssetRoots = @(
        'src\AkashaAutomation.BetterGiPort\Assets\Config',
        'src\AkashaAutomation.BetterGiPort\Assets\Recognition',
        'src\AkashaAutomation.BetterGiPort\Assets\Model'
    )
    $undeclaredTargetFiles = [Collections.Generic.List[string]]::new()

    foreach ($relativeAssetRoot in $managedAssetRoots) {
        $assetRoot = Get-ContainedPath -Root $repositoryRoot -RelativePath $relativeAssetRoot
        if (-not (Test-Path -LiteralPath $assetRoot -PathType Container)) {
            continue
        }

        Assert-NoReparsePoint -Root $repositoryRoot -Path $assetRoot
        foreach ($targetFile in Get-ChildItem -LiteralPath $assetRoot -File -Recurse) {
            Assert-NoReparsePoint -Root $repositoryRoot -Path $targetFile.FullName
            if (-not $declaredTargets.Contains($targetFile.FullName)) {
                $summary.UndeclaredTargetFiles++
                [void] $undeclaredTargetFiles.Add($targetFile.FullName)
            }
        }
    }

    if ($undeclaredTargetFiles.Count -gt 0) {
        $relativeUndeclaredFiles = @($undeclaredTargetFiles | ForEach-Object {
            [IO.Path]::GetRelativePath($repositoryRoot, $_).Replace('\', '/')
        })

        if ($VerifyOnly) {
            throw "Managed BetterGI asset directories contain files not declared by the manifest: $($relativeUndeclaredFiles -join ', ')"
        }

        foreach ($undeclaredTargetFile in $undeclaredTargetFiles) {
            Remove-Item -LiteralPath $undeclaredTargetFile -Force
            $summary.Removed++
        }
    }

    [pscustomobject] $summary
}
finally {
    if ($null -ne $temporaryDirectory -and (Test-Path -LiteralPath $temporaryDirectory)) {
        Remove-Item -LiteralPath $temporaryDirectory -Recurse -Force
    }
}

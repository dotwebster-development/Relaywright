param(
    [string]$SitePath = "site",
    [string]$ExpectedStableVersion = $(if ($env:RELAYWRIGHT_SITE_EXPECTED_VERSION) { $env:RELAYWRIGHT_SITE_EXPECTED_VERSION } else { "1.0.2" })
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Resolve-RepoPath {
    param([string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Get-UInt32BigEndian {
    param(
        [byte[]]$Bytes,
        [int]$Offset
    )

    return [uint32](
        ([uint32]$Bytes[$Offset] -shl 24) -bor
        ([uint32]$Bytes[$Offset + 1] -shl 16) -bor
        ([uint32]$Bytes[$Offset + 2] -shl 8) -bor
        [uint32]$Bytes[$Offset + 3])
}

function Test-PngImage {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $signature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)

    if ($bytes.Length -lt 33) {
        throw "PNG is too short: $Path"
    }

    for ($i = 0; $i -lt $signature.Length; $i++) {
        if ($bytes[$i] -ne $signature[$i]) {
            $actual = (($bytes[0..7] | ForEach-Object { "{0:X2}" -f $_ }) -join " ")
            throw "Invalid PNG signature for $Path. Actual: $actual"
        }
    }

    $offset = 8
    $sawIhdr = $false
    $sawIend = $false
    $width = 0
    $height = 0

    while ($offset + 12 -le $bytes.Length) {
        $length = [int](Get-UInt32BigEndian -Bytes $bytes -Offset $offset)
        $type = [System.Text.Encoding]::ASCII.GetString($bytes, $offset + 4, 4)
        $dataOffset = $offset + 8
        $nextOffset = $dataOffset + $length + 4

        if ($nextOffset -gt $bytes.Length) {
            throw "PNG chunk $type exceeds file length in $Path"
        }

        if (-not $sawIhdr) {
            if ($type -ne "IHDR" -or $length -ne 13) {
                throw "PNG first chunk must be IHDR with length 13 in $Path"
            }

            $width = [int](Get-UInt32BigEndian -Bytes $bytes -Offset $dataOffset)
            $height = [int](Get-UInt32BigEndian -Bytes $bytes -Offset ($dataOffset + 4))
            if ($width -le 0 -or $height -le 0) {
                throw "PNG has invalid dimensions in $Path"
            }

            $sawIhdr = $true
        }

        if ($type -eq "IEND") {
            $sawIend = $true
            break
        }

        $offset = $nextOffset
    }

    if (-not $sawIhdr -or -not $sawIend) {
        throw "PNG is missing required IHDR or IEND chunks: $Path"
    }

    Write-Host "Validated PNG $Path ($width x $height)"
}

function Test-SvgImage {
    param([string]$Path)

    $xml = New-Object System.Xml.XmlDocument
    $xml.PreserveWhitespace = $true
    $xml.Load($Path)

    if ($xml.DocumentElement.LocalName -ne "svg") {
        throw "SVG root element is not svg: $Path"
    }

    Write-Host "Validated SVG $Path"
}

function Assert-FileExists {
    param(
        [string]$Root,
        [string]$Reference
    )

    $relative = ($Reference -replace "[?#].*$", "") -replace "/", [System.IO.Path]::DirectorySeparatorChar
    $path = [System.IO.Path]::GetFullPath((Join-Path $Root $relative))

    if (-not $path.StartsWith($Root, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Reference escapes site root: $Reference"
    }

    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Missing site reference: $Reference -> $path"
    }

    return $path
}

$siteRoot = Resolve-RepoPath -Path $SitePath
$indexPath = Join-Path $siteRoot "index.html"

if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
    throw "Missing site index: $indexPath"
}

$html = Get-Content -LiteralPath $indexPath -Raw

foreach ($required in @(
    '<link rel="canonical"',
    'property="og:title"',
    'property="og:image"',
    'name="twitter:card"',
    'type="application/ld+json"',
    "v$ExpectedStableVersion"
)) {
    if ($html -notlike "*$required*") {
        throw "Missing required website marker: $required"
    }
}

$jsonLdMatch = [regex]::Match($html, '(?is)<script\s+type="application/ld\+json">\s*(.*?)\s*</script>')
if (-not $jsonLdMatch.Success) {
    throw "Missing JSON-LD script block."
}

$jsonLd = $jsonLdMatch.Groups[1].Value | ConvertFrom-Json
if ($jsonLd.softwareVersion -ne $ExpectedStableVersion) {
    throw "JSON-LD softwareVersion '$($jsonLd.softwareVersion)' does not match expected '$ExpectedStableVersion'."
}

$idMatches = [regex]::Matches($html, '\sid="([^"]+)"')
$ids = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
foreach ($match in $idMatches) {
    $id = $match.Groups[1].Value
    if (-not $ids.Add($id)) {
        throw "Duplicate id in index.html: $id"
    }
}

$anchorMatches = [regex]::Matches($html, 'href="#([^"]+)"')
foreach ($match in $anchorMatches) {
    $anchor = $match.Groups[1].Value
    if (-not $ids.Contains($anchor)) {
        throw "Anchor target not found: #$anchor"
    }
}

$imageMatches = [regex]::Matches($html, '(?is)<img\b[^>]*>')
foreach ($match in $imageMatches) {
    $tag = $match.Value
    if ($tag -notmatch '\salt="[^"]+"') {
        throw "Image is missing alt text: $tag"
    }
}

$referenceMatches = [regex]::Matches($html, '(?i)\b(?:src|href)="([^"]+)"')
$validatedFiles = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

foreach ($match in $referenceMatches) {
    $reference = $match.Groups[1].Value

    if ($reference -match '^(https?:|mailto:|#|/)' -or [string]::IsNullOrWhiteSpace($reference)) {
        continue
    }

    $path = Assert-FileExists -Root $siteRoot -Reference $reference
    [void]$validatedFiles.Add($path)
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $siteRoot ".."))
$repoLinkMatches = [regex]::Matches($html, 'https://github\.com/dotwebster-development/Relaywright/blob/main/([^"#?]+)')
foreach ($match in $repoLinkMatches) {
    $repoRelative = [System.Uri]::UnescapeDataString($match.Groups[1].Value) -replace "/", [System.IO.Path]::DirectorySeparatorChar
    $repoPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $repoRelative))

    if (-not $repoPath.StartsWith($repoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Repository link escapes repository root: $repoRelative"
    }

    if (-not (Test-Path -LiteralPath $repoPath -PathType Leaf)) {
        throw "Repository blob link target does not exist locally: $repoRelative"
    }
}

foreach ($file in $validatedFiles) {
    $extension = [System.IO.Path]::GetExtension($file).ToLowerInvariant()
    switch ($extension) {
        ".png" { Test-PngImage -Path $file }
        ".svg" { Test-SvgImage -Path $file }
    }
}

Write-Host "Pages site validation passed for $siteRoot"

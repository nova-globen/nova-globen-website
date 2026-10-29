#requires -Version 7
<#
.SYNOPSIS
    Makes the 800-pixel-wide WebP variant of every article cover (content/articles/<slug>/cover-800.webp) from its
    cover.png, for phones: the article page and the cards offer it in srcset next to the 1200-pixel cover.webp, and a
    phone shows the cover at about 380 CSS pixels (Lighthouse: the cover is the page's largest paint).
    Uses ffmpeg's libwebp encoder; ffmpeg is a tool on the author's machine, not part of the site. Existing variants are
    kept unless -Force. The content tests fail when a cover.webp has no variant, so a new article's cover gets one here.

.EXAMPLE
    pwsh scripts/content/cover-variants.ps1
#>
param(
    [switch] $Force,
    [int] $Width = 800,
    [int] $Quality = 80
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw 'ffmpeg is not on PATH (winget install Gyan.FFmpeg).' }

$made = 0
foreach ($webp in Get-ChildItem (Join-Path $root 'content/articles') -Recurse -Filter cover.webp)
{
    $png = Join-Path $webp.DirectoryName 'cover.png'
    $out = Join-Path $webp.DirectoryName "cover-$Width.webp"
    if (-not (Test-Path $png)) { throw "No cover.png next to $($webp.FullName)" }
    if ((Test-Path $out) -and -not $Force) { continue }
    ffmpeg -loglevel error -y -i $png -vf "scale=${Width}:-2:flags=lanczos" -c:v libwebp -quality $Quality -compression_level 6 $out
    if ($LASTEXITCODE -ne 0) { throw "ffmpeg failed on $png" }
    $made++
    '{0,-90} {1,7:N0} KB' -f $out.Substring($root.Length + 1), ((Get-Item $out).Length / 1KB)
}
"Made $made variant(s)."

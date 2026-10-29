#requires -Version 7
<#
.SYNOPSIS
    Measures every text/background colour pair of the Nova Globen theme with the WCAG 2.x contrast formula and fails
    when one is below its threshold (4.5:1 for text, 3:1 for large text and for non-text UI such
    as focus rings and borders that carry meaning).

.DESCRIPTION
    The colours are READ from the theme's stylesheet (its light :root block and its dark one inside
    @media (prefers-color-scheme: dark)), not restated here, and every pair is measured in both palettes,
    so a token change cannot pass unmeasured. A pair names its two colours by custom property (--name) or as a
    literal #RRGGBB. Prints the table it measured.

.EXAMPLE
    pwsh scripts/check-contrast.ps1
#>
param(
    [string] $Css = (Join-Path (Split-Path -Parent $PSScriptRoot) 'NovaGloben.Website/Client/wwwroot/Themes/NovaGloben.Website.Client.Themes.NovaGloben/theme.css')
)

$ErrorActionPreference = 'Stop'

# Foreground, background, minimum ratio, where the pair is used.
$Pairs = @(
    @('--db-text',        '--db-bg',          4.5, 'body text'),
    @('--db-text',        '--db-bg-elev-2',   4.5, 'body text on soft sections'),
    @('--db-text-2',      '--db-bg',          4.5, 'secondary text'),
    @('--db-text-2',      '--db-bg-elev-2',   4.5, 'secondary text on soft sections'),
    @('--db-muted',       '--db-bg',          4.5, 'muted text'),
    @('--db-muted',       '--db-bg-elev-2',   4.5, 'muted text on soft sections'),
    @('--ng-link',        '--db-bg',          4.5, 'links'),
    @('--ng-link',        '--db-bg-elev-2',   4.5, 'links on soft sections'),
    @('--ng-link-hover',  '--db-bg',          4.5, 'links, hover'),
    @('--db-on-accent',   '--db-accent',      4.5, 'primary button'),
    @('--db-on-accent',   '--db-accent-2',    4.5, 'primary button, hover'),
    @('--db-on-accent',   '--db-accent-3',    4.5, 'primary button, pressed'),
    @('--db-accent',      '--db-bg',          4.5, 'outline button text, eyebrow labels'),
    @('--db-accent',      '--db-bg-elev-2',   4.5, 'outline button text on soft sections'),
    @('--ng-focus',       '--db-bg',          3.0, 'focus ring (non-text)'),
    @('--ng-focus',       '--db-bg-elev-2',   3.0, 'focus ring on soft sections (non-text)'),
    @('--ng-footer-text', '--ng-footer-bg',   4.5, 'footer text'),
    @('--ng-footer-head', '--ng-footer-bg',   4.5, 'footer headings'),
    @('--ng-footer-link', '--ng-footer-bg',   4.5, 'footer links'),
    @('--ng-footer-focus','--ng-footer-bg',   4.5, 'footer links, hover; focus ring in the footer'),
    @('--db-ok',          '--db-bg',          4.5, 'success messages'),
    @('--db-warn',        '--db-bg',          4.5, 'warning messages'),
    @('--db-bad',         '--db-bg',          4.5, 'error messages'),
    @('--db-info',        '--db-bg',          4.5, 'information messages'),
    @('--db-border-strong','--db-bg',         3.0, 'form field borders (non-text)'),
    @('--db-text-2',      '--db-bg-elev-3',   4.5, 'badges and inline code'),
    @('--db-accent',      '--db-bg-elev-3',   3.0, 'card icons (non-text)'),
    @('--db-text',        '--db-bg-elev',     4.5, 'text on cards and the header'),
    @('--db-text-2',      '--db-bg-elev',     4.5, 'secondary text on cards'),
    @('--db-muted',       '--db-bg-elev',     4.5, 'muted text on cards'),
    @('--ng-link',        '--db-bg-elev',     4.5, 'links on cards'),
    @('--db-accent',      '--db-bg-elev',     3.0, 'card hover border (non-text)'),
    @('--ng-brand',       '--ng-accent-soft', 3.0, 'icons in their tinted wells (non-text)'),
    @('--db-text',        '--ng-accent-soft', 4.5, 'text on accent badges'),
    @('--db-bg',          '--db-text',        4.5, 'the selected blog topic'),
    @('--db-border-strong','--db-bg-elev',    3.0, 'form field borders on cards (non-text)'),
    @('--ng-ok-text',     '--ng-ok-bg',       4.5, 'form: success message'),
    @('--ng-error-text',  '--ng-error-bg',    4.5, 'form: error message'),
    @('--db-ok',          '--ng-ok-bg',       3.0, 'form: success border (non-text)'),
    @('--db-bad',         '--ng-error-bg',    3.0, 'form: error border (non-text)'),
    @('--ng-code-text',      '--ng-code-bg',  4.5, 'code'),
    @('--ng-code-keyword',   '--ng-code-bg',  4.5, 'code: keywords'),
    @('--ng-code-comment',   '--ng-code-bg',  4.5, 'code: comments'),
    @('--ng-code-string',    '--ng-code-bg',  4.5, 'code: strings'),
    @('--ng-code-type',      '--ng-code-bg',  4.5, 'code: types and element names'),
    @('--ng-code-number',    '--ng-code-bg',  4.5, 'code: numbers'),
    @('--ng-code-attribute', '--ng-code-bg',  4.5, 'code: attributes')
)

# Two palettes: the light one is the first :root block; the dark one is the :root inside
# @media (prefers-color-scheme: dark), laid over the light one (a token it does not redefine keeps its light value).
$text = Get-Content -Raw -LiteralPath $Css
function Read-Tokens([string] $block)
{
    $found = @{}
    foreach ($m in [regex]::Matches($block, '(--[a-z0-9-]+)\s*:\s*(#[0-9A-Fa-f]{6})\b')) { $found[$m.Groups[1].Value] = $m.Groups[2].Value }
    return $found
}
$lightBlock = [regex]::Match($text, '(?s):root\s*\{(.*?)\}').Groups[1].Value
if (-not $lightBlock) { throw "No :root block in $Css" }
$darkBlock = [regex]::Match($text, '(?s)@media\s*\(prefers-color-scheme:\s*dark\)\s*\{\s*:root\s*\{(.*?)\}').Groups[1].Value
if (-not $darkBlock) { throw "No dark palette (:root inside @media (prefers-color-scheme: dark)) in $Css" }
$light = Read-Tokens $lightBlock
$dark = @{} + $light
(Read-Tokens $darkBlock).GetEnumerator() | ForEach-Object { $dark[$_.Key] = $_.Value }
$tokens = $light

function Resolve-Colour([string] $name)
{
    if ($name.StartsWith('#')) { return $name }
    if (-not $tokens.ContainsKey($name)) { throw "Token $name is not a #RRGGBB value in the :root block of $Css" }
    return $tokens[$name]
}

function Get-Luminance([string] $hex)
{
    $channels = 1, 3, 5 | ForEach-Object {
        $c = [Convert]::ToInt32($hex.Substring($_, 2), 16) / 255.0
        if ($c -le 0.04045) { $c / 12.92 } else { [math]::Pow(($c + 0.055) / 1.055, 2.4) }
    }
    return 0.2126 * $channels[0] + 0.7152 * $channels[1] + 0.0722 * $channels[2]
}

$failed = 0
$rows = foreach ($palette in 'light', 'dark') { $tokens = if ($palette -eq 'light') { $light } else { $dark }; foreach ($p in $Pairs)
{
    $fg = Resolve-Colour $p[0]; $bg = Resolve-Colour $p[1]
    $l1 = Get-Luminance $fg; $l2 = Get-Luminance $bg
    $ratio = ([math]::Max($l1, $l2) + 0.05) / ([math]::Min($l1, $l2) + 0.05)
    $ok = [math]::Round($ratio, 2) -ge $p[2]
    if (-not $ok) { $failed++ }
    [pscustomobject]@{ Mode = $palette; Pair = "$($p[0]) $fg on $($p[1]) $bg"; Ratio = '{0:n2}:1' -f $ratio; Needs = "$($p[2]):1"; Result = $(if ($ok) { 'pass' } else { 'FAIL' }); Use = $p[3] }
} }
$rows | Format-Table -AutoSize | Out-String -Width 220 | Write-Host

if ($failed -gt 0)
{
    Write-Host "Contrast gate: FAILED - $failed pair(s) below their threshold." -ForegroundColor Red
    exit 1
}
Write-Host "Contrast gate: PASSED ($($Pairs.Count) pairs in each of 2 palettes measured from $([IO.Path]::GetFileName($Css)))." -ForegroundColor Green
exit 0

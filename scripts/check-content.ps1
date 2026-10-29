#requires -Version 7
<#
.SYNOPSIS
    The content rules as a gate: fails on any placeholder or forbidden marker in what the sites
    publish.

.DESCRIPTION
    Scans two things:
      - the content sources in the repository (the folders in $SourceRoots: pages, articles, site templates, themes);
      - with -BaseUrl, the rendered pages of a running instance: every path in -Paths, plus every path its
        /pages/sitemap.xml lists when the instance serves one.
    A finding is a marker from the table below, with the file or URL and the line. Exit 1 on any finding.

    A marker that is legitimate in one place (for example "free" in an article about memory) is allowed by adding the
    exact file or URL and marker id to $Allowed, with the reason. Nothing is allowed by default.

.EXAMPLE
    pwsh scripts/check-content.ps1
    pwsh scripts/check-content.ps1 -BaseUrl http://localhost:43497 -Paths /,/sv/
#>
param(
    [string] $BaseUrl,
    [string[]] $Paths = @('/'),
    [string] $Root = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'

# Folders whose files the sites publish. Each is scanned when it exists; more join as the phases add them.
$SourceRoots = @(
    'content',
    'NovaGloben.Website/Server/SiteTemplates',
    'NovaGloben.Website/Server/Modules',
    'NovaGloben.Website/Client/Themes',
    'NovaGloben.Website/Client/wwwroot',
    'NovaGloben.Website/Client/Modules'
)
$SourceExtensions = '.md', '.html', '.razor', '.cshtml', '.json', '.txt', '.cs', '.css'

# id = regex (case-insensitive). Every marker is one of the content rules.
$Markers = [ordered]@{
    'lorem'            = '\blorem\b|\bipsum dolor\b'
    'coming-soon'      = '\bcoming soon\b|\bunder construction\b'
    'todo'             = '\bTODO\b|\bTBD\b|\bFIXME\b|\bXXX\b'
    'placeholder-text' = '\bplaceholder text\b|\byour (company|name|logo) here\b|\[\\?\[?your [^\]]{1,40}\]|\bINSERT [A-Z ]+ HERE\b'
    'empty-href'       = 'href\s*=\s*"#"|href\s*=\s*''#'''
    'example-domain'   = '\bexample\.(com|org|net)\b'
    'open-source'      = '\bopen[- ]source\b'
    'community-ed'     = '\bcommunity edition\b|\bclone (our|the) repo'
    'superlative'      = '\b(fastest|best-in-class|world-class|unlimited|bank-grade|military-grade|industry-leading)\b'
    'uptime-number'    = '\b99\.9+\s*%'
    'testimonial'      = '\btestimonial|\btrusted by\b'
    'certification'    = '\bISO ?27001\b|\bSOC ?2\b|\bGDPR[- ]compliant\b|\bWCAG[- ](conformant|compliant)\b|\bHIPAA[- ]compliant\b'
    'mysql'            = '\bMySQL\b'
    'internal-host'    = '\b192\.168\.\d{1,3}\.\d{1,3}\b'
    'install-steps'    = '\bdotnet new install\b'
}

# The markers that name private hosts and feeds live with the private documentation; the published tree carries no such name, so it runs without them.
$privateMarkers = Join-Path $Root 'docs/content-markers.ps1'
if (Test-Path $privateMarkers)
{
    $extra = & $privateMarkers
    foreach ($id in $extra.Keys) { $Markers[$id] = $extra[$id] }
}

# "<file or page path>|<marker id>" = reason. Keep it short and specific. A rendered page is named by its path, so an entry
# holds on every host. Each entry was read where it stands (2026-10-23): technical content of an article, not a claim of ours.
$Allowed = [ordered]@{
    'content/articles/contract-stability-and-versioning-dotnet-core-azure/index.md|example-domain' = 'john@example.com in code samples: the reserved documentation domain (RFC 2606)'
    '/blog/contract-stability-and-versioning-dotnet-core-azure|example-domain'                     = 'the same, rendered'
    'content/articles/resilient-http-calls-in-dotnet/index.md|example-domain'                      = 'api.example.com in a code sample: the reserved documentation domain'
    '/blog/resilient-http-calls-in-dotnet|example-domain'                                          = 'the same, rendered'
    'content/articles/modeling-contracts-dtos-entities-value-objects-best-practices-dotnet-core/index.md|superlative' = 'compares third-party mapping libraries and quotes one project''s own "tiniest and fastest"'
    '/blog/modeling-contracts-dtos-entities-value-objects-best-practices-dotnet-core|superlative'                     = 'the same, rendered'
    'content/articles/resilience-and-circuit-breakers-dotnet-core-azure/index.md|superlative'      = 'a code comment ("memory cache first (fastest)") about cache tiers'
    '/blog/resilience-and-circuit-breakers-dotnet-core-azure|superlative'                          = 'the same, rendered'
    'content/articles/distributed-tracing-dotnet-core-azure/index.md|open-source'                  = 'describes Jaeger, a third-party open-source project'
    '/blog/distributed-tracing-dotnet-core-azure|open-source'                                      = 'the same, rendered'
    # Vacha is free and open source under GPL-3.0-or-later. The rule against "open source"
    # was written for Stomme, which is not; it still holds on every other page.
    'content/sites/company-en/pages/product-vacha.html|open-source'         = 'Vacha is GPL-3.0-or-later (R-17)'
    'content/sites/company-en/pages/product-vacha-pricing.html|open-source' = 'Vacha is GPL-3.0-or-later (R-17)'
    '/products/vacha|open-source'                                            = 'the same, rendered'
    '/products/vacha/pricing|open-source'                                    = 'the same, rendered'
}

# Source files where a marker is about code, not published copy: the Stomme module and theme generator templates.
$SkipSource = '[\\/](bin|obj|node_modules|Templates)[\\/]'

$findings = [System.Collections.Generic.List[object]]::new()

function Test-Text([string] $where, [string] $text, [bool] $isHtml)
{
    if ($isHtml)
    {
        # Scripts, styles and comments are not published copy; attributes stay (href="#" lives there).
        $text = [regex]::Replace($text, '(?s)<script\b[^>]*>.*?</script>|<style\b[^>]*>.*?</style>|<!--.*?-->', '')
    }
    $lines = $text -split "`r?`n"
    for ($i = 0; $i -lt $lines.Count; $i++)
    {
        foreach ($id in $Markers.Keys)
        {
            if ($lines[$i] -match $Markers[$id] -and -not $Allowed.Contains("$where|$id"))
            {
                $snippet = $lines[$i].Trim()
                if ($snippet.Length -gt 140) { $snippet = $snippet.Substring(0, 140) + '...' }
                $findings.Add([pscustomobject]@{ Where = "${where}:$($i + 1)"; Marker = $id; Text = $snippet })
            }
        }
    }
}

$sourceCount = 0
foreach ($relative in $SourceRoots)
{
    $folder = Join-Path $Root $relative
    if (-not (Test-Path $folder)) { continue }
    foreach ($file in Get-ChildItem $folder -Recurse -File | Where-Object { $_.Extension -in $SourceExtensions -and $_.FullName -notmatch $SkipSource })
    {
        $sourceCount++
        $rel = $file.FullName.Substring($Root.Length + 1).Replace('\', '/')
        Test-Text $rel (Get-Content -Raw -LiteralPath $file.FullName) ($file.Extension -in '.html', '.razor', '.cshtml')
    }
}

$pageCount = 0
if ($BaseUrl)
{
    $base = [Uri]($BaseUrl.TrimEnd('/') + '/')
    # "-Paths /,/sv/" arrives as one string when the script is started with pwsh -File; split it.
    $toScan = [System.Collections.Generic.List[string]]::new([string[]]($Paths | ForEach-Object { $_ -split ',' } | Where-Object { $_ }))
    $sitemap = Invoke-WebRequest ([Uri]::new($base, 'sitemap.xml')) -SkipHttpErrorCheck
    if ($sitemap.StatusCode -eq 200 -and [string]$sitemap.Headers['Content-Type'] -match 'xml')
    {
        foreach ($m in [regex]::Matches($sitemap.Content, '<loc>([^<]+)</loc>'))
        {
            $path = ([Uri]$m.Groups[1].Value).PathAndQuery
            if (-not $toScan.Contains($path)) { $toScan.Add($path) }
        }
    }
    foreach ($path in $toScan)
    {
        $url = [Uri]::new($base, $path.TrimStart('/')).AbsoluteUri
        $response = Invoke-WebRequest $url -SkipHttpErrorCheck
        if ($response.StatusCode -ne 200)
        {
            $findings.Add([pscustomobject]@{ Where = $path; Marker = 'status'; Text = "HTTP $($response.StatusCode)" })
            continue
        }
        $pageCount++
        Test-Text $path $response.Content $true
    }
}

if ($findings.Count -gt 0)
{
    Write-Host "Content gate: FAILED - $($findings.Count) finding(s):" -ForegroundColor Red
    $findings | ForEach-Object { Write-Host ("  x [{0}] {1}  {2}" -f $_.Marker, $_.Where, $_.Text) -ForegroundColor Red }
    exit 1
}

Write-Host "Content gate: PASSED ($sourceCount source file(s), $pageCount rendered page(s), $($Markers.Count) markers)." -ForegroundColor Green
exit 0

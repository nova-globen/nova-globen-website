<#
.SYNOPSIS
    The names check: no file of the published tree names an internal host or an old product name. Exits 1 on any hit and lists each.
.DESCRIPTION
    Reads every file git would publish: the tracked files, less those .gitattributes marks export-ignore. The old names
    are matched at the start of a word, case-insensitively, so ordinary words that merely contain the letters (an
    article's "candidates") pass. The patterns are built from parts, so this file does not match itself.
.EXAMPLE
    pwsh scripts/check-names.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$old = 'di' + 'da'
$Patterns = [ordered]@{
    'uat-host'  = '(?i)' + $old + 'bit\.com'
    'old-names' = '(?i)\b' + $old
}

Push-Location $root
try
{
    $published = @(git -c core.quotePath=false ls-files -- . ':(exclude,attr:export-ignore)' |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf })

    $hits = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $published)
    {
        $lines = @(Get-Content -LiteralPath $file -ErrorAction SilentlyContinue)
        for ($i = 0; $i -lt $lines.Count; $i++)
        {
            foreach ($rule in $Patterns.GetEnumerator())
            {
                if ($lines[$i] -match $rule.Value)
                {
                    $text = $lines[$i].Trim()
                    if ($text.Length -gt 140) { $text = $text.Substring(0, 140) + '...' }
                    $hits.Add(('{0}:{1} [{2}] {3}' -f $file, ($i + 1), $rule.Key, $text))
                }
            }
        }
    }
}
finally
{
    Pop-Location
}

if ($hits.Count -gt 0)
{
    $hits | ForEach-Object { Write-Host "  $_" }
    Write-Host "Names check: FAILED - $($hits.Count) line(s) in the published tree ($($published.Count) files)." -ForegroundColor Red
    exit 1
}
Write-Host "Names check: PASSED ($($published.Count) published files)." -ForegroundColor Green

#requires -Version 7
<#
.SYNOPSIS
    The gate every commit passes: agent instructions in sync, the names check, the build, the tests, the licence
    gate, the content gate and the theme's contrast gate. Stops at the first red step and exits 1.
    -DocsOnly is the gate of a commit that touches only documentation: agent instructions and names, nothing built.

.EXAMPLE
    pwsh scripts/gate.ps1
    pwsh scripts/gate.ps1 -DocsOnly
    pwsh scripts/gate.ps1 -BaseUrl http://localhost:43497 -Paths /,/sv/   # also scan a running instance's pages
#>
param(
    [string] $BaseUrl,
    [string[]] $Paths = @('/'),
    [switch] $DocsOnly
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'NovaGloben.Website/NovaGloben.Website.slnx'
$started = Get-Date
$results = [System.Collections.Generic.List[string]]::new()

function Step([string] $name, [scriptblock] $action)
{
    Write-Host "`n== $name" -ForegroundColor Cyan
    $t = Get-Date
    & $action
    if ($LASTEXITCODE -ne 0)
    {
        Write-Host "`nGATE RED at '$name' (exit $LASTEXITCODE)." -ForegroundColor Red
        exit 1
    }
    $results.Add(('{0,-22} green  {1,5:n0}s' -f $name, ((Get-Date) - $t).TotalSeconds))
}

Push-Location $root
try
{
    # The agent instructions are kept in sync where they are kept (.agent/); the published tree carries none.
    if (Test-Path (Join-Path $root '.agent/agent.yaml'))
    {
        Step 'agent instructions' { dotnet agent status --fail-on-drift }
    }

    Step 'names' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-names.ps1') }

    if ($DocsOnly)
    {
        Write-Host "`n== documentation only: nothing to build or test" -ForegroundColor Cyan
        $results.Add('build and tests        skipped (documentation only)')
    }
}
finally
{
    Pop-Location
}

if (-not $DocsOnly)
{
Push-Location $root
try
{
    # A red build stops the gate before the tests: tests after a failed build run the previous assemblies and report
    # a vacuous green (Stomme measured it, 2026-09-22).
    Step 'build' { dotnet build $solution -c Release "-m:4" -nologo -clp:ErrorsOnly }

    # The Didin app (apps/didin) is a MAUI project outside the solution: it is built when a commit touches it or the
    # module code it compiles (the UI and the models), on a machine with the MAUI workloads. Its Windows target is
    # the quick one; the public repository's apps.yml builds Android as well.
    $staged = @(git diff --cached --name-only 2>$null)
    $appTouched = $staged | Where-Object { $_ -match '^(apps/|NovaGloben\.Website/Modules/Didin/(Ui|Shared)/)' }
    if ($appTouched -and (dotnet workload list 2>$null | Select-String -Quiet 'maui-windows'))
    {
        Step 'app (Windows)' { dotnet build (Join-Path $root 'apps/didin/Didin.App/Didin.App.csproj') -f net10.0-windows10.0.19041.0 -c Release -nologo -clp:ErrorsOnly }
    }

    $testProjects = @(Get-ChildItem (Join-Path $root 'NovaGloben.Website') -Recurse -Filter '*.Tests.csproj' |
        Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' })
    if ($testProjects.Count -eq 0)
    {
        Write-Host "`n== tests`nno test project yet" -ForegroundColor Yellow
        $results.Add('tests                  none yet')
    }
    else
    {
        foreach ($project in $testProjects)
        {
            Step "tests: $($project.BaseName)" { dotnet test $project.FullName -c Release --no-build -nologo }
        }
    }

    Step 'licences' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-licenses.ps1') }

    $contentArgs = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'check-content.ps1'))
    if ($BaseUrl) { $contentArgs += @('-BaseUrl', $BaseUrl, '-Paths', ($Paths -join ',')) }
    Step 'content' { pwsh @contentArgs }

    Step 'contrast' { pwsh -NoProfile -File (Join-Path $PSScriptRoot 'check-contrast.ps1') }
}
finally
{
    Pop-Location
    dotnet build-server shutdown *> $null
}
}

Write-Host "`nGATE GREEN in $([int]((Get-Date) - $started).TotalSeconds)s" -ForegroundColor Green
$results | ForEach-Object { Write-Host "  $_" }
exit 0

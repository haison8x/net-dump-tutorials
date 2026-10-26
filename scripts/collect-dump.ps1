<#
.SYNOPSIS
  Collects a dump from a lab container (docker compose service) into ./dumps.

.EXAMPLE
  ./scripts/collect-dump.ps1 memoryleak leak-event-1
  ./scripts/collect-dump.ps1 deadlock hang -Type Heap
#>
param(
    [Parameter(Mandatory)][string]$Service,
    [string]$Name = "$Service-$(Get-Date -Format yyyyMMdd-HHmmss)",
    [ValidateSet('Full', 'Heap', 'Mini', 'Triage')][string]$Type = 'Full'
)

$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    # The app is PID 1 inside its container (see docker/app.Dockerfile).
    docker compose exec $Service dotnet-dump collect --process-id 1 --type $Type --output "/dumps/$Name.dmp"
    Write-Host "Dump written to ./dumps/$Name.dmp"
}
finally {
    Pop-Location
}

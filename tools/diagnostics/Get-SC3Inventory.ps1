[CmdletBinding()]
param(
    [string] $Match = 'FIFINE|Mixer SC3|\bSC3\b',
    [switch] $PresentOnly,
    [switch] $Json,
    [switch] $IncludeAllAudioEndpoints
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SC3.Diagnostics.psm1') -Force

$inventory = Get-SC3Inventory -Match $Match -PresentOnly:$PresentOnly
if (-not $IncludeAllAudioEndpoints) {
    $inventory.PSObject.Properties.Remove('AllAudioEndpoints')
}

if ($Json) {
    $inventory | ConvertTo-Json -Depth 8
    return
}

$inventory

[CmdletBinding()]
param(
    [string] $Match = 'FIFINE|Mixer SC3|\bSC3\b',
    [ValidateRange(0.25, 60)] [double] $IntervalSeconds = 1,
    [ValidateRange(0, 86400)] [int] $DurationSeconds = 0,
    [switch] $ShowInitialState
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'SC3.Diagnostics.psm1') -Force

function ConvertTo-SC3Map {
    param([object[]] $Rows)
    $map = @{}
    foreach ($row in $Rows) { $map[$row.Key] = $row }
    return $map
}

function Write-SC3Event {
    param(
        [string] $Change,
        $Current,
        $Previous
    )
    $source = if ($null -ne $Current) { $Current } else { $Previous }
    [pscustomobject] [ordered]@{
        Time        = (Get-Date).ToString('o')
        Change      = $Change
        Kind        = $source.Kind
        Description = $source.Description
        Key         = $source.Key
        OldState    = if ($null -ne $Previous) { $Previous.State } else { $null }
        NewState    = if ($null -ne $Current) { $Current.State } else { $null }
    }
}

$started = Get-Date
$previous = @(Get-SC3Snapshot -Match $Match)
if ($ShowInitialState) {
    foreach ($row in $previous) { Write-SC3Event -Change 'Initial' -Current $row -Previous $null }
}

Write-Verbose "Monitorando alterações PnP e Core Audio. Use Ctrl+C para encerrar."
while ($DurationSeconds -eq 0 -or ((Get-Date) - $started).TotalSeconds -lt $DurationSeconds) {
    Start-Sleep -Milliseconds ([int]($IntervalSeconds * 1000))
    try {
        $current = @(Get-SC3Snapshot -Match $Match)
    }
    catch {
        Write-Warning "Falha transitória durante a leitura: $($_.Exception.Message)"
        continue
    }

    $oldMap = ConvertTo-SC3Map $previous
    $newMap = ConvertTo-SC3Map $current
    foreach ($key in $newMap.Keys) {
        if (-not $oldMap.ContainsKey($key)) {
            Write-SC3Event -Change 'Added' -Current $newMap[$key] -Previous $null
        }
        elseif ($newMap[$key].State -ne $oldMap[$key].State) {
            Write-SC3Event -Change 'Changed' -Current $newMap[$key] -Previous $oldMap[$key]
        }
    }
    foreach ($key in $oldMap.Keys) {
        if (-not $newMap.ContainsKey($key)) {
            Write-SC3Event -Change 'Removed' -Current $null -Previous $oldMap[$key]
        }
    }
    $previous = $current
}

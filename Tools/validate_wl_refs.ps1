# Quick WL prototype reference check (heuristic)
$root = Join-Path $PSScriptRoot '..'
$prototypeDir = Join-Path $root 'Resources\Prototypes'
$wlDir = Join-Path $prototypeDir '_WL'
$ids = [System.Collections.Generic.HashSet[string]]::new()
Get-ChildItem -Recurse $prototypeDir -Filter '*.yml' | ForEach-Object {
    Select-String -Path $_.FullName -Pattern '^\s+id:\s+["'']?([^"''\s]+)["'']?\s*$' | ForEach-Object { [void]$ids.Add($_.Matches.Groups[1].Value) }
}
$missing = [System.Collections.Generic.List[string]]::new()
Get-ChildItem -Recurse $wlDir -Filter '*.yml' | ForEach-Object {
    $file = $_.Name
    $content = Get-Content $_.FullName
    for ($i = 0; $i -lt $content.Count; $i++) {
        $line = $content[$i]
        if ($line -match '^\s+requiredNode:\s+(\S+)') {
            $id = $Matches[1]
            if (-not $ids.Contains($id)) { $missing.Add("$file`:$($i+1): requiredNode $id") }
        }
        if ($line -match '^\s+displayProto:\s+(\S+)') {
            $id = $Matches[1]
            if (-not $ids.Contains($id)) { $missing.Add("$file`:$($i+1): displayProto $id") }
        }
        if ($i -gt 0 -and $content[$i-1] -match '^\s+prerequisites:\s*$' -and $line -match '^\s+-\s+(\S+)') {
            $id = $Matches[1]
            if ($id -like 'WLCraftNode*' -or $id -like 'WLSkillNode*') {
                if (-not $ids.Contains($id)) { $missing.Add("$file`:$($i+1): prerequisite $id") }
            }
        }
        if ($line -match '^\s+zonePreset:\s+(\S+)') {
            $id = $Matches[1]
            if (-not $ids.Contains($id)) { $missing.Add("$file`:$($i+1): zonePreset $id") }
        }
    }
}
Write-Host "Prototype ids: $($ids.Count)"
if ($missing.Count -eq 0) { Write-Host 'No missing prototype refs in WL files (heuristic).' }
else {
    Write-Host "Missing ($($missing.Count)):"
    $missing | Select-Object -First 30
}

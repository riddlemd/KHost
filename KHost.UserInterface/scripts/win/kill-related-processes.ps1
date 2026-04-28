$patterns = @('KHost', 'dotnet')

$killed = @()

foreach ($pattern in $patterns) {
    Get-Process -Name "*$pattern*" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host "Killing $($_.Name) (PID $($_.Id))"
        Stop-Process -Id $_.Id -Force
        $killed += $_.Name
    }
}

if ($killed.Count -eq 0) {
    Write-Host "No dotnet processes found."
} else {
    Write-Host "Done. Killed $($killed.Count) process(es)."
}

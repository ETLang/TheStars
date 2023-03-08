& .\BuildScraper.ps1

# WARNING: You will need at least 2 TB of free hard drive space
# WARNING: Expect this operation to take several days to complete

# Execute the built EXE
$exePath = "IMDBScraper\bin\Release\net6.0-windows\IMDBScraper.exe"
if (Test-Path $exePath) {
    & $exePath 800000 200000 DB_Full
} else {
    Write-Error "Executable not found: $exePath"
    Exit 1
}
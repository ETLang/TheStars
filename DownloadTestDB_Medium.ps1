& .\BuildScraper.ps1

# Execute the built EXE
$exePath = "IMDBScraper\bin\Release\net6.0-windows\IMDBScraper.exe"
if (Test-Path $exePath) {
    & $exePath 1000 50 TestDB_Medium
} else {
    Write-Error "Executable not found: $exePath"
    Exit 1
}
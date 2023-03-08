& .\BuildScraper.ps1

# Execute the built EXE
$exePath = "IMDBScraper\bin\Release\net6.0-windows\IMDBScraper.exe"
if (Test-Path $exePath) {
    & $exePath 100 20 TestDB_Small
} else {
    Write-Error "Executable not found: $exePath"
    Exit 1
}
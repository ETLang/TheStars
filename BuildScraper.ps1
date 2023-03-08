# Find the latest version of Visual Studio installed
$latestVersion =  Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\" -Directory | Where-Object { $_.Name -match "^(\d+)" } | ForEach-Object { $Matches[1] } | Sort-Object -Descending | Select-Object -first 1

# Find MSBuild.exe for the latest version of Visual Studio installed
$msbuildPath = "${env:ProgramFiles}\Microsoft Visual Studio\$latestVersion\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
if (-not (Test-Path $msbuildPath)) {
    $msbuildPath = "${env:ProgramFiles}\Microsoft Visual Studio\$latestVersion\Professional\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuildPath)) {
    $msbuildPath = "${env:ProgramFiles}\Microsoft Visual Studio\$latestVersion\Community\MSBuild\Current\Bin\MSBuild.exe"
}
if (-not (Test-Path $msbuildPath)) {
    Write-Error "MSBuild.exe not found for Visual Studio $latestVersion."
    Exit 1
}

# Build the solution using MSBuild.exe
& $msbuildPath /verbosity:m /t:Rebuild /p:Configuration=Release IMDBScraper\IMDBScraper.sln

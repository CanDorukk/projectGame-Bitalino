$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $RepoRoot

Write-Host "=== EMG Backend Bundle (PyInstaller) ===" -ForegroundColor Cyan

python --version
if ($LASTEXITCODE -ne 0) {
    throw "Python not found. Install Python 3 first (developers only)."
}

Write-Host "Installing build dependencies..."
python -m pip install --upgrade pip
python -m pip install -r requirements.txt pyinstaller

Write-Host "Building emg_backend.exe (this may take a few minutes)..."
python -m PyInstaller emg_backend.spec --noconfirm

$DistExe = Join-Path $RepoRoot "dist\emg_backend\emg_backend.exe"
if (-not (Test-Path $DistExe)) {
    throw "Build failed: $DistExe not found"
}

$DestDir = Join-Path $RepoRoot "3DRunner-main\Assets\StreamingAssets\EMG"
New-Item -ItemType Directory -Force -Path $DestDir | Out-Null

Write-Host "Copying bundle to StreamingAssets/EMG..."
Copy-Item -Path (Join-Path $RepoRoot "dist\emg_backend\*") -Destination $DestDir -Recurse -Force

Write-Host "Done. Bundled backend:" -ForegroundColor Green
Write-Host "  $DestDir\emg_backend.exe"
Write-Host "Next: Unity build (File -> Build Settings -> Build)"

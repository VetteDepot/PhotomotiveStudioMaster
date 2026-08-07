$ErrorActionPreference = 'Stop'

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent (Split-Path -Parent $scriptRoot)
$venv = Join-Path $repoRoot '.venv'

Write-Host 'Photomotive Studio Master - Local AI Setup'
Write-Host "Repository: $repoRoot"

if (-not (Get-Command py -ErrorAction SilentlyContinue)) {
    throw 'Python launcher (py.exe) was not found. Install Python 3.11 or 3.12 for Windows, then run this script again.'
}

if (-not (Test-Path $venv)) {
    Write-Host 'Creating Python virtual environment...'
    py -3 -m venv $venv
}

$python = Join-Path $venv 'Scripts\python.exe'
$pip = Join-Path $venv 'Scripts\pip.exe'

Write-Host 'Updating pip...'
& $python -m pip install --upgrade pip

Write-Host 'Installing local AI packages...'
& $pip install -r (Join-Path $scriptRoot 'requirements.txt')

Write-Host 'Warming the U2Net model cache. Internet is required for this one-time step...'
& $python -c "from rembg import new_session; new_session('u2net'); print('U2Net model ready.')"

Write-Host ''
Write-Host 'AI setup complete. Event-time extraction can now run locally without internet.' -ForegroundColor Green

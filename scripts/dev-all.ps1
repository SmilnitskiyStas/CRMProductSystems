# Starts everything for local development of the web panel + the cashier desktop app:
#   - Postgres (docker container, if it is not running)
#   - backend API    http://localhost:5100
#   - web panel      http://localhost:3003
#   - cashier app    Electron window (dev mode, points at the API above)
# Each service gets its own PowerShell window; close the window to stop that service.
# Usage (from the repo root):  powershell -ExecutionPolicy Bypass -File scripts/dev-all.ps1

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

$api = 'http://localhost:5100'
$web = 'http://localhost:3003'

# 1. Database container (name used by the repo's docker-compose; ignore if it is already up).
$db = 'crmproductsystems-postgres-1'
if ((docker ps --filter "name=$db" --format '{{.Names}}') -ne $db) {
  Write-Host "Starting $db ..."
  docker start $db | Out-Null
}

function Start-Window($title, $dir, $command) {
  $full = "`$Host.UI.RawUI.WindowTitle = '$title'; Set-Location '$dir'; $command"
  Start-Process powershell -ArgumentList '-NoExit', '-Command', $full
}

# 2. Backend API (CORS allows the web panel origin).
Start-Window 'ShelfGuard API' $root (
  "`$env:ASPNETCORE_URLS='$api'; `$env:ASPNETCORE_ENVIRONMENT='Development'; `$env:Cors__Origins='$web'; " +
  'dotnet run --project backend/ShelfGuard.Api --no-launch-profile')

# 3. Web panel.
Start-Window 'ShelfGuard Web' (Join-Path $root 'frontend') (
  "`$env:NEXT_PUBLIC_API_URL='$api'; npm run dev -- -p 3003")

# 4. Cashier desktop app (Electron, dev mode). Give the API a head start first.
Start-Sleep -Seconds 15
Start-Window 'ShelfGuard Cashier' (Join-Path $root 'cashier') (
  "`$env:CASHIER_SERVER_URL='$api'; npm run dev")

Write-Host "Started: API $api, web $web, cashier (Electron window)."

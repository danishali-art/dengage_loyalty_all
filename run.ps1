# Starts the full loyalty-update stack: infra containers, Api, Consumer, and the Angular web app.
# Each service opens in its own PowerShell window so you can watch its logs live.
# Run from the repo root:  .\run.ps1

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "Starting infra (Postgres, RabbitMQ, Redis)..." -ForegroundColor Cyan
docker compose -f "$root\docker-compose.yml" up -d
if ($LASTEXITCODE -ne 0) {
    Write-Host "Docker Desktop doesn't seem to be running. Start it, then re-run this script." -ForegroundColor Red
    exit 1
}

Write-Host "Waiting for Postgres/RabbitMQ to report healthy..." -ForegroundColor Cyan
$deadline = (Get-Date).AddSeconds(60)
do {
    Start-Sleep -Seconds 2
    $unhealthy = docker compose -f "$root\docker-compose.yml" ps --format json |
        ConvertFrom-Json |
        Where-Object { $_.Health -and $_.Health -ne "healthy" }
} while ($unhealthy -and (Get-Date) -lt $deadline)

Write-Host "Starting Api (http://localhost:5173) ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root'; dotnet run --project src\dEngage.Loyalty.Api"

Write-Host "Starting Consumer (background worker) ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root'; dotnet run --project src\dEngage.Loyalty.Consumer"

Write-Host "Starting web (http://localhost:4200) ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\web'; npx ng serve --port 4200"

Write-Host ""
Write-Host "All services launching in separate windows:" -ForegroundColor Green
Write-Host "  Api           -> http://localhost:5173/swagger"
Write-Host "  Web portal    -> http://localhost:4200"
Write-Host "  RabbitMQ mgmt -> http://localhost:15672  (guest/guest)"
Write-Host ""
Write-Host "Give the Api/Consumer/Web windows a few seconds to finish their first build before hitting the URLs above."

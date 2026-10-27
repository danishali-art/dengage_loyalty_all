# Starts the full loyalty-update stack: infra containers, Api, Public.Api, Consumer, and the admin portal.
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

Write-Host "Starting Public.Api (event ingestion, http://localhost:5175) ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root'; dotnet run --project src\dEngage.Loyalty.Public.Api --launch-profile http"

Write-Host "Starting Consumer (background worker) ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root'; dotnet run --project src\dEngage.Loyalty.Consumer"

Write-Host "Starting admin portal (http://localhost:4201) ..." -ForegroundColor Cyan
Start-Process powershell -ArgumentList "-NoExit", "-Command", "cd '$root\web'; npm run dev"

Write-Host ""
Write-Host "All services launching in separate windows:" -ForegroundColor Green
Write-Host "  Api           -> http://localhost:5173"
Write-Host "  Public.Api    -> http://localhost:5175  (event ingestion, X-Api-Key)"
Write-Host "  Admin portal  -> http://localhost:4201"
Write-Host "  RabbitMQ mgmt -> http://localhost:15672  (guest/guest)"
Write-Host ""
Write-Host "Give the Api/Consumer/portal windows a few seconds to finish their first build before hitting the URLs above."

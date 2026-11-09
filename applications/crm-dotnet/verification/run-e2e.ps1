$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$frontend = Join-Path $root "frontend"

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    throw "Docker Desktop / Docker Compose is required."
}
if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "Node.js 20+ and npm are required to run the Playwright suite."
}

# Compose reads .env automatically; also expose the key to Playwright so its AI test
# is skipped or enabled consistently with the API container.
$envFile = Join-Path $root ".env"
if (Test-Path $envFile) {
    Get-Content $envFile | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith("#") -and $line.Contains("=")) {
            $parts = $line.Split("=", 2)
            if (-not (Get-Item "Env:$($parts[0])" -ErrorAction SilentlyContinue)) {
                [Environment]::SetEnvironmentVariable($parts[0], $parts[1], "Process")
            }
        }
    }
}

Write-Host "Starting PostgreSQL, Keycloak, migrations, API and frontend..." -ForegroundColor Cyan
Push-Location $root
try {
    docker compose -p foundgine-crm-e2e up --build -d
    if ($LASTEXITCODE -ne 0) { throw "docker compose up failed." }

    $checks = @(
        "http://localhost:8080/realms/foundgine-crm/.well-known/openid-configuration",
        "http://localhost:5080/health",
        "http://localhost:3000/"
    )
    foreach ($url in $checks) {
        $ready = $false
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            try {
                Invoke-WebRequest -Uri $url -TimeoutSec 5 -UseBasicParsing | Out-Null
                $ready = $true
                break
            } catch {
                Start-Sleep -Seconds 3
            }
        }
        if (-not $ready) { throw "Service did not become ready: $url" }
    }

    Push-Location $frontend
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install failed." }
        npx playwright install chromium
        if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed." }
        npm run test:e2e
        if ($LASTEXITCODE -ne 0) { throw "Playwright E2E tests failed." }
    } finally {
        Pop-Location
    }
} finally {
    Pop-Location
}

Write-Host "E2E run finished. The isolated stack is still running; stop it with 'docker compose -p foundgine-crm-e2e down'." -ForegroundColor Green

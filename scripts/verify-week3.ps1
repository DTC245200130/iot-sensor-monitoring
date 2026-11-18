$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet build IoTSensorMonitoring.slnx -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet tests/Sensor.IntegrationTests/bin/Release/net10.0/Sensor.IntegrationTests.dll $root
    if ($LASTEXITCODE -ne 0) { throw 'SignalR integration checks failed. See artifacts/week3-results.json.' }
} finally { Pop-Location }

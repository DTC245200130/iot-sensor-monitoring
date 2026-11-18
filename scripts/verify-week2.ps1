param([int]$Port = 0)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$results = New-Object System.Collections.Generic.List[object]
$apiProcess = $null
$client = New-Object System.Net.Http.HttpClient
$client.Timeout = [TimeSpan]::FromSeconds(5)

function Assert-True($Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Request([string]$Method, [string]$Path, $Body = $null) {
    $request = New-Object System.Net.Http.HttpRequestMessage
    $request.Method = New-Object System.Net.Http.HttpMethod($Method)
    $request.RequestUri = [Uri]($script:baseUrl + $Path)
    if ($null -ne $Body) {
        $json = if ($Body -is [string]) { $Body } else { ConvertTo-Json -InputObject $Body -Compress }
        $request.Content = New-Object System.Net.Http.StringContent($json, [Text.Encoding]::UTF8, 'application/json')
    }
    try {
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $data = if ($text) { ConvertFrom-Json -InputObject $text } else { $null }
            return [pscustomobject]@{ Status = [int]$response.StatusCode; Data = $data; Body = $text; Location = [string]$response.Headers.Location }
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

function Check([string]$Name, [string]$Method, [string]$Path, [int]$Expected, $Body = $null) {
    $response = Request $Method $Path $Body
    $results.Add([pscustomobject]@{
        Test = $Name; Method = $Method; Path = $Path; Expected = $Expected
        Actual = $response.Status; Passed = ($response.Status -eq $Expected)
        Location = $response.Location; Body = $response.Body
    })
    Assert-True ($response.Status -eq $Expected) "$Name expected $Expected but got $($response.Status): $($response.Body)"
    return $response
}

function Start-TestApi([string]$Suffix) {
    $dll = Join-Path $root 'src\Sensor.Api\bin\Release\net10.0\Sensor.Api.dll'
    $startArgs = @{
        FilePath = 'dotnet'; ArgumentList = @(('"' + $dll + '"'), '--urls', $script:baseUrl)
        WorkingDirectory = (Split-Path -Parent $dll); PassThru = $true
        RedirectStandardOutput = (Join-Path $artifacts "api-$Suffix.log")
        RedirectStandardError = (Join-Path $artifacts "api-$Suffix-error.log")
    }
    if ($env:OS -eq 'Windows_NT') { $startArgs.WindowStyle = 'Hidden' }
    $script:apiProcess = Start-Process @startArgs
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        if ($script:apiProcess.HasExited) { throw 'API exited during startup. Read artifacts/api-*.log.' }
        try { if ((Request 'GET' '/').Status -eq 200) { return } } catch { }
        Start-Sleep -Milliseconds 100
    }
    throw 'API did not start in time.'
}

function Stop-TestApi {
    if ($script:apiProcess -and -not $script:apiProcess.HasExited) {
        Stop-Process -Id $script:apiProcess.Id
        $script:apiProcess.WaitForExit()
    }
}

Push-Location $root
try {
    dotnet build IoTSensorMonitoring.slnx -c Release
    Assert-True ($LASTEXITCODE -eq 0) 'Build failed.'
    if ($Port -eq 0) {
        $listener = New-Object System.Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $Port = $listener.LocalEndpoint.Port
        $listener.Stop()
    }
    $script:baseUrl = "http://127.0.0.1:$Port"
    Start-TestApi 'first'
    $null = Check 'API status' 'GET' '/' 200
    $list = Check 'Initially empty list' 'GET' '/sensors' 200
    Assert-True (@($list.Data).Count -eq 0) 'Expected empty storage.'
    $created = Check 'Register sensor' 'POST' '/sensors' 201 @{
        name = '  Room A  '; type = ' temperature '; unit = ' C '; alertThreshold = 35
    }
    $id = $created.Data.id
    Assert-True ($created.Data.name -eq 'Room A') 'Name was not trimmed.'
    Assert-True ($created.Location -eq "/sensors/$id") 'Invalid Location header.'
    $detail = Check 'Follow Location' 'GET' $created.Location 200
    Assert-True ($detail.Data.id -eq $id) 'GET did not return the created sensor.'
    $unknown = [Guid]::NewGuid().ToString()
    $missing = Check 'Unknown sensor' 'GET' "/sensors/$unknown" 404
    Assert-True ($missing.Data.code -eq 'SENSOR_NOT_FOUND') 'Incorrect error code.'
    $null = Check 'Route constraint' 'GET' '/sensors/not-a-guid' 404
    $null = Check 'Invalid sensor' 'POST' '/sensors' 400 @{ name = ' '; type = 't'; unit = 'C'; alertThreshold = 35 }
    $null = Check 'Missing threshold' 'POST' '/sensors' 400 @{ name = 'Room B'; type = 't'; unit = 'C' }
    $null = Check 'Malformed JSON' 'POST' '/sensors' 400 '{bad json'
    $null = Check 'Empty sensor body' 'POST' '/sensors' 400 '{}'
    $null = Check 'No latest reading' 'GET' "/sensors/$id/readings/latest" 404
    $emptyHistory = Check 'Empty history' 'GET' "/sensors/$id/readings" 200
    Assert-True ($emptyHistory.Data.total -eq 0) 'Expected zero readings.'
    $null = Check 'Unknown sensor reading' 'POST' "/sensors/$unknown/readings" 404 @{ value = 1; measuredAt = '2026-11-18T01:00:00Z' }
    $null = Check 'Missing value' 'POST' "/sensors/$id/readings" 400 @{ measuredAt = '2026-11-18T01:00:00Z' }
    $null = Check 'Missing timestamp' 'POST' "/sensors/$id/readings" 400 @{ value = 10 }
    $null = Check 'Invalid timestamp' 'POST' "/sensors/$id/readings" 400 @{ value = 10; measuredAt = 'bad' }

    $alert = Check 'Above threshold' 'POST' "/sensors/$id/readings" 201 @{ value = 36; measuredAt = '2026-11-18T10:00:00+07:00' }
    Assert-True ($alert.Data.isAlert -eq $true) 'Expected alert for 36 > 35.'
    $sameReading = Check 'Follow reading Location' 'GET' $alert.Location 200
    Assert-True ($sameReading.Data.items[0].id -eq $alert.Data.id) 'Reading Location does not include created reading.'
    $equal = Check 'Equal threshold' 'POST' "/sensors/$id/readings" 201 @{ value = 35; measuredAt = '2026-11-18T02:00:00Z' }
    Assert-True ($equal.Data.isAlert -eq $false) '35 == 35 must not trigger an alert.'
    $below = Check 'Zero is valid' 'POST' "/sensors/$id/readings" 201 @{ value = 0; measuredAt = '2026-11-18T01:00:00Z' }
    Assert-True ($below.Data.isAlert -eq $false) 'Expected normal reading.'
    $latest = Check 'Latest by measurement time' 'GET' "/sensors/$id/readings/latest" 200
    Assert-True ($latest.Data.id -eq $alert.Data.id) 'Latest should use measurement time, not arrival order.'
    $range = Check 'Inclusive time range' 'GET' "/sensors/$id/readings?from=2026-11-18T02:00:00Z&to=2026-11-18T03:00:00Z" 200
    Assert-True ($range.Data.total -eq 2) 'Time range should include both endpoints and normalize timezone.'
    $page = Check 'History pagination' 'GET' "/sensors/$id/readings?skip=1&take=1" 200
    Assert-True ($page.Data.total -eq 3 -and @($page.Data.items).Count -eq 1 -and $page.Data.items[0].value -eq 35) 'Incorrect pagination.'
    $null = Check 'Reversed range' 'GET' "/sensors/$id/readings?from=2026-11-19T00:00:00Z&to=2026-11-18T00:00:00Z" 400
    $null = Check 'Invalid page size' 'GET' "/sensors/$id/readings?take=501" 400
    $null = Check 'Invalid query binding' 'GET' "/sensors/$id/readings?take=abc" 400

    # Real parallel HTTP requests: verify no readings or IDs are lost.
    $requests = @(); $tasks = @()
    try {
        for ($i = 0; $i -lt 20; $i++) {
            $request = New-Object System.Net.Http.HttpRequestMessage
            $request.Method = [Net.Http.HttpMethod]::Post
            $request.RequestUri = [Uri]"$script:baseUrl/sensors/$id/readings"
            $request.Content = New-Object System.Net.Http.StringContent(('{"value":' + $i + ',"measuredAt":"2026-11-18T04:00:00Z"}'), [Text.Encoding]::UTF8, 'application/json')
            $requests += $request
            $tasks += $client.SendAsync($request)
        }
        foreach ($task in $tasks) {
            $response = $task.GetAwaiter().GetResult()
            try { Assert-True ([int]$response.StatusCode -eq 201) 'Concurrent POST failed.' }
            finally { $response.Dispose() }
        }
    } finally { foreach ($request in $requests) { $request.Dispose() } }
    $concurrent = Check '20 concurrent readings retained' 'GET' "/sensors/$id/readings" 200
    Assert-True ($concurrent.Data.total -eq 23) 'Lost concurrent readings.'
    Assert-True (@($concurrent.Data.items.id | Select-Object -Unique).Count -eq 23) 'Duplicate reading IDs.'

    $clientDll = Join-Path $root 'src\Sensor.Client\bin\Release\net10.0\Sensor.Client.dll'
    & dotnet $clientDll $script:baseUrl > (Join-Path $artifacts 'client-success.log') 2>&1
    Assert-True ($LASTEXITCODE -eq 0) 'Console demo failed.'
    $demoSensors = Check 'Two demo sensors registered' 'GET' '/sensors' 200
    Assert-True (@($demoSensors.Data).Count -eq 3) 'Expected test sensor plus two simulator sensors.'
    foreach ($sensor in @($demoSensors.Data) | Where-Object { $_.id -ne $id }) {
        $history = Request 'GET' "/sensors/$($sensor.id)/readings"
        Assert-True ($history.Data.total -eq 3) 'Simulator must send 3 readings per sensor.'
    }

    Stop-TestApi
    $connected = $true
    try { $null = Request 'GET' '/' } catch { $connected = $false }
    Assert-True (-not $connected) 'Expected connection failure after stopping API.'
    $results.Add([pscustomobject]@{Test='API stopped';Method='GET';Path='/';Expected='connection failure';Actual='connection failure';Passed=$true;Location='';Body='No HTTP response'})
    # Windows PowerShell treats native stderr as an error record; capture it without stopping the script.
    $ErrorActionPreference = 'Continue'
    & dotnet $clientDll $script:baseUrl > (Join-Path $artifacts 'client-offline.log') 2>&1
    $offlineExit = $LASTEXITCODE
    $ErrorActionPreference = 'Stop'
    Assert-True ($offlineExit -eq 1) 'Client must return exit code 1 when API is offline.'
    $results.Add([pscustomobject]@{Test='Client handles offline API';Method='CLI';Path='Sensor.Client';Expected=1;Actual=$offlineExit;Passed=$true;Location='';Body='Connection failure handled'})

    Start-TestApi 'restart'
    $afterRestart = Check 'Memory resets after restart' 'GET' '/sensors' 200
    Assert-True (@($afterRestart.Data).Count -eq 0) 'Storage should reset after restart.'
    $null = Check 'Old sensor gone after restart' 'GET' "/sensors/$id" 404
    Write-Host "PASS: $($results.Count) checks plus value, concurrency and simulator assertions."
} finally {
    Stop-TestApi
    $client.Dispose()
    ConvertTo-Json -InputObject @($results.ToArray()) -Depth 8 | Set-Content (Join-Path $artifacts 'week2-results.json') -Encoding UTF8
    $results | Select-Object Test, Method, Expected, Actual, Passed | Format-Table -AutoSize | Out-String -Width 180 | Set-Content (Join-Path $artifacts 'week2-results.log') -Encoding UTF8
    Pop-Location
}

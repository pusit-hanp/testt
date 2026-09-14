param(
    [string]$SourceDir = (Join-Path $PSScriptRoot '..\..\02-Enhancement-Working\Z02JHVPService'),
    [string]$Compiler = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
$ErrorActionPreference = 'Stop'
$outputDir = Join-Path $PSScriptRoot 'bin'
$null = New-Item -ItemType Directory -Force -Path $outputDir
$hostExe = Join-Path $outputDir 'HvpHostUnderTest.exe'
& $Compiler /nologo /noconfig /reference:System.dll /target:exe /main:HostRegression.Entry "/out:$hostExe" (Join-Path $SourceDir 'Program.cs') (Join-Path $PSScriptRoot 'HostStubs.cs')
if ($LASTEXITCODE -ne 0) { throw 'Host fixture compilation failed.' }

function Assert-True($Value, [string]$Message) { if (-not $Value) { throw $Message } }
function Start-HostCase([string]$Arguments, [hashtable]$Flags) {
    $tracePath = Join-Path $outputDir ([Guid]::NewGuid().ToString('N') + '.trace')
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $hostExe
    $psi.Arguments = $Arguments
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.EnvironmentVariables['HVP_HOST_TRACE'] = $tracePath
    foreach ($key in $Flags.Keys) { $psi.EnvironmentVariables[$key] = [string]$Flags[$key] }
    $process = [System.Diagnostics.Process]::Start($psi)
    return @{
        Process = $process; TracePath = $tracePath
        Stdout = $process.StandardOutput.ReadToEndAsync()
        Stderr = $process.StandardError.ReadToEndAsync()
    }
}
function Read-Trace($Case) {
    if (Test-Path -LiteralPath $Case.TracePath) {
        # Timer callbacks append while the parent polls; allow both handles to coexist.
        $stream = [IO.File]::Open($Case.TracePath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $reader = New-Object IO.StreamReader -ArgumentList $stream
        try { return $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    return ''
}
function Wait-Trace($Case, [string]$Pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if ((Read-Trace $Case) -match $Pattern) { return }
        if ($Case.Process.HasExited) { break }
        Start-Sleep -Milliseconds 25
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Host did not reach $Pattern. Trace: $(Read-Trace $Case)"
}
function Wait-Exit($Case) {
    Assert-True ($Case.Process.WaitForExit(5000)) 'Host did not exit within 5 seconds.'
    return $Case.Process.ExitCode
}
function Close-Case($Case) {
    if (-not $Case.Process.HasExited) { $Case.Process.Kill(); $Case.Process.WaitForExit() }
    $Case.Process.Dispose()
}
function Assert-Sanitized($Case) {
    $all = (Read-Trace $Case) + $Case.Stdout.Result + $Case.Stderr.Result
    Assert-True ($all -notmatch 'host-secret|Password=|UNHANDLED') 'Host exposed or failed to handle a raw exception.'
    Assert-True ($all -match '(?i)error|fatal|failed') 'Fatal failure had no visible diagnostic.'
}
function Run-Case([string]$Name, [scriptblock]$Body) {
    try { & $Body; Write-Output "PASS $Name"; $script:passed++ }
    catch { Write-Output "FAIL $Name -- $($_.Exception.Message)"; $script:failed++ }
}
$passed = 0
$failed = 0

# Catches accidentally dispatching Task Scheduler to SCM or allowing stdin to stop it.
foreach ($interactive in @('0', '1')) {
    Run-Case "background is continuous and ignores Enter/EOF (interactive=$interactive)" {
        $case = Start-HostCase '--background' @{ HVP_HOST_INTERACTIVE = $interactive; HVP_HOST_CONSOLE_INPUT = '1' }
        try {
            Wait-Trace $case 'TICK'
            $case.Process.StandardInput.WriteLine()
            $case.Process.StandardInput.Close()
            Assert-True (-not $case.Process.WaitForExit(350)) 'Background host exited after Enter/EOF.'
            Assert-True ((Read-Trace $case) -notmatch 'READ_STDIN|SERVICE_RUN') 'Background host read stdin or dispatched to SCM.'
        } finally { Close-Case $case }
    }
}

# Catches removing the supported manual Enter path or skipping orderly cleanup.
Run-Case 'manual no-argument launch stops after Enter' {
    $case = Start-HostCase '' @{ HVP_HOST_INTERACTIVE = '1'; HVP_HOST_CONSOLE_INPUT = '1' }
    try {
        Wait-Trace $case 'READ_STDIN'
        $case.Process.StandardInput.WriteLine()
        Assert-True ((Wait-Exit $case) -eq 0) 'Manual host returned a failure exit code.'
        Assert-True ((Read-Trace $case) -match 'START[\s\S]*STOP') 'Manual host skipped StopService.'
    } finally { Close-Case $case }
}

# Catches treating redirected EOF as a manual stop request.
Run-Case 'manual redirected stdin stays running after EOF' {
    $case = Start-HostCase '' @{ HVP_HOST_INTERACTIVE = '1'; HVP_HOST_CONSOLE_INPUT = '0' }
    try {
        Wait-Trace $case 'TICK'
        $case.Process.StandardInput.Close()
        Assert-True (-not $case.Process.WaitForExit(350)) 'Redirected manual host exited on EOF.'
        Assert-True ((Read-Trace $case) -notmatch 'READ_STDIN') 'Manual host read redirected stdin.'
    } finally { Close-Case $case }
}

# Catches removing the existing Windows Service entry path.
Run-Case 'noninteractive no-argument launch dispatches to ServiceBase.Run' {
    $case = Start-HostCase '' @{ HVP_HOST_INTERACTIVE = '0' }
    try {
        Assert-True ((Wait-Exit $case) -eq 0) 'Service entry returned a failure exit code.'
        Assert-True ((Read-Trace $case) -match 'SERVICE_RUN count=1') 'Service entry did not dispatch its service.'
        Assert-True ((Read-Trace $case) -notmatch 'START|READ_STDIN') 'Service entry ran console startup.'
    } finally { Close-Case $case }
}

# Catches ignored invalid arguments, accidental startup and argument/credential echo.
foreach ($arguments in @('--invalid=host-secret', '--background extra', '--background --background')) {
    Run-Case "invalid arguments fail before construction ($arguments)" {
        $case = Start-HostCase $arguments @{ HVP_HOST_INTERACTIVE = '0' }
        try {
            Assert-True ((Wait-Exit $case) -ne 0) 'Invalid arguments returned success.'
            Assert-True ((Read-Trace $case) -notmatch 'CONSTRUCT|START|SERVICE_RUN') 'Invalid arguments started a service.'
            Assert-True (($case.Stdout.Result + $case.Stderr.Result) -notmatch 'host-secret') 'Invalid arguments were echoed.'
            Assert-True (($case.Stdout.Result + $case.Stderr.Result) -match '(?i)usage|argument') 'Invalid arguments had no useful diagnostic.'
        } finally { Close-Case $case }
    }
}

# Catches raw startup exception escape, missing nonzero exit and timer cleanup omission.
foreach ($mode in @('', '--background')) {
    Run-Case "startup failure stops a partially started service ($mode)" {
        $case = Start-HostCase $mode @{ HVP_HOST_INTERACTIVE = '1'; HVP_HOST_START_FAIL = '1' }
        try {
            Assert-True ((Wait-Exit $case) -ne 0) 'Startup failure returned success.'
            Assert-True ((Read-Trace $case) -match 'START[\s\S]*STOP') 'Partial startup did not stop its timer.'
            Assert-Sanitized $case
        } finally { Close-Case $case }
    }
}

foreach ($failure in @('HVP_HOST_CONSTRUCTOR_FAIL', 'HVP_HOST_SERVICE_FAIL')) {
    Run-Case "fatal host error is handled and sanitized ($failure)" {
        $flags = @{ HVP_HOST_INTERACTIVE = '0' }; $flags[$failure] = '1'
        $case = Start-HostCase '' $flags
        try {
            Assert-True ((Wait-Exit $case) -ne 0) 'Fatal host failure returned success.'
            Assert-Sanitized $case
        } finally { Close-Case $case }
    }
}

Run-Case 'fatal startup remains sanitized when the logger also fails' {
    $case = Start-HostCase '--background' @{ HVP_HOST_INTERACTIVE = '1'; HVP_HOST_START_FAIL = '1'; HVP_HOST_LOG_FAIL = '1' }
    try {
        Assert-True ((Wait-Exit $case) -ne 0) 'Startup failure returned success.'
        Assert-True ((Read-Trace $case) -match 'STOP') 'Startup failure did not stop the service.'
        Assert-Sanitized $case
    } finally { Close-Case $case }
}

Run-Case 'manual stop failure returns nonzero and stays sanitized' {
    $case = Start-HostCase '' @{ HVP_HOST_INTERACTIVE = '1'; HVP_HOST_CONSOLE_INPUT = '1'; HVP_HOST_STOP_FAIL = '1' }
    try {
        Wait-Trace $case 'READ_STDIN'
        $case.Process.StandardInput.WriteLine()
        Assert-True ((Wait-Exit $case) -ne 0) 'Stop failure returned success.'
        Assert-Sanitized $case
    } finally { Close-Case $case }
}

Write-Output "Host regression: $passed passed; $failed failed."
if ($failed -gt 0) { exit 1 }
exit 0

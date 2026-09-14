param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\..\02-Enhancement-Working\Z02JHVPService\bin\Release'),
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
$fixture = Join-Path $PSScriptRoot ('bin\isolation-' + [Guid]::NewGuid().ToString('N'))
$complete = Join-Path $fixture 'complete-build'
$incomplete = Join-Path $fixture 'incomplete-build'
$null = New-Item -ItemType Directory -Path $complete, $incomplete -Force
# Run an unmodified copy of the actual runner, isolated from other tests and builds.
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Run.ps1'), (Join-Path $PSScriptRoot 'Program.cs') -Destination $fixture
$files = @(Get-ChildItem -LiteralPath $BuildDir -File | Where-Object {
    $_.Extension -eq '.dll' -or $_.Name -eq 'Z02JHVPService.exe' -or $_.Name -eq 'Z02JHVPService.exe.config'
})
$files | Copy-Item -Destination $complete
$files | Where-Object { $_.Name -ne 'System.Text.Json.dll' } | Copy-Item -Destination $incomplete
if (-not (Test-Path -LiteralPath (Join-Path $complete 'System.Text.Json.dll'))) { throw 'The complete fixture requires System.Text.Json.dll.' }
if (Test-Path -LiteralPath (Join-Path $incomplete 'System.Text.Json.dll')) { throw 'The incomplete fixture must omit System.Text.Json.dll.' }

$runner = Join-Path $fixture 'Run.ps1'
$compilerArgs = @()
if (-not [string]::IsNullOrWhiteSpace($Compiler)) { $compilerArgs = @('-Compiler', $Compiler) }
$completeLog = Join-Path $fixture 'complete.log'
& powershell -NoProfile -ExecutionPolicy Bypass -File $runner -BuildDir $complete @compilerArgs *> $completeLog
if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath $completeLog -Pattern '^PASS ').Count -ne 6) {
    throw "The complete build did not pass all six runtime checks. See $completeLog"
}
Write-Output 'PASS complete build succeeds before the missing-dependency check'

$incompleteLog = Join-Path $fixture 'incomplete.log'
try {
    # Windows PowerShell treats native stderr as an ErrorRecord; this failure is expected.
    $ErrorActionPreference = 'Continue'
    & powershell -NoProfile -ExecutionPolicy Bypass -File $runner -BuildDir $incomplete @compilerArgs *> $incompleteLog
    $incompleteExit = $LASTEXITCODE
} finally { $ErrorActionPreference = 'Stop' }
$failureText = Get-Content -Raw -LiteralPath $incompleteLog
if ($incompleteExit -eq 0) { throw 'FAIL incomplete build passed by using stale DLLs from the previous run.' }
# Require the intended compiler failure, not an unrelated launch/policy error.
if ($failureText -notmatch 'CS0234' -or $failureText -notmatch 'Json') {
    throw "The incomplete build failed for an unexpected reason. See $incompleteLog"
}
Write-Output 'PASS incomplete build is rejected despite DLLs from a previous successful run'
Write-Output "Isolation regression: 2 passed; evidence: $fixture"

param(
    [string]$BuildDir = (Join-Path $PSScriptRoot '..\..\02-Enhancement-Working\Z02JHVPService\bin\Release'),
    [string]$Compiler
)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Compiler)) {
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        $Compiler = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\Roslyn\csc.exe' | Select-Object -First 1)
    }
    if ([string]::IsNullOrWhiteSpace($Compiler)) { throw 'A Visual Studio C# compiler is required; pass -Compiler with its csc.exe path.' }
}
# A fresh directory prevents an incomplete build from borrowing DLLs from earlier runs.
$output = Join-Path $PSScriptRoot ('bin\' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Force -Path $output
Write-Output ("Runtime smoke output: " + $output)
Get-ChildItem -LiteralPath $BuildDir -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name -eq 'Z02JHVPService.exe' } | Copy-Item -Destination $output
$references = @('System.dll','System.Core.dll','System.Data.dll','System.Configuration.dll','System.Numerics.dll')
$references += (Get-ChildItem -LiteralPath $output -Filter '*.dll').FullName
$references += Join-Path $output 'Z02JHVPService.exe'
$arguments = @('/nologo','/target:exe',('/out:' + (Join-Path $output 'HvpRuntimeSmoke.exe')))
$arguments += $references | ForEach-Object { '/reference:' + $_ }
$arguments += Join-Path $PSScriptRoot 'Program.cs'
& $Compiler $arguments
if ($LASTEXITCODE -ne 0) { throw 'Runtime smoke compilation failed' }
[xml]$config = Get-Content -Raw -LiteralPath (Join-Path $BuildDir 'Z02JHVPService.exe.config')
# The smoke fixture always uses placeholders, even when inspecting a company build.
$config.configuration.connectionStrings.add.SetAttribute('connectionString','User Id=MATERIAL;Password=xxx;Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=xxx)(PORT=1521))(CONNECT_DATA=(SID=xxx)));')
$config.Save((Join-Path $output 'HvpRuntimeSmoke.exe.config'))
& (Join-Path $output 'HvpRuntimeSmoke.exe')
exit $LASTEXITCODE

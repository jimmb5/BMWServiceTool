$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $projectRoot
try {
    dotnet build BMWServiceTool.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Testien rakennus epäonnistui.' }
    & './tests/G30ServiceTool.Tests/bin/Release/net9.0-windows/G30ServiceTool.Tests.exe' "$projectRoot/artifacts/qa"
    if ($LASTEXITCODE -ne 0) { throw 'Testit epäonnistuivat.' }
}
finally { Pop-Location }

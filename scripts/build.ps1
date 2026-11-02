param([switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
Push-Location $projectRoot
try {
    dotnet build BMWServiceTool.sln -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Rakennus epäonnistui.' }
    $bundledRuntime = if ($SelfContained) { 'true' } else { 'false' }
    dotnet publish src/G30ServiceTool.App/G30ServiceTool.App.csproj -c Release -r win-x64 --self-contained $bundledRuntime -o artifacts/app --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Julkaisukansion muodostus epäonnistui.' }
    Copy-Item -LiteralPath README.md, THIRD-PARTY-NOTICES.md -Destination artifacts/app
    Copy-Item -LiteralPath docs/tool32-laptop.txt -Destination artifacts/app/ALOITA-TASTA.txt
    Compress-Archive -Path artifacts/app/* -DestinationPath artifacts/G30ServiceTool-win-x64.zip -Force
    Write-Host "Valmis: $projectRoot\artifacts\app\G30ServiceTool.exe"
    Write-Host "Siirtopaketti: $projectRoot\artifacts\G30ServiceTool-win-x64.zip"
}
finally { Pop-Location }

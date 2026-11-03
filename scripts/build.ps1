param([switch]$NoPause, [switch]$Verify)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskBuild = Join-Path $taskRoot '.build'
$taskExit = 1
try {
    & (Join-Path $PSScriptRoot 'make-icon.ps1')
    if (!(Test-Path (Join-Path $taskRoot 'src/eFakturaViewer/Assets/update-public-key.xml'))) { throw 'Chyba verejny aktualizacny kluc. Pozrite README.' }
    & dotnet publish (Join-Path $taskRoot 'src/eFakturaViewer/eFakturaViewer.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o (Join-Path $taskBuild 'publish') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Zostavenie zlyhalo.' }
    New-Item -ItemType Directory -Force (Join-Path $taskRoot 'dist') | Out-Null
    $taskExe = Join-Path $taskRoot 'dist/eFakturaViewer.exe'
    Copy-Item -LiteralPath (Join-Path $taskBuild 'publish/eFakturaViewer.exe') -Destination $taskExe -Force
    if ($Verify) {
        $taskArgs = @('--self-test', ('"' + (Join-Path $taskRoot 'test-results') + '"'), ('"' + (Join-Path $taskRoot 'examples') + '"'))
        $taskTest = Start-Process $taskExe -ArgumentList $taskArgs -WindowStyle Hidden -Wait -PassThru
        if ($taskTest.ExitCode -ne 0) { throw 'Kontroly zlyhali. Pozrite test-results/failure.txt.' }
        $taskReport = Get-Content (Join-Path $taskRoot 'test-results/self-test.json') -Raw | ConvertFrom-Json
        Write-Host ('Uspesne kontroly: ' + $taskReport.checks.Count)
        foreach ($taskSkip in $taskReport.skipped) { Write-Host ('Preskocene: ' + $taskSkip) -ForegroundColor Yellow }
    }
    Write-Host "Hotovo: $taskExe" -ForegroundColor Green
    $taskExit = 0
} catch { Write-Host $_.Exception.Message -ForegroundColor Red }
finally {
    $taskResolved = [IO.Path]::GetFullPath($taskBuild)
    if ($taskResolved -ne (Join-Path $taskRoot '.build') -or !$taskResolved.StartsWith($taskRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Neocakavany adresar zostavenia.' }
    if (Test-Path -LiteralPath $taskResolved) {
        # Mazat iba vlastny pracovny adresar. Symlinky a junctions sa neprechadzaju.
        $taskLinks = Get-ChildItem -LiteralPath $taskResolved -Force -Recurse | Where-Object { $_.LinkType -in @('Junction','SymbolicLink') }
        if ($taskLinks -or (Get-Item -LiteralPath $taskResolved).LinkType -in @('Junction','SymbolicLink')) { Write-Host 'Upratanie preskocene: odkaz na iny adresar.'; $taskExit = 1 }
        else { Remove-Item -LiteralPath $taskResolved -Recurse -Force }
    }
}
if (!$NoPause) { [void](Read-Host 'Stlacte Enter') }
exit $taskExit

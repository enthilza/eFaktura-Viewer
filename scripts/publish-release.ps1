param([string]$Repository = 'enthilza/eFaktura-Viewer')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskVersion = ([xml](Get-Content (Join-Path $taskRoot 'src/eFakturaViewer/eFakturaViewer.csproj'))).Project.PropertyGroup.Version
$taskDist = Join-Path $taskRoot 'dist'
# Poslednu kontrolu robi samotna distribuovana aplikacia svojim zabudovanym verejnym klucom.
# Neopierame sa iba o uspech podpisovacieho skriptu alebo pritomnost suborov.
$taskVerify = Start-Process (Join-Path $taskDist 'eFakturaViewer.exe') -ArgumentList @('--verify-package', ('"' + $taskDist + '"')) -Wait -PassThru -WindowStyle Hidden
if ($taskVerify.ExitCode -ne 0) { throw 'Podpis vydania nie je platny.' }
# Vydanie viazeme na commit, aby sa dalo dohladat, z ktorych sledovanych zdrojov vzniklo.
& git -C $taskRoot diff --quiet HEAD
if ($LASTEXITCODE -ne 0) { throw 'Pred vydanim commitnite vsetky sledovane zmeny.' }
$taskCommit = & git -C $taskRoot rev-parse HEAD
& gh release create "v$taskVersion" (Join-Path $taskDist 'eFakturaViewer.exe') (Join-Path $taskDist 'update.json') (Join-Path $taskDist 'update.sig') --repo $Repository --target $taskCommit --title "eFaktúra Viewer $taskVersion" --notes-file (Join-Path $taskRoot 'release-notes.md')
if ($LASTEXITCODE -ne 0) { throw 'Vydanie sa nepodarilo publikovat.' }
# Starsie vydania odstranime az po overeni bajtov stiahnutych z noveho vydania.
# Tagy ostavaju v Git historii; mazeme vydania a ich binarne prilohy, nie zdrojovy kod.
$taskCheckDir = Join-Path ([IO.Path]::GetTempPath()) ('efaktura-release-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $taskCheckDir | Out-Null
try {
    & gh release download "v$taskVersion" --repo $Repository --dir $taskCheckDir --pattern eFakturaViewer.exe --pattern update.json --pattern update.sig
    if ($LASTEXITCODE -ne 0) { throw 'Stiahnutie publikovaneho balika zlyhalo. Starsie vydania zostali zachovane.' }
    $taskRemoteVerify = Start-Process (Join-Path $taskDist 'eFakturaViewer.exe') -ArgumentList @('--verify-package', ('"' + $taskCheckDir + '"')) -Wait -PassThru -WindowStyle Hidden
    if ($taskRemoteVerify.ExitCode -ne 0) { throw 'Publikovany balik nepresiel overenim podpisu. Starsie vydania zostali zachovane.' }
    $taskProbe = Start-Process (Join-Path $taskCheckDir 'eFakturaViewer.exe') -ArgumentList '--probe' -PassThru -WindowStyle Hidden
    if (!$taskProbe.WaitForExit(30000)) { $taskProbe.Kill(); $taskProbe.WaitForExit(); throw 'Publikovany EXE nepresiel kontrolou startu.' }
    if ($taskProbe.ExitCode -ne 0) { throw 'Publikovany EXE nepresiel kontrolou startu.' }
    $taskReleasesJson = & gh release list --repo $Repository --limit 1000 --json tagName,isDraft,isPrerelease
    if ($LASTEXITCODE -ne 0) { throw 'Nepodarilo sa nacitat zoznam vydani.' }
    foreach ($taskRelease in ($taskReleasesJson | ConvertFrom-Json)) {
        if ($taskRelease.isDraft -or $taskRelease.isPrerelease -or $taskRelease.tagName -notmatch '^v(\d+\.\d+\.\d+)$') { continue }
        if ([version]$Matches[1] -ge [version]$taskVersion) { continue }
        & gh release delete $taskRelease.tagName --repo $Repository --yes
        if ($LASTEXITCODE -ne 0) { throw ('Nepodarilo sa odstranit stare vydanie ' + $taskRelease.tagName) }
    }
} finally {
    foreach ($taskFile in @('eFakturaViewer.exe', 'update.json', 'update.sig')) { Remove-Item -LiteralPath (Join-Path $taskCheckDir $taskFile) -Force -ErrorAction SilentlyContinue }
    Remove-Item -LiteralPath $taskCheckDir -ErrorAction SilentlyContinue
}

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
& gh release create "v$taskVersion" (Join-Path $taskDist 'eFakturaViewer.exe') (Join-Path $taskDist 'update.json') (Join-Path $taskDist 'update.sig') --repo $Repository --target $taskCommit --title "eFaktura Viewer $taskVersion" --notes-file (Join-Path $taskRoot 'release-notes.md')
if ($LASTEXITCODE -ne 0) { throw 'Vydanie sa nepodarilo publikovat.' }

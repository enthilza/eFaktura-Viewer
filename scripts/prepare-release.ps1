param([switch]$InitializeKey)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Pouzivatel vydava z viacerych PC: kluc cestuje s projektom cez Nextcloud.
# Prenosny RSA XML nema vazbu na ucet Windows; cely .secrets je vyluceny z Gitu.
$taskKeyDir = Join-Path $taskRoot '.secrets'
$taskKeyPath = Join-Path $taskKeyDir 'signing-key.xml'
$taskPublic = Join-Path $taskRoot 'src/eFakturaViewer/Assets/update-public-key.xml'
$taskRsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
$taskRsa.PersistKeyInCsp = $false
if (!(Test-Path -LiteralPath $taskKeyPath)) {
    # Stratu kluca nesmieme vyriesit automatickym vytvorenim noveho: nainstalovane EXE
    # doveruju povodnemu verejnemu klucu a dalsie aktualizacie by potom odmietli.
    if (!$InitializeKey -or (Test-Path $taskPublic)) { throw 'Chyba povodny sukromny kluc. Existujuci verejny kluc sa nesmie vymenit.' }
    New-Item -ItemType Directory -Force $taskKeyDir | Out-Null
    $taskSecret = [Text.Encoding]::UTF8.GetBytes($taskRsa.ToXmlString($true))
    try { [IO.File]::WriteAllBytes($taskKeyPath,$taskSecret) }
    finally { [Array]::Clear($taskSecret,0,$taskSecret.Length) }
} else {
    $taskRsa.FromXmlString([IO.File]::ReadAllText($taskKeyPath))
}
if (Test-Path $taskPublic) { if ([IO.File]::ReadAllText($taskPublic).Trim() -ne $taskRsa.ToXmlString($false)) { throw 'Sukromny a verejny kluc nesuhlasia.' } }
else { [IO.File]::WriteAllText($taskPublic,$taskRsa.ToXmlString($false),[Text.UTF8Encoding]::new($false)) }
if ($InitializeKey) { $taskRsa.Dispose(); Write-Host 'Kluc pripraveny v .secrets/signing-key.xml (prenosny cez Nextcloud, mimo Gitu).'; exit 0 }
$taskExe = Join-Path $taskRoot 'dist/eFakturaViewer.exe'
$taskVersion = ([xml](Get-Content (Join-Path $taskRoot 'src/eFakturaViewer/eFakturaViewer.csproj'))).Project.PropertyGroup.Version
# Stary build nesmie dostat podpis novsej verzie iba preto, ze uz bolo upravene csproj.
$taskBinaryVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskExe).ProductVersion
if (!$taskBinaryVersion -or $taskBinaryVersion.Split('+')[0] -ne $taskVersion) { throw 'Verzia hotoveho EXE nesuhlasi s projektom. Najprv vykonajte novy build.' }
$taskStream = [IO.File]::OpenRead($taskExe)
# Podpisujeme hotovy EXE. Akykolvek dalsi build po tomto kroku vyzaduje novy podpis.
try { $taskSignature = [Convert]::ToBase64String($taskRsa.SignData($taskStream,[Security.Cryptography.SHA256]::Create())) } finally { $taskStream.Dispose() }
$taskManifest = [ordered]@{ Version=$taskVersion; Tag="v$taskVersion"; File='eFakturaViewer.exe'; Runtime='win-x64'; Size=(Get-Item $taskExe).Length; Signature=$taskSignature } | ConvertTo-Json
$taskManifestPath = Join-Path $taskRoot 'dist/update.json'
[IO.File]::WriteAllText($taskManifestPath,$taskManifest,[Text.UTF8Encoding]::new($false))
# Druhy podpis chrani aj verziu, velkost a podpis EXE v manifeste. Berieme presne ulozene
# bajty; preformatovanie JSON po podpisani by overenie pokazilo.
$taskManifestSignature = [Convert]::ToBase64String($taskRsa.SignData([IO.File]::ReadAllBytes($taskManifestPath),[Security.Cryptography.SHA256]::Create()))
[IO.File]::WriteAllText((Join-Path $taskRoot 'dist/update.sig'),$taskManifestSignature,[Text.UTF8Encoding]::new($false))
$taskRsa.Dispose()
Write-Host 'Podpisane vydanie pripravene. Kontrolny sucet sa nepublikuje.'

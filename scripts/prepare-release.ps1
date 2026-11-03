param([switch]$InitializeKey)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
# Kluc nie je sucastou projektu ani Nextcloud kopie. DPAPI ho viaze na tento ucet Windows;
# prenos samotneho suboru na iny pocitac nie je pouzitelna zaloha podpisovacej identity.
$taskKeyDir = Join-Path $env:LOCALAPPDATA 'eFakturaViewerPublisher'
$taskKeyPath = Join-Path $taskKeyDir 'signing-key.dpapi'
$taskPublic = Join-Path $taskRoot 'src/eFakturaViewer/Assets/update-public-key.xml'
$taskRsa = [Security.Cryptography.RSACryptoServiceProvider]::new(3072)
$taskRsa.PersistKeyInCsp = $false
if (!(Test-Path -LiteralPath $taskKeyPath)) {
    # Stratu kluca nesmieme vyriesit automatickym vytvorenim noveho: nainstalovane EXE
    # doveruju povodnemu verejnemu klucu a dalsie aktualizacie by potom odmietli.
    if (!$InitializeKey -or (Test-Path $taskPublic)) { throw 'Chyba povodny sukromny kluc. Existujuci verejny kluc sa nesmie vymenit.' }
    New-Item -ItemType Directory -Force $taskKeyDir | Out-Null
    $taskSecret = [Text.Encoding]::UTF8.GetBytes($taskRsa.ToXmlString($true))
    [IO.File]::WriteAllBytes($taskKeyPath,[Security.Cryptography.ProtectedData]::Protect($taskSecret,$null,[Security.Cryptography.DataProtectionScope]::CurrentUser))
    [Array]::Clear($taskSecret,0,$taskSecret.Length)
} else {
    $taskSecret = [Security.Cryptography.ProtectedData]::Unprotect([IO.File]::ReadAllBytes($taskKeyPath),$null,[Security.Cryptography.DataProtectionScope]::CurrentUser)
    $taskRsa.FromXmlString([Text.Encoding]::UTF8.GetString($taskSecret)); [Array]::Clear($taskSecret,0,$taskSecret.Length)
}
if (Test-Path $taskPublic) { if ([IO.File]::ReadAllText($taskPublic).Trim() -ne $taskRsa.ToXmlString($false)) { throw 'Sukromny a verejny kluc nesuhlasia.' } }
else { [IO.File]::WriteAllText($taskPublic,$taskRsa.ToXmlString($false),[Text.UTF8Encoding]::new($false)) }
if ($InitializeKey) { $taskRsa.Dispose(); Write-Host 'Kluc pripraveny. Sukromna cast je mimo projektu.'; exit 0 }
$taskExe = Join-Path $taskRoot 'dist/eFakturaViewer.exe'
$taskVersion = ([xml](Get-Content (Join-Path $taskRoot 'src/eFakturaViewer/eFakturaViewer.csproj'))).Project.PropertyGroup.Version
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

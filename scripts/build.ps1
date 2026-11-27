param([switch]$NoPause, [switch]$Verify, [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [string]$OutputDirectory)
# Build jedneho samostatneho EXE (self-contained, single-file). Cielovy pocitac nepotrebuje .NET.
# V projekte po builde ostane IBA eFakturaViewer.exe v jeho koreni. Projekt je v Nextcloude, preto vsetko
# docasne vznika mimo neho v C:\Temp\eFakturaViewer (ak neexistuje, vytvori sa):
#   build\         bin/obj/publish - VZDY sa zmaze, aj po chybe
#   test-results\  self-test.json + PNG nahlady pri -Verify (ostava do dalsieho buildu na kontrolu)
#   release\       podpisany balik z prepare-release.ps1 (zmaze ho publish-release.ps1)
# Pozostatky starsich buildov v projekte (.build, dist, test-results, publish, bin, obj) sa mazu
# pri kazdom builde bez pytania.
$ErrorActionPreference = 'Stop'
$AppName = 'eFakturaViewer'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskTemp = "C:\Temp\$AppName"            # rovnaka cesta je v Directory.Build.props
$taskBuild = Join-Path $taskTemp 'build'
$taskDist = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory) } else { $taskRoot }
$taskProject = Join-Path $taskRoot "src\$AppName\$AppName.csproj"
$taskResult = 1
$taskOldCli = $env:DOTNET_CLI_HOME
$taskOldNuget = $env:NUGET_PACKAGES

# Zmaze presne jeden vlastny priecinok. Nikdy cez junction/symlink (ten by mazal ciel inde).
function Remove-OwnDirectory([string]$Path) {
    $Path = [IO.Path]::GetFullPath($Path)
    $taskAllowedTemp = @((Join-Path $taskTemp 'build'), (Join-Path $taskTemp 'test-results'))
    $taskRootNames = @('.build', 'dist', 'test-results', 'publish', '.publish', 'bin', 'obj')
    $taskIsRootLeftover = (Split-Path $Path -Parent) -eq $taskRoot -and (Split-Path $Path -Leaf) -in $taskRootNames
    $taskIsProjectOutput = $Path.StartsWith((Join-Path $taskRoot 'src') + '\', [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $Path -Leaf) -in @('bin','obj')
    if ($Path -notin $taskAllowedTemp -and !$taskIsRootLeftover -and !$taskIsProjectOutput) { throw 'Nepovolena cesta upratovania.' }
    if (!(Test-Path -LiteralPath $Path)) { return }
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
public static class BuildPaths {
    [StructLayout(LayoutKind.Sequential)] struct TagInfo { public uint Attributes; public uint Tag; }
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError=true)]
    static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int infoClass, out TagInfo info, uint size);
    public static bool Redirects(string path) {
        using (var file = CreateFile(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero)) {
            TagInfo info;
            if (file.IsInvalid || !GetFileInformationByHandleEx(file, 9, out info, 8)) throw new Win32Exception(Marshal.GetLastWin32Error());
            return (info.Tag & 0x20000000) != 0;
        }
    }
}
'@ -ErrorAction SilentlyContinue
    # Nextcloud oznacuje bezne subory ako cloud reparse pointy. Mazanie blokuju iba name-surrogate
    # tagy (junction/symlink), ktore presmerovavaju cestu inam.
    $items = @(Get-Item -LiteralPath $Path -Force) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force -Attributes ReparsePoint)
    foreach ($item in $items) {
        if ((([int]$item.Attributes) -band 1024) -ne 0 -and [BuildPaths]::Redirects($item.FullName)) { throw "$Path obsahuje presmerovanie (junction/symlink); automaticke mazanie zastavene." }
    }
    Remove-Item -LiteralPath $Path -Recurse -Force
}

try {
    if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Na build treba .NET 10 SDK (cielove PC .NET nepotrebuje).' }
    $taskSdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or [int]($taskSdk.Split('.')[0]) -lt 10) { throw 'Treba .NET 10 SDK alebo novsi.' }
    if (!(Test-Path -LiteralPath (Join-Path $taskRoot "src\$AppName\Assets\update-public-key.xml"))) { throw 'Chyba verejny aktualizacny kluc. Spustite scripts\prepare-release.ps1 -InitializeKey (iba pri prvom vydani novej aplikacie).' }
    $taskExeOut = Join-Path $taskDist "$AppName.exe"
    if (Test-Path -LiteralPath $taskExeOut) {
        # Bezici EXE sa neda prepisat. Aplikaciu NIKDY neukoncujeme (moze mat neulozenu pracu).
        try { [IO.File]::Open($taskExeOut, 'Open', 'ReadWrite', 'None').Dispose() }
        catch { throw "$taskExeOut je zamknuty (aplikacia bezi?). Ukoncite ju alebo pouzite -OutputDirectory <iny priecinok>." }
    }
    # Cestu ku globalnej NuGet cache zistime PRED presmerovanim DOTNET_CLI_HOME do docasneho
    # priecinka, inak by sa balicky stahovali tam a po kazdom builde mazali.
    $taskPackageCache = & dotnet nuget locals global-packages --list --force-english-output
    if ($LASTEXITCODE -ne 0 -or $taskPackageCache -notmatch '^global-packages: ') { throw 'Nepodarilo sa zistit NuGet cache.' }
    $env:NUGET_PACKAGES = ($taskPackageCache -replace '^global-packages:\s*', '').Trim()
    New-Item -ItemType Directory -Force -Path $taskBuild, $taskDist | Out-Null
    $env:DOTNET_CLI_HOME = Join-Path $taskBuild 'cli'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_NOLOGO = '1'
    $taskIcon = Join-Path $taskRoot "src\$AppName\Assets\app.ico"
    if (!(Test-Path -LiteralPath $taskIcon) -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'make-icon.ps1'))) {
        Write-Host 'Vytvaram ikonu...' -ForegroundColor Cyan
        & (Join-Path $PSScriptRoot 'make-icon.ps1')
    }
    $taskPublish = Join-Path $taskBuild 'publish'
    Write-Host "Build $AppName.exe ($Runtime)..." -ForegroundColor Cyan
    & dotnet publish $taskProject -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded -p:NuGetAudit=false -o $taskPublish --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build zlyhal (exit $LASTEXITCODE)." }
    $taskExe = Join-Path $taskPublish "$AppName.exe"
    if (!(Test-Path -LiteralPath $taskExe)) { throw 'Vystupny EXE chyba.' }
    Copy-Item -LiteralPath $taskExe -Destination $taskExeOut -Force
    if ($Verify) {
        # Kontrolujeme priamo distribuovany EXE vratane pribalenych zdrojov. Vysledky su mimo projektu.
        $taskTestDir = Join-Path $taskTemp 'test-results'
        # Stary report ani stare nahlady nesmu zamaskovat predcasne ukoncenie noveho testu.
        Remove-OwnDirectory $taskTestDir
        New-Item -ItemType Directory -Force -Path $taskTestDir | Out-Null
        Write-Host 'Kontrolujem hotovy EXE...' -ForegroundColor Cyan
        $taskTest = Start-Process -FilePath $taskExeOut -ArgumentList @('--self-test', ('"' + $taskTestDir + '"'), ('"' + (Join-Path $taskRoot 'examples') + '"')) -PassThru -WindowStyle Hidden
        if (!$taskTest.WaitForExit(300000)) { $taskTest.Kill(); throw 'Kontroly prekrocili 5 minut.' }
        if ($taskTest.ExitCode -ne 0) { throw "Kontroly zlyhali. Pozrite $taskTestDir\failure.txt" }
        $taskReport = Get-Content -LiteralPath (Join-Path $taskTestDir 'self-test.json') -Raw | ConvertFrom-Json
        $taskExpected = ([xml](Get-Content -LiteralPath $taskProject)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        if ($taskReport.version -ne $taskExpected) { throw 'Report kontrol nepatri zostavovanej verzii.' }
        Write-Host ('Uspesne kontroly: ' + $taskReport.checks.Count + '; runtime ' + $taskReport.runtime) -ForegroundColor Green
        foreach ($taskSkipped in $taskReport.skipped) { Write-Host "Neoverene v tomto prostredi: $taskSkipped" -ForegroundColor Yellow }
        Write-Host "Report a nahlady okien: $taskTestDir"
    }
    $taskSize = [Math]::Round((Get-Item -LiteralPath $taskExeOut).Length / 1MB, 1)
    Write-Host "Hotovo: $taskExeOut ($taskSize MB)" -ForegroundColor Green
    $taskResult = 0
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
} finally {
    $env:DOTNET_CLI_HOME = $taskOldCli
    $env:NUGET_PACKAGES = $taskOldNuget
    # Mazeme IBA presny vlastny adresar C:\Temp\<App>\build.
    if ($taskBuild -eq "C:\Temp\$AppName\build") {
        try { Remove-OwnDirectory $taskBuild; Write-Host 'Docasne build subory zmazane.' }
        catch { Write-Host "Upratovanie: $($_.Exception.Message)" -ForegroundColor Yellow; $taskResult = 1 }
    }
    # Pozostatky buildu v projekte (starsie rozlozenie, IDE, rucny dotnet build): do Nextcloudu
    # nepatria, mazu sa bez pytania. Iba tieto presne nazvy - nic sa neodhaduje podla vzoru.
    $taskLeftovers = @('.build', 'dist', 'test-results', 'publish', '.publish', 'bin', 'obj') | ForEach-Object { Join-Path $taskRoot $_ }
    foreach ($taskCsproj in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Recurse -Filter *.csproj -File -ErrorAction SilentlyContinue) {
        $taskLeftovers += (Join-Path $taskCsproj.DirectoryName 'bin'), (Join-Path $taskCsproj.DirectoryName 'obj')
    }
    foreach ($taskLeftover in $taskLeftovers) {
        if (!(Test-Path -LiteralPath $taskLeftover)) { continue }
        # -OutputDirectory moze ukazovat prave do jedneho z tychto priecinkov; vysledok buildu nemazeme.
        if ($taskDist.StartsWith($taskLeftover, [StringComparison]::OrdinalIgnoreCase)) { continue }
        try { Remove-OwnDirectory $taskLeftover; Write-Host "Zmazany pozostatok buildu: $taskLeftover" }
        catch { Write-Host "Pozostatok buildu $taskLeftover sa nepodarilo zmazat (bezi z neho aplikacia?): $($_.Exception.Message)" -ForegroundColor Yellow }
    }
}
if (!$NoPause) { [void](Read-Host 'Stlacte Enter') }
exit $taskResult

<#
  build.ps1: makes out\Cadence Installer.exe, one native file that needs nothing installed on the PC, and the
  files a release attaches, in out\release: the same exe under its name, Cadence-Installer.exe, and under the old
  name, Get-Bradleys-apps.exe (old links point at .../releases/latest/download/Get-Bradleys-apps.exe, and GitHub
  does not redirect a renamed file), with SHA256SUMS.txt listing both. It publishes nothing and uploads nothing.

  Needs: the .NET 10 SDK and Visual Studio's "Desktop development with C++" (the native build uses its linker).
  Run:   pwsh -File build.ps1                       (finds dotnet on PATH, or names it with -Dotnet)
         pwsh -File build.ps1 -Check                (then downloads and checks Cadence into work\check, installing nothing)
         pwsh -File build.ps1 -Unsigned             (a local trial: the exe is not signed)

  Signing: the exe is signed with Azure Artifact Signing (Microsoft's signtool, its Artifact Signing client, and an
  Azure CLI sign-in, az login). The account and profile are in work\signing.json, which is not in git; its shape is
  Microsoft's metadata.json (Endpoint, CodeSigningAccountName, CertificateProfileName).
#>
param(
    [string]$Dotnet,
    [switch]$Check,
    [switch]$Unsigned
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root
if (-not $Dotnet) { $Dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source }
if (-not $Dotnet -or -not (Test-Path -LiteralPath $Dotnet)) { throw 'No dotnet found: install the .NET 10 SDK or pass -Dotnet <path to dotnet.exe>.' }
$env:DOTNET_CLI_HOME = "$root\work\dotnet-home"; $env:NUGET_PACKAGES = "$root\work\nuget"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'

$publish = Join-Path $root 'work\publish'
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force -Confirm:$false }
# The native build loads Visual Studio's C++ tools through vcvarsall.bat, which calls vswhere.exe by name: without
# its folder on PATH, the "not recognized" message lands inside the linker's path and the build fails.
$env:PATH = (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer') + ";$env:PATH"
& $Dotnet publish src\BradleysApps.csproj -c Release -o $publish
if ($LASTEXITCODE -ne 0) { throw 'The build failed.' }

$out = Join-Path $root 'out'
New-Item -ItemType Directory -Force $out | Out-Null
$exe = Join-Path $out 'Cadence Installer.exe'
Copy-Item (Join-Path $publish 'CadenceInstaller.exe') $exe -Force
# only the exe goes out: it carries Skia and HarfBuzz inside (the loose copies beside the build are not needed)

# names of this PC's folders must not be inside a file that goes to other people
$bytes = [IO.File]::ReadAllBytes($exe)
$text = [Text.Encoding]::ASCII.GetString($bytes) + [Text.Encoding]::Unicode.GetString($bytes)
# (the user name as a folder, "\name\": as a bare word it also turns up in ordinary text tables)
$private = [ordered]@{ 'the user folder' = "\$env:USERNAME\"; 'the project folder' = $root; 'the project''s parent folder' = (Split-Path $root -Parent) }
# more words to look for, one per line, kept out of the source: work\private-words.txt (not in git)
$words = Join-Path $root 'work\private-words.txt'
if (Test-Path $words) { Get-Content $words | Where-Object { $_.Trim() } | ForEach-Object { $private["a word from private-words.txt"+ $_.GetHashCode()] = $_.Trim() } }
foreach ($k in $private.Keys) {
    $label = $k -replace '\-?\d+$', ''
    if ($text.IndexOf($private[$k], [StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "The exe names $label. It must not go to anyone like this." }
}

if (-not $Unsigned) {
    $metadata = Join-Path $root 'work\signing.json'
    if (-not (Test-Path $metadata)) { throw 'work\signing.json is missing: the signing account and profile go there (or build with -Unsigned for a trial).' }
    $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\signtool.exe' -ErrorAction SilentlyContinue |
        Sort-Object { [version]($_.Directory.Parent.Name) } -Descending | Select-Object -First 1
    if (-not $signtool) { throw 'signtool.exe was not found: install the Windows SDK.' }
    $dlib = Join-Path $env:LOCALAPPDATA 'Microsoft\MicrosoftArtifactSigningClientTools\Azure.CodeSigning.Dlib.dll'
    if (-not (Test-Path $dlib)) { throw 'The Artifact Signing client was not found: winget install --id Microsoft.Azure.ArtifactSigningClientTools -e' }
    $cli = Join-Path $env:ProgramFiles 'Microsoft SDKs\Azure\CLI2\wbin'
    if (-not (Get-Command az -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $cli 'az.cmd'))) { $env:PATH = "$cli;$env:PATH" }
    & $signtool.FullName sign /fd SHA256 /tr 'http://timestamp.acs.microsoft.com' /td SHA256 /dlib $dlib /dmdf $metadata $exe | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Signing failed (signtool exit $LASTEXITCODE). Is 'az login' still signed in?" }
    $signature = Get-AuthenticodeSignature -LiteralPath $exe
    if ($signature.Status -ne 'Valid') { throw "The signed exe does not verify: $($signature.Status)." }
    "Signed by $($signature.SignerCertificate.Subject -replace ',.*', '')"
}

$size = (Get-Item $exe).Length
$hash = (Get-FileHash $exe -Algorithm SHA256).Hash.ToLowerInvariant()
"Built: $exe"
'{0:N1} MB, SHA-256 {1}' -f ($size / 1MB), $hash

# what a release attaches: one exe, under the new name and the old one, so every old download link keeps working
$release = Join-Path $out 'release'
if (Test-Path $release) { Remove-Item $release -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force $release | Out-Null
$names = 'Cadence-Installer.exe', 'Get-Bradleys-apps.exe'
foreach ($n in $names) { Copy-Item $exe (Join-Path $release $n) }
$sums = foreach ($n in $names) { '{0}  {1}' -f (Get-FileHash (Join-Path $release $n) -Algorithm SHA256).Hash.ToLowerInvariant(), $n }
[IO.File]::WriteAllText((Join-Path $release 'SHA256SUMS.txt'), ($sums -join "`n") + "`n")
if (@($sums | ForEach-Object { $_.Substring(0, 64) } | Select-Object -Unique).Count -ne 1) { throw 'The two release files differ.' }
"Release files: $release ($($names -join ', '), SHA256SUMS.txt)"

if ($Check) {
    $checkDir = Join-Path $root 'work\check'
    & $exe --check $checkDir | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'The check found something wrong (see above).' }
}

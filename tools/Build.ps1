param([string[]]$Versions = @('1.5','1.6','1.7'), [string]$Configuration = 'Release', [string]$GameDir = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $root 'artifacts\RimTube-0.2.0'
# Fail before updating any version when a running isolated game has mapped the package DLL.
foreach ($v in $Versions) {
    $dll = Join-Path $package "$v\Assemblies\RimTube.dll"
    if (Test-Path -LiteralPath $dll) {
        try { $probe = [IO.File]::Open($dll, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $probe.Dispose() }
        catch { throw "Close the game using this local package before rebuilding: $dll" }
    }
}
foreach ($v in $Versions) {
    if ($v -notin @('1.5','1.6','1.7')) { throw "Unsupported version: $v" }
    [string[]]$gameArguments = if ($GameDir) { @("-p:GameDir=$GameDir") } else { @() }
    & dotnet build (Join-Path $root 'src\RimTube.csproj') -c $Configuration "-p:GameVersion=$v" "-p:BaseIntermediateOutputPath=obj/$v/" @gameArguments --nologo -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $v" }
    $out = Join-Path $root "artifacts\RimTube-0.2.0\$v\Assemblies"
    New-Item -ItemType Directory -Path $out -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $root "artifacts\build\$v\RimTube.dll") -Destination $out
}
Copy-Item -LiteralPath (Join-Path $root 'mod\About') -Destination $package -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'mod\Common') -Destination $package -Recurse -Force
Copy-Item -LiteralPath (Join-Path $root 'mod\LoadFolders.xml') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $root 'mod\README.txt') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSE-ASSETS.md') -Destination $package -Force
Copy-Item -LiteralPath (Join-Path $root 'LICENSES') -Destination $package -Recurse -Force
Write-Output "Local package: $package"

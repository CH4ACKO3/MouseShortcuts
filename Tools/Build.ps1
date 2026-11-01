param([string]$GameDir = $env:PROBABLY_STOLEN_DIR, [string]$ManagedDir, [string]$Tag)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot
if (!$ManagedDir) { $ManagedDir = Join-Path $GameDir 'Probably Stolen_Data/Managed' }
$ManagedDir = (Resolve-Path -LiteralPath $ManagedDir).Path
$project = [xml](Get-Content "$root/source/RightClickClose.csproj" -Raw)
$version = $project.Project.PropertyGroup.Version
$mod = [xml](Get-Content "$root/manifest.xml" -Raw)
if ($mod.Mod.ID -cne 'ch4acko3_right_click_close' -or $mod.Mod.ModVersion -cne $version) { throw 'Manifest/project version or mod ID mismatch.' }
if ($Tag -and $Tag -cne "v$version") { throw 'Release tag must match the project and manifest version.' }
foreach ($reference in $project.Project.ItemGroup.Reference) {
    if (!(Test-Path -LiteralPath "$ManagedDir/$($reference.Include).dll")) { throw "Missing reference: $($reference.Include)" }
}
dotnet build "$root/source/RightClickClose.csproj" -c Release -o "$root/artifacts/build" "-p:ManagedDir=$ManagedDir" -p:EnableNETAnalyzers=true -p:AnalysisLevel=latest -warnaserror
if ($LASTEXITCODE) { throw 'Build failed.' }
$package = "$root/artifacts/package"
if (Test-Path -LiteralPath $package) { throw 'Package directory already exists. Use a clean checkout or move artifacts/package before rebuilding.' }
$stage = "$package/RightClickClose"
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Copy-Item "$root/artifacts/build/RightClickClose.dll", "$root/manifest.xml", "$root/使用说明.txt", "$root/LICENSE" $stage
Copy-Item "$root/docs/Description.zh-CN.bbcode", "$root/workshop.json" $package
Copy-Item "$root/releases/$version.md" "$package/release-notes.md"
$archive = "MouseShortcuts-$version.zip"
Compress-Archive -LiteralPath $stage -DestinationPath "$package/$archive"
$files = @(Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object { @{path=$_.Name; sha256=(Get-FileHash -LiteralPath $_.FullName).Hash} })
@{version=$version; tag=$Tag; archive=$archive; archiveSha256=(Get-FileHash "$package/$archive").Hash; files=$files;
  descriptionSha256=(Get-FileHash "$package/Description.zh-CN.bbcode").Hash;
  notesSha256=(Get-FileHash "$package/release-notes.md").Hash;
  workshopSha256=(Get-FileHash "$package/workshop.json").Hash} | ConvertTo-Json -Depth 4 | Set-Content "$package/package.json" -Encoding utf8NoBOM
Write-Host "Package ready: $package/$archive"

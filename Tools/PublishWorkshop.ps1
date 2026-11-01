param([Parameter(Mandatory)][string]$ArtifactRoot, [switch]$DryRun, [switch]$CheckOnly, [switch]$CreatePrivate)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$m = Get-Content "$root/package.json" -Raw | ConvertFrom-Json
$w = Get-Content "$root/workshop.json" -Raw | ConvertFrom-Json
if ($w.appid -cne '4348910' -or $w.publishedfileid -notmatch '^(0|[1-9][0-9]+)$') { throw 'Invalid Workshop identity.' }
if ($m.version -notmatch '^\d+\.\d+\.\d+$' -or $m.archive -cne "MouseShortcuts-$($m.version).zip") { throw 'Invalid package version/archive.' }
if (!$DryRun -and !$CheckOnly) {
    if ($CreatePrivate) {
        if ($w.publishedfileid -cne '0') { throw 'Bootstrap requires an unset item ID.' }
    } elseif ($w.publishedfileid -eq '0' -or $m.tag -cne "v$($m.version)") { throw 'Updating Workshop requires a pinned item ID and release tag.' }
}
foreach ($entry in @(
    @($m.archive, $m.archiveSha256), @('Description.zh-CN.bbcode', $m.descriptionSha256),
    @('preview.png', $m.previewSha256),
    @('release-notes.md', $m.notesSha256), @('workshop.json', $m.workshopSha256))) {
    if ((Get-FileHash -LiteralPath "$root/$($entry[0])").Hash -cne $entry[1]) { throw "Checksum mismatch: $($entry[0])" }
}
if ((Get-Item "$root/preview.png").Length -ge 1MB) { throw 'Workshop preview must be smaller than 1 MB.' }
$stage = "$root/RightClickClose"
$expected = @('LICENSE','RightClickClose.dll','manifest.xml','使用说明.txt')
if (@(Get-ChildItem $stage -File -Recurse).Count -ne $expected.Count -or $m.files.Count -ne $expected.Count) { throw 'Unexpected package contents.' }
foreach ($name in $expected) {
    $files = @($m.files | Where-Object path -CEQ $name)
    if ($files.Count -ne 1 -or (Get-FileHash -LiteralPath "$stage/$name").Hash -cne $files[0].sha256) { throw "Invalid package file: $name" }
}
$mod = [xml](Get-Content "$stage/manifest.xml" -Raw)
if ($mod.Mod.ModVersion -cne $m.version -or $mod.Mod.ID -cne 'ch4acko3_right_click_close') { throw 'Mod manifest mismatch.' }
function Escape-Vdf([string]$value) { $value.Replace('\','\\').Replace('"','\"').Replace("`r",'') }
$fields = [ordered]@{
    appid=$w.appid; publishedfileid=$w.publishedfileid; contentfolder=$stage.Replace('\','/');
    previewfile="$root/preview.png".Replace('\','/');
    changenote=(Get-Content "$root/release-notes.md" -Raw)
}
if ($CreatePrivate) {
    $fields.title = $w.title
    $fields.description = Get-Content "$root/Description.zh-CN.bbcode" -Raw
    $fields.visibility = '2'
}
$vdf = '"workshopitem"' + "`n{`n"
foreach ($field in $fields.GetEnumerator()) { $vdf += '  "' + $field.Key + '" "' + (Escape-Vdf $field.Value) + '"' + "`n" }
$vdf += "}`n"
$vdfPath = "$root/workshop-upload.vdf"
[IO.File]::WriteAllText($vdfPath, $vdf, [Text.UTF8Encoding]::new($false))
if ($DryRun) { Write-Host 'Package checksums and Workshop payload validated; no upload.'; return }
foreach ($key in @('STEAM_USERNAME','STEAM_CONFIG_VDF_BASE64','STEAM_SESSION_KEY')) {
    if (![Environment]::GetEnvironmentVariable($key)) { throw "Missing secret: $key" }
}
. "$PSScriptRoot/SteamSession.ps1"
$steam = Join-Path ([IO.Path]::GetTempPath()) ('mouse-shortcuts-steam-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$steam/config" -Force | Out-Null
try {
    Invoke-WebRequest 'https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip' -OutFile "$steam/steamcmd.zip"
    Expand-Archive "$steam/steamcmd.zip" $steam
    $output = & "$steam/steamcmd.exe" '+quit' 2>&1
    if ($LASTEXITCODE -notin @(0,7)) { throw 'SteamCMD initialization failed.' }
    [IO.File]::WriteAllBytes("$steam/config/config.vdf", [Convert]::FromBase64String($env:STEAM_CONFIG_VDF_BASE64))
    Restore-SteamSession $steam
    # Capture login output: never emit Steam session tokens or credentials to Actions logs.
    Push-Location $steam
    try {
        $output = & "$steam/steamcmd.exe" '+@ShutdownOnFailedCommand' '1' '+@NoPromptForPassword' '1' '+login' $env:STEAM_USERNAME '+quit' 2>&1
        if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'Waiting for user info\.\.\.\s*OK') {
            throw 'Steam cached login failed. Refresh STEAM_CONFIG_VDF_BASE64 from an authenticated SteamCMD session; raw login output withheld.'
        }
        if ($CheckOnly) { Write-Host 'Steam cached login accepted; no Workshop upload.'; return }
        $output = & "$steam/steamcmd.exe" '+@ShutdownOnFailedCommand' '1' '+@NoPromptForPassword' '1' '+login' $env:STEAM_USERNAME '+workshop_build_item' $vdfPath '+quit' 2>&1
        $code = $LASTEXITCODE
        if ($CreatePrivate -and (Get-Content $vdfPath -Raw) -match '"publishedfileid"\s+"([1-9][0-9]+)"') {
            Set-Content "$root/created-workshop-id.txt" $Matches[1] -NoNewline
            Write-Host "Created private Workshop item: $($Matches[1]). Record this ID before retrying."
        }
        if ($code -ne 0) { throw "Workshop upload exited with code $code; inspect created-workshop-id.txt before any bootstrap retry." }
        $id = if ($CreatePrivate) { (Get-Content "$root/created-workshop-id.txt" -Raw).Trim() } else { $w.publishedfileid }
        $output = & "$steam/steamcmd.exe" '+@ShutdownOnFailedCommand' '1' '+@NoPromptForPassword' '1' '+login' $env:STEAM_USERNAME '+workshop_download_item' $w.appid $id 'validate' '+quit' 2>&1
        if ($LASTEXITCODE) { throw 'Post-upload Workshop download failed.' }
        $download = "$steam/steamapps/workshop/content/$($w.appid)/$id"
        if (@(Get-ChildItem $download -File -Recurse).Count -ne $m.files.Count) { throw 'Downloaded Workshop file count mismatch.' }
        foreach ($file in $m.files) {
            if ((Get-FileHash -LiteralPath "$download/$($file.path)").Hash -cne $file.sha256) { throw "Downloaded Workshop checksum mismatch: $($file.path)" }
        }
        Write-Host "Workshop $id uploaded and downloaded files verified."
    } finally { Pop-Location }
} finally {
    if (Test-Path "$steam/config/config.vdf") { Save-SteamSession $steam }
    $resolved = [IO.Path]::GetFullPath($steam)
    if ($resolved.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -and [IO.Path]::GetFileName($resolved) -match '^mouse-shortcuts-steam-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}

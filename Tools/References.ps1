param([ValidateSet('Seal','Open')][string]$Mode = 'Open', [Parameter(Mandatory)][string]$ManagedDir)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path $PSScriptRoot
$key = [Convert]::FromBase64String($env:BUILD_REFERENCES_KEY)
if ($key.Length -ne 32) { throw 'BUILD_REFERENCES_KEY must contain a random 256-bit key encoded as base64.' }
$path = "$root/.ci/references.bin"
$context = [Text.Encoding]::UTF8.GetBytes('MouseShortcuts build references v1')
$aes = [Security.Cryptography.AesGcm]::new($key, 16)
try {
    if ($Mode -eq 'Seal') {
        $memory = [IO.MemoryStream]::new()
        $zip = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $project = [xml](Get-Content "$root/source/RightClickClose.csproj" -Raw)
            foreach ($reference in $project.Project.ItemGroup.Reference) {
                $name = "$($reference.Include).dll"
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, "$ManagedDir/$name", $name) | Out-Null
            }
        } finally { $zip.Dispose() }
        $data = $memory.ToArray()
        $memory.Dispose()
        $nonce = [Security.Cryptography.RandomNumberGenerator]::GetBytes(12)
        $tag = [byte[]]::new(16)
        $cipher = [byte[]]::new($data.Length)
        $aes.Encrypt($nonce, $data, $cipher, $tag, $context)
        New-Item -ItemType Directory "$root/.ci" -Force | Out-Null
        [IO.File]::WriteAllBytes($path, [byte[]]($nonce + $tag + $cipher))
    } else {
        $envelope = [IO.File]::ReadAllBytes($path)
        if ($envelope.Length -lt 29) { throw 'Invalid encrypted reference bundle.' }
        $data = [byte[]]::new($envelope.Length - 28)
        $aes.Decrypt([byte[]]$envelope[0..11], [byte[]]$envelope[28..($envelope.Length-1)], [byte[]]$envelope[12..27], $data, $context)
        $memory = [IO.MemoryStream]::new($data, $false)
        $zip = [IO.Compression.ZipArchive]::new($memory, [IO.Compression.ZipArchiveMode]::Read)
        try {
            New-Item -ItemType Directory $ManagedDir -Force | Out-Null
            foreach ($entry in $zip.Entries) {
                if ($entry.FullName -cne $entry.Name -or !$entry.Name.EndsWith('.dll')) { throw 'Invalid reference entry.' }
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $ManagedDir $entry.Name), $true)
            }
        } finally { $zip.Dispose(); $memory.Dispose() }
    }
} finally { $aes.Dispose(); [Array]::Clear($key, 0, $key.Length) }

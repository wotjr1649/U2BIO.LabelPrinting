$ErrorActionPreference = 'Stop'
$packageRoot = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\U2BIO.BT003PROLabelPrinting\Sdk'
$expected = @{
    'x86\BlackfishNfcLib.dll' = 'fed09bae47835dcf78f58579e3cfb9382561799b369e54fd9fa0902e5b89dbf5'
    'x86\HFModule.dll' = 'bb2721801bc853ead75e647608c6b36a5a3a75f344095e4e29ffc35e822319d4'
    'x64\BlackfishNfcLib.dll' = '64c7874980cb0a774a766e299359bc9cba5dd66fd052d758f1d4d1678d8cf72c'
    'x64\HFModule.dll' = '542ebfbccca1f953b4dcafb2a1faed198707d8fbcdb0653c0fc121d3c273edf5'
}
foreach ($entry in $expected.GetEnumerator()) {
    $file = Join-Path $packageRoot $entry.Key
    if (!(Test-Path -LiteralPath $file -PathType Leaf)) { throw ('SDK asset missing: ' + $entry.Key) }
    if ((Get-Item -LiteralPath $file).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'SDK asset redirects' }
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() -ne $entry.Value) {
        throw ('SDK asset hash mismatch: ' + $entry.Key)
    }
}
'PASS: four repository SDK assets match fixed manufacturer hashes; no SDK execution'

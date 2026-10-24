$root = "D:\dw4-work\extracted"
$out  = "D:\dw4-work\inventario\arquivos.csv"
$rows = foreach ($f in Get-ChildItem -LiteralPath $root -Recurse -File | Sort-Object FullName) {
    $fs = [System.IO.File]::Open($f.FullName, 'Open', 'Read', 'Read')
    try {
        $buf = New-Object byte[] 65536
        $n = 0
        while ($n -lt $buf.Length) { $r = $fs.Read($buf, $n, $buf.Length - $n); if ($r -le 0) { break }; $n += $r }
    } finally { $fs.Close() }
    $counts = New-Object int[] 256
    for ($i = 0; $i -lt $n; $i++) { $counts[$buf[$i]]++ }
    $ent = 0.0
    foreach ($c in $counts) { if ($c -gt 0) { $p = $c / $n; $ent -= $p * [math]::Log($p, 2) } }
    $hn = [math]::Min(16, $n)
    $head = $buf[0..($hn - 1)]
    [pscustomobject]@{
        caminho      = $f.FullName.Substring($root.Length + 1)
        bytes        = $f.Length
        sha256       = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash
        hex16        = ($head | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
        ascii16      = -join ($head | ForEach-Object { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
        entropia64k  = [math]::Round($ent, 4)
    }
}
$rows | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8
Get-Content -LiteralPath $out

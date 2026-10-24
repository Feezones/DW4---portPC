param([string]$Name)
$ErrorActionPreference = 'Stop'
$inv = [Globalization.CultureInfo]::InvariantCulture
$path = "D:\dw4-work\extracted\files\$Name.afs"
$out  = "D:\dw4-work\inventario\afs_$Name.csv"
$rep  = "D:\dw4-work\inventario\afs_$Name.report.txt"
$fs = [IO.File]::Open($path, 'Open', 'Read', 'Read')
$br = New-Object IO.BinaryReader($fs)
$flen = $fs.Length
$L = New-Object Collections.Generic.List[string]

$fs.Position = 0
$magic = $br.ReadBytes(4)
$n = $br.ReadUInt32()
$off = New-Object 'uint32[]' $n
$sz  = New-Object 'uint32[]' $n
for ($i = 0; $i -lt $n; $i++) { $off[$i] = $br.ReadUInt32(); $sz[$i] = $br.ReadUInt32() }
$extraPos = $fs.Position
$nameOff = $br.ReadUInt32(); $nameSz = $br.ReadUInt32()

$L.Add("arquivo: $path  tamanho: $flen")
$L.Add(("magic: {0}  N: {1}  fim da tabela: 0x{2:X}" -f (($magic | % { '{0:X2}' -f $_ }) -join ' '), $n, $extraPos))

# 1. validacao
$incr = $true; $a800 = 0; $a20 = 0; $oob = 0; $overlap = 0; $zero = 0
$gaps = New-Object Collections.Generic.List[long]
for ($i = 0; $i -lt $n; $i++) {
    if ($off[$i] % 0x800 -eq 0) { $a800++ }
    if ($off[$i] % 0x20 -eq 0) { $a20++ }
    if ([long]$off[$i] + $sz[$i] -gt $flen) { $oob++ }
    if ($sz[$i] -eq 0) { $zero++ }
    if ($i -gt 0) {
        if ($off[$i] -le $off[$i-1]) { $incr = $false }
        $endPrev = [long]$off[$i-1] + $sz[$i-1]
        if ($off[$i] -lt $endPrev) { $overlap++ } else { $gaps.Add($off[$i] - $endPrev) }
    }
}
$L.Add("offsets estritamente crescentes: $incr")
$L.Add("alinhados a 0x800: $a800 / $n ; alinhados a 0x20: $a20 / $n")
$L.Add("offset+tamanho > tamanho do arquivo: $oob")
$L.Add("sobreposicoes com a entrada anterior: $overlap")
$L.Add("entradas com tamanho 0: $zero")
if ($gaps.Count) {
    $gm = $gaps | Measure-Object -Minimum -Maximum -Sum
    $L.Add(("lacunas entre entradas: min {0} max {1} soma {2}" -f $gm.Minimum, $gm.Maximum, $gm.Sum))
}
$L.Add(("primeira entrada: offset 0x{0:X}; ultima termina em 0x{1:X}" -f $off[0], ([long]$off[$n-1] + $sz[$n-1])))
$L.Add(("par extra em 0x{0:X}: offset 0x{1:X} tamanho {2} (N*0x30 = {3})" -f $extraPos, $nameOff, $nameSz, ($n * 0x30)))

# 2. tabela de nomes
$names = New-Object 'string[]' $n
$hasNames = $false
if ($nameOff -gt 0 -and ([long]$nameOff + $nameSz) -le $flen -and $nameSz -ge $n * 0x30) {
    $fs.Position = $nameOff
    $good = 0
    for ($i = 0; $i -lt $n; $i++) {
        $raw = $br.ReadBytes(32)
        $z = [Array]::IndexOf($raw, [byte]0); if ($z -lt 0) { $z = 32 }
        $s = [Text.Encoding]::ASCII.GetString($raw, 0, $z)
        $dt = 1..6 | % { $br.ReadUInt16() }
        $u = $br.ReadUInt32()
        $names[$i] = $s
        if ($s -match '^[\x21-\x7E][\x20-\x7E]*$') { $good++ }
        if ($i -lt 20) { $L.Add(("  [{0}] '{1}'  data/hora {2}  u32 {3}" -f $i, $s, ($dt -join ' '), $u)) }
    }
    $L.Add("nomes ASCII imprimiveis validos: $good / $n")
    $hasNames = $good -gt ($n / 2)
} else {
    $L.Add("par extra nao aponta para tabela de nomes valida")
    if ($nameOff -gt 0 -and $nameOff -lt $flen) {
        $fs.Position = $nameOff
        $b = $br.ReadBytes(64)
        $L.Add("64 bytes no offset: " + (($b | % { '{0:X2}' -f $_ }) -join ' '))
    }
}

# 3. csv por entrada
$rows = New-Object Collections.Generic.List[object]
$buf = New-Object byte[] 65536
for ($i = 0; $i -lt $n; $i++) {
    $len = [int][Math]::Min(65536, [long]$sz[$i])
    $hex = ''; $asc = ''; $ent = ''
    if ($len -gt 0 -and ([long]$off[$i] + $sz[$i]) -le $flen) {
        $fs.Position = $off[$i]
        $got = 0
        while ($got -lt $len) { $r = $fs.Read($buf, $got, $len - $got); if ($r -le 0) { break }; $got += $r }
        $counts = New-Object int[] 256
        for ($k = 0; $k -lt $got; $k++) { $counts[$buf[$k]]++ }
        $e = 0.0
        foreach ($c in $counts) { if ($c) { $p = $c / $got; $e -= $p * [Math]::Log($p, 2) } }
        $ent = $e.ToString('0.0000', $inv)
        $h = [Math]::Min(16, $got)
        $hb = $buf[0..($h - 1)]
        $hex = ($hb | % { '{0:X2}' -f $_ }) -join ' '
        $asc = -join ($hb | % { if ($_ -ge 32 -and $_ -lt 127) { [char]$_ } else { '.' } })
    }
    $rows.Add([pscustomobject]@{
        indice = $i; offset = ('0x{0:X}' -f $off[$i]); tamanho = $sz[$i]
        nome = $(if ($hasNames) { $names[$i] } else { '' })
        hex16 = $hex; ascii16 = $asc; entropia = $ent
    })
}
$fs.Close()
$rows | Export-Csv -LiteralPath $out -NoTypeInformation -Encoding UTF8

# 4. agrupamentos
$L.Add("--- por assinatura (4 bytes) ---")
$rows | Group-Object { if ($_.hex16) { $_.hex16.Substring(0, 11) } else { '(vazio)' } } | Sort-Object Count -Descending | % {
    $g = $_.Group
    $bytes = ($g | Measure-Object tamanho -Sum).Sum
    $es = $g | ? entropia | % { [double]::Parse($_.entropia, $inv) } | Measure-Object -Minimum -Maximum
    $asc4 = if ($g[0].ascii16) { $g[0].ascii16.Substring(0, [Math]::Min(4, $g[0].ascii16.Length)) } else { '' }
    $ex = ($g | Select-Object -First 3 | % { if ($_.nome) { $_.nome } else { "#$($_.indice)" } }) -join ', '
    $L.Add(("{0} '{1}': {2} entradas, {3} bytes, entropia {4}-{5}, ex: {6}" -f $_.Name, $asc4, $_.Count, $bytes,
        $(if ($es.Count) { $es.Minimum.ToString('0.00', $inv) } else { '-' }), $(if ($es.Count) { $es.Maximum.ToString('0.00', $inv) } else { '-' }), $ex))
}
if ($hasNames) {
    $L.Add("--- por extensao ---")
    $rows | Group-Object { $e = [IO.Path]::GetExtension($_.nome); if ($e) { $e.ToLower() } else { '(sem)' } } | Sort-Object Count -Descending | % {
        $L.Add(("{0}: {1} entradas, {2} bytes" -f $_.Name, $_.Count, ($_.Group | Measure-Object tamanho -Sum).Sum))
    }
}
$L | Set-Content -LiteralPath $rep -Encoding UTF8
$L

# 口径类规则的「唯一实现」校验（配合《规则索引.md》「唯一实现登记」）
#
# 校验三件事：
#   1) 登记表里的每条规则，在源码里必须**恰有一处** `// RULE:<ID>` 标记，且落在登记声明的文件；
#   2) 登记的 ID 必须存在于《规则索引.md》的规则表（不得登记不存在的规则）；
#   3) Vanilla / ThirdModCompat 不得出现重复的取用算式：
#      `Math.Min(…available)`、`GetCount(…) - …`、`… - GetReserved(…)`、`GetPreservedCount(…) - …`；
#      兼容层（ThirdModCompat）不得直接引用 SDS_Core / SDS_Ledger / SDS_Vanilla（E6 / R2）。
#
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckRuleSingleSource.ps1
#
# 退出码：0 = 通过；1 = 违规；2 = 找不到文档 / 解析失败。

param([string]$SourceRoot = '')

$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $srcDir)
$docsDir = Join-Path $root '超维存储框架'
if ($SourceRoot) { $srcDir = $SourceRoot }
$indexPath = Join-Path $docsDir '规则索引.md'

if (-not (Test-Path $indexPath)) { Write-Host "找不到 规则索引.md"; exit 2 }
if (-not (Test-Path $srcDir)) { Write-Host "找不到源码目录：$srcDir"; exit 2 }

$failed = 0
function Fail([string]$msg) { Write-Host $msg; $script:failed++ }

$lines = Get-Content $indexPath -Encoding UTF8

# ---------- 1) 主规则表 ID ----------
$mainIds = @{}
foreach ($line in $lines) {
    if ($line -match '^\|\s*([RWES]\d+[a-z]?)\s*\|') {
        $cells = $line.Trim().Trim('|').Split('|')
        if ($cells.Count -ge 5) { $mainIds[$Matches[1]] = $true }
    }
}
if ($mainIds.Count -eq 0) { Write-Host "规则索引里没有任何规则行"; exit 2 }

# ---------- 2) 唯一实现登记段 ----------
$inRegistry = $false
$registry = @()
foreach ($line in $lines) {
    if ($line -match '^##\s*唯一实现登记') { $inRegistry = $true; continue }
    if ($inRegistry -and $line -match '^##\s') { break }
    if (-not $inRegistry) { continue }
    if ($line -notmatch '^\|\s*([RWES]\d+[a-z]?)\s*\|') { continue }
    $cells = $line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 3) { continue }
    $registry += [pscustomobject]@{
        Id   = $cells[0]
        File = ($cells[1] -replace '`', '')
        Note = $cells[2]
    }
}
if ($registry.Count -eq 0) { Write-Host "唯一实现登记段为空或格式不符"; exit 2 }

# ---------- 3) 源码索引 ----------
$codeFiles = @(Get-ChildItem -LiteralPath $srcDir -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })

function NormalizeRel([string]$path) {
    return ($path -replace '/', '\') -replace '^\\+', ''
}

$list = @()
foreach ($row in $registry) {
    if (-not $mainIds.ContainsKey($row.Id)) {
        Fail "[登记未定义] $($row.Id)：规则索引主表没有该 ID"
        continue
    }
    $pattern = 'RULE:' + [regex]::Escape($row.Id) + '(?![A-Za-z0-9])'
    $hits = @()
    foreach ($file in $codeFiles) {
        $text = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8
        if ($text -and [regex]::IsMatch($text, $pattern)) { $hits += $file }
    }
    if ($hits.Count -eq 0) {
        Fail "[缺唯一标记] $($row.Id)：源码里没有 // $($row.Id) 的 RULE 标记"
        continue
    }
    if ($hits.Count -gt 1) {
        Fail ("[重复实现] {0}：RULE 标记出现 {1} 处（{2}）" -f $row.Id, $hits.Count,
            (($hits | ForEach-Object { $_.Name }) -join ', '))
        continue
    }
    $actual = NormalizeRel($hits[0].FullName.Substring($srcDir.Length))
    $expected = NormalizeRel($row.File)
    if ($actual -ne $expected) {
        Fail ("[落点不符] {0}：标记在 {1}，登记为 {2}" -f $row.Id, $actual, $expected)
        continue
    }
    $list += $row.Id
}

# ---------- 4) 重复取用算式（Vanilla / Compat） ----------
$scanDirs = @('SDS.Vanilla', 'SDS.ThirdModCompat')
$scanFiles = @($codeFiles | Where-Object {
    $rel = NormalizeRel($_.FullName.Substring($srcDir.Length))
    foreach ($d in $scanDirs) { if ($rel.StartsWith($d + '\')) { return $true } }
    return $false
})
$dupPatterns = @(
    @{ Re = 'Math\.Min\([^\r\n]*available'; Label = 'min(…, available)' },
    @{ Re = 'GetCount\([^)]*\)\s*-';          Label = 'GetCount(…) - …' },
    @{ Re = '-\s*GetReserved\(';              Label = '… - GetReserved(…)' },
    @{ Re = 'GetPreservedCount\([^)]*\)\s*-'; Label = 'GetPreservedCount(…) - …' }
)
foreach ($file in $scanFiles) {
    $fileLines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $fileLines.Count; $i++) {
        $code = $fileLines[$i]
        $comment = $code.IndexOf('//')
        if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
        if ([string]::IsNullOrWhiteSpace($code)) { continue }
        foreach ($p in $dupPatterns) {
            if ($code -match $p.Re) {
                Fail ("[重复算式] {0}:{1} → {2}（取用口径必须调用登记的唯一实现）" -f `
                    $file.Name, ($i + 1), $p.Label)
            }
        }
    }
}

# ---------- 5) 兼容层不得直连实现层（E6 / R2） ----------
$compatFiles = @($codeFiles | Where-Object {
    (NormalizeRel($_.FullName.Substring($srcDir.Length))).StartsWith('SDS.ThirdModCompat\')
})
foreach ($file in $compatFiles) {
    $fileLines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $fileLines.Count; $i++) {
        $code = $fileLines[$i]
        $comment = $code.IndexOf('//')
        if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
        if ([string]::IsNullOrWhiteSpace($code)) { continue }
        if ($code -match '(?<![\w.])SDS_Core\.|(?<![\w.])SDS_Ledger\b|(?<![\w.])SDS_Vanilla\b') {
            Fail ("[兼容直连] {0}:{1} → 只允许 SDS_API / SDS_Compat* / SDS_Diag / Contracts" -f `
                $file.Name, ($i + 1))
        }
    }
}

# ---------- 6) 分散型规则的调用图白名单 ----------
# 这类规则没有单一符号，靠「关键原语只允许登记文件调用」防止换写法复制第二套。
$callRules = @(
    @{ Label = 'W1 单扣账点'
       Symbols = @('TryDebitVirtual')
       Allowed = @('SDS.Core\SDS_Ledger.cs', 'SDS.Rules\SDS_WithdrawTransaction.cs') },
    @{ Label = 'W5/W54 镜像取货入口'
       Symbols = @('TryTakeMirror\(', 'TakeIntoCarry\(')
       Allowed = @('SDS.Rules\SDS_WithdrawExecutor.cs', 'SDS.Rules\SDS_RulesImpl.cs',
                   'SDS.Rules\SDS.Rules_API.cs', 'SDS.Api\SDS_API.cs',
                   'SDS.Vanilla\Mirrors\SDS_Mirrors.cs', 'SDS.Vanilla\Mirrors\Patch_Mirrors.cs',
                   'SDS.Vanilla\Jobs\SDS_JobTargets.cs') },
    @{ Label = 'W38-W48 装载链'
       Symbols = @('MaterializeForCaravan\(', 'ClampLoadTake\(', 'TakePendingLoadKey\(')
       Allowed = @('SDS.Vanilla\TradeTransport\SDS_CaravanTransport.cs',
                   'SDS.Vanilla\TradeTransport\Patch_CaravanTransport.cs',
                   'SDS.Vanilla\TradeTransport\SDS_TransportVirtual.cs',
                   'SDS.Vanilla\TradeTransport\JobDriver_SDS_CaravanJob.cs') }
)
foreach ($rule in $callRules) {
    foreach ($file in $codeFiles) {
        $rel = NormalizeRel($file.FullName.Substring($srcDir.Length))
        if ($rel.StartsWith('SDS.Tests\') -or $rel.StartsWith('SDS.Core.Tests\') -or $rel.StartsWith('SDS.Rules.Tests\')) { continue }
        $allowed = $false
        foreach ($a in $rule.Allowed) { if ($rel -eq $a) { $allowed = $true; break } }
        if ($allowed) { continue }
        $fileLines = Get-Content -LiteralPath $file.FullName -Encoding UTF8
        for ($i = 0; $i -lt $fileLines.Count; $i++) {
            $code = $fileLines[$i]
            $comment = $code.IndexOf('//')
            if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
            if ([string]::IsNullOrWhiteSpace($code)) { continue }
            foreach ($sym in $rule.Symbols) {
                if ($code -match $sym) {
                    Fail ("[越权调用] {0}:{1} → {2}（{3}：只允许登记文件）" -f `
                        $file.Name, ($i + 1), $sym, $rule.Label)
                }
            }
        }
    }
}

if ($failed -gt 0) {
    Write-Host ("唯一实现校验失败：{0} 项" -f $failed)
    exit 1
}
Write-Host ("唯一实现校验通过：登记 {0} 条；重复算式 0；越权调用 0；兼容直连 0" -f $list.Count)
exit 0

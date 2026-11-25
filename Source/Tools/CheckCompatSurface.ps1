# 兼容层面收敛校验（阶段 0–2：派生清单 + 调用链 + 双触发审计）
#
# 校验《公共API.md》里的派生表与源码一致：
#   1) 二·七 公共面清单：SDS_API 的 public 成员必须有且只有一行；
#   2) 4.8 兼容入口归属：每个 SDS_ModCompat_*.cs 至少一行，入口键不重复，
#      源码锚点文件存在且锚点符号出现在该文件里；
#   3) 4.9 适配器规则分类（人工结论，脚本只做覆盖性检查）；
#   4) 4.10 调用链：每个适配器一行，门面方法必须能在 SDS_API 分类，唯一实现类名必须存在；
#   5) 4.11 双触发审计：4.8 的每个入口键必须有一条结论；
#   6) 兼容基础设施类白名单：SDS_CompatServices.cs 与 SDS.Diag_API.cs / SDS_CompatReports.cs（Diag 报告实现）不得新增类别；
#   7) 兼容层调用图（-Report 输出）：每个 SDS_API 调用必须能判定为 public / internal。
#
# 说明：入口归属 / 调用链的机器校验粒度是「文件 + 符号」，不是完整的 Harmony 反射级挂点。
# 双触发的语义结论仍需人工审阅（《公共API》§4.3 / E7）。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckCompatSurface.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckCompatSurface.ps1 -Report
#
# 路径：脚本在 超维存储/Source/Tools；源码根 = 上一级（按文件名递归解析模块目录）；
#       设计文档全部位于 超维存储框架/（2026-11-12 迁移；旧框架/ 为纯历史存档，不再被读取）。
#       -SourceRoot 可覆盖源码根，用于对历史基线做回归对照。
#
# 本脚本只读源码与文档，不修改任何文件。

param([switch]$Report, [string]$SourceRoot = '')

$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $srcDir)
$docsDir = Join-Path $root '超维存储框架'
if ($SourceRoot) { $srcDir = $SourceRoot }
$docPath = Join-Path $docsDir '公共API.md'

if (-not (Test-Path $srcDir)) { Write-Host "找不到源码目录：$srcDir"; exit 2 }
if (-not (Test-Path $docPath)) { Write-Host "找不到 公共API.md"; exit 2 }

$failed = 0
function Fail([string]$msg) { Write-Host $msg; $script:failed++ }

function Find-SourceFile([string]$name) {
    $hit = Get-ChildItem $srcDir -Filter $name -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return ''
}

function Get-SourceFiles([string]$pattern) {
    return @(Get-ChildItem $srcDir -Filter $pattern -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
}

function Get-SectionRows([string]$path, [string]$headingPattern) {
    $lines = Get-Content $path -Encoding UTF8
    $inSection = $false
    $rows = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line -match '^#{2,3}\s') {
            if ($inSection) { break }
            if ($line -match $headingPattern) { $inSection = $true }
            continue
        }
        if ($inSection -and $line -match '^\|') { $rows.Add($line) }
    }
    return $rows
}

function Get-ApiMemberNames([string]$path, [string]$visibility) {
    $lines = Get-Content $path -Encoding UTF8
    $names = New-Object System.Collections.Generic.List[string]
    $inClass = $false
    $depth = 0
    $started = $false
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if (-not $inClass -and $line -match '\bclass\s+SDS_API\b') {
            $inClass = $true
            $depth = 0
            $started = $false
        }
        if (-not $inClass) { continue }

        if ($line -match ('^\s*' + $visibility + '\s+static\s+[^=;]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*\(')) {
            $names.Add($Matches[1])
        }
        if ($line -match ('^\s*' + $visibility + '\s+const\s+[^=;]*?\b([A-Za-z_][A-Za-z0-9_]*)\s*=')) {
            $names.Add($Matches[1])
        }

        $opens = ([regex]::Matches($line, '\{')).Count
        $closes = ([regex]::Matches($line, '\}')).Count
        if ($opens -gt 0) { $started = $true }
        $depth += ($opens - $closes)
        if ($started -and $depth -le 0 -and $i -gt 0) { break }
    }
    return $names | Sort-Object -Unique
}

function Get-ClassNames([string]$path) {
    $names = New-Object System.Collections.Generic.List[string]
    foreach ($line in (Get-Content $path -Encoding UTF8)) {
        if ($line -match '^\s*(?:internal|public)\s+(?:(?:static|sealed|abstract|partial)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)') {
            $names.Add($Matches[1])
        }
    }
    return $names | Sort-Object -Unique
}

$apiFiles = @(
    (Find-SourceFile 'SDS_API.cs'),
    (Find-SourceFile 'SDS_API_Adapters.cs')
)
$publicNames = New-Object System.Collections.Generic.List[string]
$internalNames = New-Object System.Collections.Generic.List[string]
foreach ($apiFile in $apiFiles) {
    if (-not $apiFile) { Fail "[缺文件] SDS_API.cs / SDS_API_Adapters.cs"; continue }
    foreach ($n in (Get-ApiMemberNames $apiFile 'public')) { $publicNames.Add($n) }
    foreach ($n in (Get-ApiMemberNames $apiFile 'internal')) { $internalNames.Add($n) }
}
$publicNames = $publicNames | Sort-Object -Unique
$internalNames = $internalNames | Sort-Object -Unique

# ---------- 1) 公共面清单 ----------
$publicSection = Get-SectionRows $docPath '二·七'
$publicTable = @{}
foreach ($row in $publicSection) {
    $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 5) { continue }
    $memberCell = $cells[0] -replace '`', ''
    if ($memberCell -notmatch '^SDS_API\.([A-Za-z0-9_]+)$') { continue }
    $name = $Matches[1]
    if ($publicTable.ContainsKey($name)) { Fail "[公共面重复] SDS_API.$name" }
    $publicTable[$name] = [pscustomobject]@{
        Category = $cells[1]
        Doc      = $cells[2]
        Callers  = $cells[3]
        Evidence = $cells[4]
    }
}

# Version 4 起，适配器桥以 public 方法挂在 SDS_API 上（跨程序集无 InternalsVisibleTo）。
# 桥面的唯一登记处是《模块接口.md》五；这里只把其中的方法名并入“已知公共面”，不再要求冻结表行。
$contractDocPath = Join-Path $root '超维存储框架\模块接口.md'
$bridgeNames = @{}
if (Test-Path $contractDocPath) {
    foreach ($row in (Get-SectionRows $contractDocPath '五、SDS\.Api')) {
        foreach ($m in [regex]::Matches($row, '`([A-Za-z_][A-Za-z0-9_]*)\s*\(')) {
            $bridgeNames[$m.Groups[1].Value] = $true
        }
    }
}

$validCategories = @('契约', '公共辅助', '仅内部', '未使用')
foreach ($name in $publicNames) {
    if ($bridgeNames.ContainsKey($name)) { continue }
    if (-not $publicTable.ContainsKey($name)) { Fail "[公共面缺行] SDS_API.$name" }
}
foreach ($name in ($publicTable.Keys | Sort-Object)) {
    $row = $publicTable[$name]
    if ($publicNames -notcontains $name) { Fail "[公共面残留] SDS_API.$name" }
    if ($validCategories -notcontains $row.Category) {
        Fail "[公共面类别] SDS_API.$name：$($row.Category)"
    }
    if ([string]::IsNullOrWhiteSpace($row.Evidence)) {
        Fail "[公共面证据缺] SDS_API.$name"
    }
    if ($row.Category -eq '仅内部' -or $row.Category -eq '未使用') {
        Write-Host "[公共面待处理] SDS_API.$name：$($row.Category)（阶段 3 决策）"
    }
}

# ---------- 2) 兼容入口归属 ----------
$entrySection = Get-SectionRows $docPath '4\.8'
$entryKeys = @{}
$anchorFiles = @{}
foreach ($row in $entrySection) {
    $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 5) { continue }
    $key = $cells[1] -replace '`', ''
    if ([string]::IsNullOrWhiteSpace($key) -or $key -eq '入口键' -or $key -match '^-+$') { continue }
    if ($entryKeys.ContainsKey($key)) { Fail "[入口重复] $key" }
    $entryKeys[$key] = $true

    $anchor = $cells[3] -replace '`', ''
    if ($anchor -notmatch '^([A-Za-z0-9_]+\.cs)\s*:\s*(.+)$') {
        Fail "[入口锚点格式] $key：$anchor（应为 文件.cs: 符号, 符号）"
        continue
    }
    $fileName = $Matches[1]
    $symbols = $Matches[2]
    $filePath = Find-SourceFile $fileName
    if (-not $filePath) { Fail "[入口缺文件] $key：$fileName"; continue }
    $anchorFiles[$fileName] = $true
    foreach ($sym in ($symbols -split '[,，/]')) {
        $s = $sym.Trim()
        if ($s -eq '') { continue }
        if (-not (Select-String -Path $filePath -Pattern $s -SimpleMatch -Quiet)) {
            Fail "[入口缺符号] $key：$fileName/$s"
        }
    }
}
foreach ($file in (Get-SourceFiles 'SDS_ModCompat_*.cs')) {
    if (-not $anchorFiles.ContainsKey($file.Name)) {
        Fail "[入口缺文件行] $($file.Name)：4.8 表没有该适配器的入口行"
    }
}

# ---------- 3) 兼容基础设施类白名单（C2.3） ----------
# 2026-11-12 起 SDS.Diag 公开面收缩进 SDS.Diag_API.cs：报告 DTO 与 Hub 门面在 API 文件，
# SDS_CompatReports.cs 只保留 SDS_CompatReportHubImpl 实现；两处分别校验。
$servicesPath = Find-SourceFile 'SDS_CompatServices.cs'
$expectedServices = @('SDS_CompatTools', 'SDS_CompatEndpoint', 'SDS_CompatStock', 'SDS_CompatBillSupply', 'SDS_CompatMirrorSeam')
if ($servicesPath) {
    $classes = Get-ClassNames $servicesPath
    foreach ($c in $classes) {
        if ($expectedServices -notcontains $c) { Fail "[基础设施多类] SDS_CompatServices.cs：$c" }
    }
    foreach ($c in $expectedServices) {
        if ($classes -notcontains $c) { Fail "[基础设施缺类] SDS_CompatServices.cs：$c" }
    }
}
$reportsApiPath = Find-SourceFile 'SDS.Diag_API.cs'
$expectedReportsApi = @('SDS_CompatPatchReport', 'SDS_CompatAdapterReport', 'SDS_CompatReportHub')
if ($reportsApiPath) {
    $classes = Get-ClassNames $reportsApiPath
    foreach ($c in ($classes | Where-Object { $_ -like 'SDS_Compat*' })) {
        if ($expectedReportsApi -notcontains $c) { Fail "[报告多类] SDS.Diag_API.cs：$c" }
    }
    foreach ($c in $expectedReportsApi) {
        if ($classes -notcontains $c) { Fail "[报告缺类] SDS.Diag_API.cs：$c" }
    }
}
$reportsImplPath = Find-SourceFile 'SDS_CompatReports.cs'
$expectedReportsImpl = @('SDS_CompatReportHubImpl')
if ($reportsImplPath) {
    $classes = Get-ClassNames $reportsImplPath
    foreach ($c in $classes) {
        if ($expectedReportsImpl -notcontains $c) { Fail "[报告实现多类] SDS_CompatReports.cs：$c" }
    }
    foreach ($c in $expectedReportsImpl) {
        if ($classes -notcontains $c) { Fail "[报告实现缺类] SDS_CompatReports.cs：$c" }
    }
}

# ---------- 4) 调用链（4.10） ----------
$chainSection = Get-SectionRows $docPath '4\.10'
$chainFiles = @{}
$chainCount = 0
foreach ($row in $chainSection) {
    $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 4) { continue }
    $adapterFile = $cells[0] -replace '`', ''
    if ($adapterFile -notmatch '^SDS_ModCompat_.+\.cs$') { continue }
    $chainCount++
    $adapterPath = Find-SourceFile $adapterFile
    if (-not $adapterPath) { Fail "[调用链缺文件] $adapterFile"; continue }
    $chainFiles[$adapterFile] = $true

    foreach ($m in [regex]::Matches($cells[2], '`SDS_API\.([A-Za-z0-9_]+)`')) {
        $apiName = $m.Groups[1].Value
        if ($publicNames -notcontains $apiName -and $internalNames -notcontains $apiName) {
            Fail "[调用链门面未知] $adapterFile：SDS_API.$apiName"
        }
    }
    foreach ($m in [regex]::Matches($cells[3], '`([^`]+)`')) {
        $impl = $m.Groups[1].Value
        if (-not $impl.StartsWith('SDS_')) { continue }
        $hit = Get-SourceFiles '*.cs' | Where-Object { Select-String -Path $_.FullName -Pattern $impl -SimpleMatch -Quiet } | Select-Object -First 1
        if (-not $hit) {
            Fail "[调用链实现未知] $adapterFile：$impl"
        }
    }
}
foreach ($file in (Get-SourceFiles 'SDS_ModCompat_*.cs')) {
    if (-not $chainFiles.ContainsKey($file.Name)) {
        Fail "[调用链缺文件行] $($file.Name)：4.10 表没有该适配器"
    }
}

# ---------- 5) 双触发审计（4.11） ----------
$auditSection = Get-SectionRows $docPath '4\.11'
$auditKeys = @{}
$auditCount = 0
foreach ($row in $auditSection) {
    $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 4) { continue }
    $auditCount++
    foreach ($m in [regex]::Matches($cells[0], '`((?:PUAH|CE|EE|UF|MiliraGift|Milira|Beam|GD|CeleTechShuttle|Versatran)\.[A-Za-z0-9_]+)`')) {
        $auditKeys[$m.Groups[1].Value] = $true
    }
}
foreach ($key in ($entryKeys.Keys | Sort-Object)) {
    if (-not $auditKeys.ContainsKey($key)) {
        Fail "[审计缺入口] $key：4.11 表没有该入口的双触发结论"
    }
}

# ---------- 6) 兼容层调用图 ----------
$compatFiles = New-Object System.Collections.Generic.List[object]
Get-SourceFiles 'SDS_ModCompat*.cs' | ForEach-Object { $compatFiles.Add($_) }
# SDS_CompatReports.cs 自 2026-11-12 起属 SDS.Diag（公开面在 SDS.Diag_API.cs），不在兼容层调用图内。
foreach ($extra in @('SDS_CompatServices.cs')) {
    $p = Find-SourceFile $extra
    if ($p) { $compatFiles.Add((Get-Item $p)) }
}

$callGraph = New-Object System.Collections.Generic.List[object]
foreach ($file in $compatFiles) {
    $apiNames = Select-String -Path $file.FullName -Pattern 'SDS_API\.([A-Za-z0-9_]+)' -AllMatches |
        ForEach-Object { $_.Matches | ForEach-Object { $_.Groups[1].Value } } |
        Sort-Object -Unique
    $helperNames = Select-String -Path $file.FullName -Pattern 'SDS_Compat[A-Za-z0-9_]+' -AllMatches |
        ForEach-Object { $_.Matches | ForEach-Object { $_.Value } } |
        Sort-Object -Unique
    if ($file.Name -eq 'SDS_ModCompat.cs' -and $apiNames.Count -gt 0) {
        Fail "[编排越界] SDS_ModCompat.cs：只做编排 / 分发，不得直接调用 SDS_API"
    }
    $entries = New-Object System.Collections.Generic.List[string]
    foreach ($name in $apiNames) {
        $kind = if ($publicNames -contains $name) { 'public' } elseif ($internalNames -contains $name) { 'internal' } else { '未知' }
        if ($kind -eq '未知') { Fail "[API未知] $($file.Name)：SDS_API.$name" }
        $entries.Add("SDS_API.$name($kind)")
    }
    $callGraph.Add([pscustomobject]@{
        File    = $file.Name
        Apis    = $entries
        Helpers = $helperNames
    })
}

# ---------- 9) 第三方适配器禁止调用 SDS_Diag.Fail（只允许 ErrorOnce / Warn / 报告中心） ----------
foreach ($file in $compatFiles) {
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($t.StartsWith('//') -or $t.StartsWith('*') -or $t.StartsWith('/*')) { continue }
        if ($lines[$i] -match 'SDS_Diag\.Fail') {
            Fail ("[Fail越界] {0}:{1}：适配器不得调用 SDS_Diag.Fail" -f $file.Name, ($i + 1))
        }
    }
}

if ($Report) {
    Write-Output "== 二·七 公共面清单（源码 public 成员）=="
    foreach ($name in $publicNames) {
        $row = $publicTable[$name]
        if ($row) {
            "SDS_API.{0}`t{1}`t调用方={2}`t证据={3}" -f $name, $row.Category, $row.Callers, $row.Evidence
        } else {
            "SDS_API.{0}`t<缺行>" -f $name
        }
    }
    Write-Output ""
    Write-Output "== 4.8 兼容入口归属 =="
    foreach ($row in $entrySection) {
        $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        if ($cells.Count -lt 5) { continue }
        $key = $cells[1] -replace '`', ''
        if ([string]::IsNullOrWhiteSpace($key) -or $key -eq '入口键' -or $key -match '^-+$') { continue }
        "{0}`t{1}`t{2}`t{3}" -f $key, $cells[0], $cells[2], $cells[3]
    }
    Write-Output ""
    Write-Output "== 4.10 调用链 =="
    foreach ($row in $chainSection) {
        $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        if ($cells.Count -lt 4) { continue }
        $adapterFile = $cells[0] -replace '`', ''
        if ($adapterFile -notmatch '^SDS_ModCompat_.+\.cs$') { continue }
        "{0}`t{1}`t{2}`t{3}" -f $adapterFile, $cells[1], $cells[2], $cells[3]
    }
    Write-Output ""
    Write-Output "== 4.11 双触发审计 =="
    foreach ($row in $auditSection) {
        $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        if ($cells.Count -lt 4) { continue }
        "{0}`t{1}`t{2}" -f $cells[0], $cells[2], $cells[3]
    }
    Write-Output ""
    Write-Output "== 兼容层调用图 =="
    foreach ($entry in $callGraph) {
        "--- {0} ---" -f $entry.File
        if ($entry.Apis.Count -gt 0) { "  API: " + ($entry.Apis -join ', ') }
        if ($entry.Helpers.Count -gt 0) { "  基础设施: " + ($entry.Helpers -join ', ') }
    }
    Write-Host ("公共成员 {0}；兼容入口 {1}；调用链 {2}；审计 {3}；兼容文件 {4}；internal 成员 {5}" -f $publicNames.Count, $entryKeys.Count, $chainCount, $auditCount, $compatFiles.Count, $internalNames.Count)
    exit 0
}

if ($failed -gt 0) {
    Write-Host ("兼容面校验失败：{0} 项（公共成员 {1} / 入口 {2} / 调用链 {3} / 审计 {4} / 兼容文件 {5}）" -f $failed, $publicNames.Count, $entryKeys.Count, $chainCount, $auditCount, $compatFiles.Count)
    exit 1
}
Write-Host ("兼容面校验通过：公共成员 {0}；兼容入口 {1}；调用链 {2}；审计 {3}；兼容文件 {4}；internal 成员 {5}" -f $publicNames.Count, $entryKeys.Count, $chainCount, $auditCount, $compatFiles.Count, $internalNames.Count)
exit 0

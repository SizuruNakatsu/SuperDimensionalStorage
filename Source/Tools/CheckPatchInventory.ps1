# 补丁清单覆盖校验（派生文档 ← 源码）
#
# 校验 补丁清单.md 与源码是否一一对应：
#   1) 源码里每个 `[HarmonyPatch]` 特性补丁类，在清单里必须且只能出现一次；
#   2) 清单里每一行，必须能在源码里找到同名补丁类（防止残留已删补丁）；
#   3) 「统计」节按文件的补丁类数量、合计，必须与源码实测一致；
#   4) 每个启用 TryPatch 的 SDS_ModCompat_*.cs，必须在「动态补丁」节至少有一行；
#   5) 每行的 层 / 理由 / 权威 / 规则 必须合法：
#      - 层 ∈ 标量读 / 看见/搜索 / 消费 / 派活 / 清单/结算 / 边界
#      - 理由 ∈ 契约 / 投影 / 规则 / 兼容
#      - 权威 = `文档名 §节号`（文档必须存在，节号必须在该文档里出现）或 —
#      - 规则 = 规则 ID（用 、 分隔）或 —；ID 必须在本目录任一设计文档里出现过
#   6) 「十一、注册表化基线」必须是 19 守卫 + 10 投影 owner，且每行的
#      TargetKey / 组 / 目标 / 动作 / 逃生门 / 规则 合法：
#      - TargetKey 的 Type / Method / 参数必须出现在该类的源码特性或方法体里
#      - 组 ∈ 守卫 / 投影owner
#      - 目标 ∈ mirror / vault / both
#      - 动作按组隔离：守卫 = false/empty/zero/filter/postfix/recount；
#                     投影 owner = translate
#      - TargetKey 格式：`Type.Method` / `Type.Method[arg,...]` / `Type.Property#getter`
#   7) 「十二、特例类」必须是 3 行（MapDeinit / NotHaulable / NotMergeable），
#      动作 ∈ allow / suppress；允许与基线分离，不进入两个注册表。
#  10) 兼容层 allowlist：SDS_ModCompat* / SDS_Compat* 只允许 SDS_API / SDS_Compat* /
#      SDS_Diag / 公共 API 类型；出现其它 SDS_* 即失败。
#   -Report 额外输出「TargetMethod 文本键去重」计数（非反射级）。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckPatchInventory.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckPatchInventory.ps1 -Report
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckPatchInventory.ps1 -Targets
#
# 路径：脚本在 超维存储/Source/Tools；源码根 = 上一级（按文件名递归解析模块目录）；
#       设计文档全部位于 超维存储框架/（2026-11-12 迁移；旧框架/ 为纯历史存档，不再被读取）。
#       -SourceRoot 可覆盖源码根，用于对历史基线做回归对照。
#
# 本脚本只读源码与文档，不修改任何文件。

param([switch]$Report, [switch]$Targets, [string]$SourceRoot = '')

$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $srcDir)
$docsDir = Join-Path $root '超维存储框架'
if ($SourceRoot) { $srcDir = $SourceRoot }
$docPath = Join-Path $docsDir '补丁清单.md'

if (-not (Test-Path $srcDir)) { Write-Host "找不到源码目录：$srcDir"; exit 2 }
if (-not (Test-Path $docPath)) { Write-Host "找不到 补丁清单.md"; exit 2 }

function Find-SourceFile([string]$name) {
    $hit = Get-ChildItem $srcDir -Filter $name -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        Select-Object -First 1
    if ($hit) { return $hit.FullName }
    return ''
}

$classRegex = '^\s*(?:public|internal|private)?\s*(?:static\s+)?(?:partial\s+)?(?:sealed\s+)?class\s+([A-Za-z0-9_]+)'

# ---------- 1) 源码：提取「特性 + 类名 + 目标方法体」 ----------
$srcRows = New-Object System.Collections.Generic.List[object]
$allClasses = @{}
$compatFiles = @{}
Get-ChildItem $srcDir -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    ForEach-Object {
        $file = $_.Name
        $lines = Get-Content $_.FullName -Encoding UTF8
        $attr = $null
        $attrClosed = $false
        $hasTryPatch = $false
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            if ($line -match '\bTryPatch\(') { $hasTryPatch = $true }

            # 两种形态：`[HarmonyPatch(...)]` 与 `[HarmonyPatch]` + `TargetMethod()`。
            if ($line -match '^\s*\[HarmonyPatch') {
                if ($null -eq $attr) { $attr = $line.Trim() }
                else { $attr = $attr + ' ' + $line.Trim() }
                if ($line -match '\]\s*$') { $attrClosed = $true }
                continue
            }

            if ($line -match $classRegex) {
                $className = $Matches[1]
                $allClasses[$className] = $true
                if ($attr) {
                    # 抓类体：用于 `[HarmonyPatch]` + TargetMethod() 的目标解析。
                    $depth = 0
                    $seenBrace = $false
                    $bodyParts = New-Object System.Collections.Generic.List[string]
                    $max = [System.Math]::Min($i + 80, $lines.Count - 1)
                    for ($j = $i; $j -le $max; $j++) {
                        $l = $lines[$j]
                        $bodyParts.Add($l)
                        $opens = ([regex]::Matches($l, '\{')).Count
                        $closes = ([regex]::Matches($l, '\}')).Count
                        if ($opens -gt 0) { $seenBrace = $true }
                        $depth += ($opens - $closes)
                        if ($seenBrace -and $j -gt $i -and $depth -le 0) { break }
                    }
                    $srcRows.Add([pscustomobject]@{
                        Class = $className
                        File  = $file
                        Attr  = $attr
                        Body  = ($bodyParts -join ' ')
                    })
                    $attr = $null
                    $attrClosed = $false
                }
            }
        }
        if ($hasTryPatch -and $file -match '^SDS_ModCompat_.+\.cs$') {
            $compatFiles[[System.IO.Path]::GetFileNameWithoutExtension($file)] = $true
        }
    }

# ---------- 1.5) 注册表条目：两张表的 Id = "Patch_..." 是补丁点的第二载体 ----------
$registryIds = New-Object System.Collections.Generic.List[string]
foreach ($registryFile in @('SDS_MirrorGuardRegistry.cs', 'SDS_ProjectionOwnerBridge.cs')) {
    $registryPath = Find-SourceFile $registryFile
    if (-not $registryPath) { continue }
    Get-Content $registryPath -Encoding UTF8 | ForEach-Object {
        if ($_ -match 'Id\s*=\s*"(Patch_[A-Za-z0-9_]+)"') { $registryIds.Add($Matches[1]) }
    }
}
$registrySet = @{}
foreach ($id in $registryIds) { $registrySet[$id] = $true }

function Get-TargetDescriptor($row) {
    $text = $row.Attr + ' ' + $row.Body
    $type = ''
    $method = ''
    if ($text -match 'typeof\(([A-Za-z0-9_<>]+)\)') { $type = $Matches[1] }
    elseif ($text -match 'TypeByName\("([A-Za-z0-9_.]+)"\)') { $type = ($Matches[1] -split '\.')[-1] }

    if ($text -match 'nameof\([A-Za-z0-9_<>]+\.([A-Za-z0-9_]+)\)') { $method = $Matches[1] }
    elseif ($text -match 'AccessTools\.Method\([^,]+,\s*"([A-Za-z0-9_]+)"') { $method = $Matches[1] }
    elseif ($text -match 'HarmonyPatch\([^,]+,\s*"([A-Za-z0-9_]+)"') { $method = $Matches[1] }
    elseif ($text -match '"([A-Za-z0-9_]+)"') { $method = $Matches[1] }

    $params = ''
    if ($text -match 'new\[\]\s*\{([^}]*)\}') {
        $raw = $Matches[1]
        $tokens = [regex]::Matches($raw, 'typeof\(([A-Za-z0-9_<>.]+)\)') | ForEach-Object { $_.Groups[1].Value }
        if ($tokens.Count -gt 0) { $params = ($tokens -join ',') }
    }
    if ($type -eq '' -or $method -eq '') { return $row.Class }
    if ($params -ne '') { return "$type.$method[$params]" }
    return "$type.$method"
}

# ---------- 2) 文档：按节提取清单行 ----------
$docLines = Get-Content $docPath -Encoding UTF8
$docRows = New-Object System.Collections.Generic.List[object]
$dynRows = New-Object System.Collections.Generic.List[object]
$baseRows = New-Object System.Collections.Generic.List[object]
$specialRows = New-Object System.Collections.Generic.List[object]
$section = ''
foreach ($line in $docLines) {
    if ($line -match '^##\s*(.+)$') { $section = $Matches[1].Trim() }

    if ($line -match '^\|\s*`(Patch_[A-Za-z0-9_]+)`\s*\|') {
        $class = $Matches[1]
        $cells = $line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        if ($section -like '十一、*') { $baseRows.Add([pscustomobject]@{ Class = $class; Cells = $cells }) }
        elseif ($section -like '十二、*') { $specialRows.Add([pscustomobject]@{ Class = $class; Cells = $cells }) }
        elseif ($section -notlike '十、*') { $docRows.Add([pscustomobject]@{ Class = $class; Cells = $cells }) }
    }
    elseif ($section -like '十、*' -and $line -match '^\|\s*`(SDS_ModCompat_[A-Za-z0-9_]+)`\s*\|') {
        $class = $Matches[1]
        $cells = $line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
        $dynRows.Add([pscustomobject]@{ Class = $class; Cells = $cells })
    }
}

if ($Report) {
    $srcRows | Sort-Object File, Class |
        ForEach-Object { "{0}`t{1}`t{2}`t{3}" -f $_.File, $_.Class, (Get-TargetDescriptor $_), $_.Attr } |
        Write-Output
    $registryIds | ForEach-Object { "registry`t$_" } | Write-Output
    $descriptors = @($srcRows | ForEach-Object { Get-TargetDescriptor $_ })
    $baseDescriptors = @($baseRows | ForEach-Object { ($_.Cells[1] -replace '`', '') })
    $distinct = @(($descriptors + $baseDescriptors) | Sort-Object -Unique)
    Write-Host ("源码特性补丁类：{0}；注册表条目：{1}；TargetMethod 文本键去重：{2}" -f $srcRows.Count, $registryIds.Count, $distinct.Count)
    exit 0
}
if ($Targets) {
    $srcRows | Sort-Object File, Class |
        ForEach-Object {
            $body = $_.Body
            if ($body.Length -gt 400) { $body = $body.Substring(0, 400) + '…' }
            "{0}`t{1}`t{2}`t{3}" -f $_.File, $_.Class, $_.Attr, $body
        } | Write-Output
    Write-Host ("源码特性补丁类：{0}" -f $srcRows.Count)
    exit 0
}

$failed = 0
function Fail([string]$msg) { Write-Host $msg; $script:failed++ }

# ---------- 3) 双向集合比对 ----------
$srcSet = @{}
foreach ($r in $srcRows) { if (-not $srcSet.ContainsKey($r.Class)) { $srcSet[$r.Class] = 0 }; $srcSet[$r.Class]++ }
foreach ($id in ($registrySet.Keys | Sort-Object)) {
    if ($srcSet.ContainsKey($id)) { Fail "[重复载体] $id 同时是特性类与注册表条目" }
    else { $srcSet[$id] = 1 }
}
if ($registryIds.Count -ne $registrySet.Count) {
    Fail "[注册表重复] 注册表 Id 有重复：登记 $($registryIds.Count)，去重 $($registrySet.Count)"
}
$docSet = @{}
foreach ($r in $docRows) { if (-not $docSet.ContainsKey($r.Class)) { $docSet[$r.Class] = 0 }; $docSet[$r.Class]++ }

foreach ($name in ($srcSet.Keys | Sort-Object)) {
    if (-not $docSet.ContainsKey($name)) { Fail "[缺清单] $name 在源码里，但清单里没有" }
}
foreach ($name in ($docSet.Keys | Sort-Object)) {
    if (-not $srcSet.ContainsKey($name)) { Fail "[残留行] $name 在清单里，但源码里没有" }
    elseif ($docSet[$name] -gt 1) { Fail "[重复行] $name 在清单里出现 $($docSet[$name]) 次" }
}

# ---------- 4) 统计节校验 ----------
$srcByFile = @{}
foreach ($r in $srcRows) { if (-not $srcByFile.ContainsKey($r.File)) { $srcByFile[$r.File] = 0 }; $srcByFile[$r.File]++ }

$inStats = $false
$statSeen = @{}
foreach ($line in $docLines) {
    if ($line -match '^##\s*统计\s*$') { $inStats = $true; continue }
    if ($inStats -and $line -match '^##\s') { break }
    if (-not $inStats) { continue }
    if ($line -match '^\|\s*`([A-Za-z0-9_]+\.cs)`\s*\|\s*(\d+)\s*\|') {
        $file = $Matches[1]; $n = [int]$Matches[2]
        $statSeen[$file] = $true
        $actual = if ($srcByFile.ContainsKey($file)) { $srcByFile[$file] } else { 0 }
        if ($actual -ne $n) { Fail "[统计漂移] $file：清单写 $n，源码 $actual" }
    }
    if ($line -match '合计（特性补丁）.*?\*\*(\d+)\*\*') {
        $total = [int]$Matches[1]
        if ($total -ne $srcRows.Count) { Fail "[合计漂移] 清单写 $total，源码特性类 $($srcRows.Count)" }
    }
    if ($line -match '注册表条目.*?\|\s*(\d+)\s*\|') {
        $n = [int]$Matches[1]
        if ($n -ne $registryIds.Count) { Fail "[注册表漂移] 清单写 $n，源码 $($registryIds.Count)" }
    }
    if ($line -match '合计（补丁点）.*?\*\*(\d+)\*\*') {
        $total = [int]$Matches[1]
        $actual = $srcRows.Count + $registryIds.Count
        if ($total -ne $actual) { Fail "[补丁点漂移] 清单写 $total，源码 $actual" }
    }
}
if ($registryIds.Count -ne 29) { Fail "[注册表数量] 期望 29（19 守卫 + 10 投影 owner），源码 $($registryIds.Count)" }
foreach ($file in ($srcByFile.Keys | Sort-Object)) {
    if (-not $statSeen.ContainsKey($file) -and $srcByFile[$file] -gt 0) {
        Fail "[统计缺行] $file 有 $($srcByFile[$file]) 个补丁类，统计节没登记"
    }
}

# ---------- 5) 主表元数据校验 ----------
$layers = @('标量读', '看见/搜索', '消费', '派活', '清单/结算', '边界')
$reasons = @('契约', '投影', '规则', '兼容')
$allDocPaths = @(Get-ChildItem $docsDir -Filter *.md | ForEach-Object { $_.FullName })

function Test-RuleIds([string]$class, [string]$ids) {
    if ($ids -eq '—' -or $ids -eq '-') { return }
    foreach ($id in ($ids -split '[、,，/]')) {
        $id = $id.Trim()
        if ($id -eq '') { continue }
        if ($id -notmatch '^(?:[RWS]\d+[a-z]?|E\d+)$') { Fail "[规则格式] $($class)：$id"; continue }
        $hit = $false
        $pattern = '(?<![A-Za-z0-9])' + [regex]::Escape($id) + '(?![A-Za-z0-9])'
        foreach ($p in $allDocPaths) {
            if (Select-String -Path $p -Pattern $pattern -Quiet) { $hit = $true; break }
        }
        if (-not $hit) { Fail "[规则未定义] $($class)：$id 不在任何设计文档里" }
    }
}

function Test-Meta([string]$class, $cells) {
    if ($cells.Count -lt 7) { Fail "[列缺失] $class：需要 补丁类|节点|层|理由|权威|规则|作用"; return }
    $layer = $cells[2]; $reason = $cells[3]; $auth = ($cells[4] -replace '`', ''); $ids = ($cells[5] -replace '`', '')

    if ($layers -notcontains $layer) { Fail "[层非法] $($class)：$layer" }
    if ($reasons -notcontains $reason) { Fail "[理由非法] $($class)：$reason" }

    if ($auth -ne '—' -and $auth -ne '-') {
        foreach ($ref in ($auth -split '[；;]')) {
            $ref = $ref.Trim()
            if ($ref -eq '') { continue }
            if ($ref -notmatch '^(.+?)\s*§(.+)$') { Fail "[权威格式] $($class)：$ref（应为 文档名 §节号）"; continue }
            $docName = $Matches[1].Trim(); $sectionName = $Matches[2].Trim()
            $p = Join-Path $docsDir ($docName + '.md')
            if (-not (Test-Path $p)) { Fail "[缺文档] $($class)：$docName.md 不存在"; continue }
            if (-not (Select-String -Path $p -Pattern $sectionName -SimpleMatch -Quiet)) {
                Fail "[缺节号] $($class)：$docName.md 里找不到 §$sectionName"
            }
        }
    }
    Test-RuleIds $class $ids
}

foreach ($r in $docRows) { Test-Meta $r.Class $r.Cells }

# ---------- 6) 动态补丁：类存在 + 每个启用 TryPatch 的文件必须登记 ----------
$dynSeen = @{}
foreach ($r in $dynRows) {
    $dynSeen[$r.Class] = $true
    if (-not $allClasses.ContainsKey($r.Class)) { Fail "[动态缺类] $($r.Class) 在源码里不存在" }
    Test-Meta $r.Class $r.Cells
}
foreach ($name in ($compatFiles.Keys | Sort-Object)) {
    if (-not $dynSeen.ContainsKey($name)) { Fail "[动态缺行] $name.cs 启用 TryPatch，但清单「动态补丁」节没有登记" }
}

# ---------- 7) 注册表化基线：19 守卫 + 10 投影 owner ----------
$expectedGuards = @(
    'Patch_SDS_MirrorNoDamage',
    'Patch_SDS_VaultPreApplyDamage',
    'Patch_SDS_MirrorNoFire',
    'Patch_SDS_MirrorNoDeSpawn',
    'Patch_SDS_MirrorNoDeSpawnWithComps',
    'Patch_SDS_MirrorNoRealtimeDraw',
    'Patch_SDS_MirrorNoMeshPrint',
    'Patch_SDS_MirrorNoMeshPrintWithComps',
    'Patch_SDS_MirrorNoGuiOverlay',
    'Patch_SDS_MirrorNoGuiOverlayWithComps',
    'Patch_SDS_MirrorNotSelectable',
    'Patch_SDS_MirrorNotSelectableDirect',
    'Patch_SDS_RoomIgnoresHiddenGrid',
    'Patch_SDS_RoomContainsThingIgnoresHiddenGrid',
    'Patch_SDS_RoomContainedThingsIgnoresHiddenGrid',
    'Patch_SDS_RoomThingCountIgnoresHiddenGrid',
    'Patch_SDS_ObserverIgnoresHiddenGrid',
    'Patch_SDS_TantrumIgnoresHiddenGrid',
    'Patch_SDS_FireSpreeIgnoresHiddenGrid'
)
$expectedProjection = @(
    'Patch_SDS_ProjectionTake',
    'Patch_SDS_ProjectionTryDropCounted',
    'Patch_SDS_ProjectionTryDropAll',
    'Patch_SDS_ProjectionTryTransfer',
    'Patch_SDS_EndpointTransferReturn',
    'Patch_SDS_ProjectionClear',
    'Patch_SDS_ProjectionClearAndDestroy',
    'Patch_SDS_ProjectionClearAndDestroyOrPassToWorld',
    'Patch_SDS_ProjectionRemoveAll',
    'Patch_SDS_ProjectionRemoveAllGeneric'
)

function Test-TargetKey([string]$class, [string]$key) {
    # 注册表条目由 C# 的 typeof / nameof 编译期校验；这里只校验声明在基线里。
    if ($registrySet.ContainsKey($class)) { return }
    $row = $srcRows | Where-Object { $_.Class -eq $class } | Select-Object -First 1
    if ($null -eq $row) { Fail "[基线缺类] $class 不在源码特性补丁里"; return }
    $text = $row.Attr + ' ' + $row.Body
    $dot = $key.IndexOf('.')
    if ($dot -lt 0) { Fail "[TargetKey格式] $($class)：$key"; return }
    $typeName = $key.Substring(0, $dot)
    $rest = $key.Substring($dot + 1)
    $cut = $rest.IndexOf('[')
    if ($cut -lt 0) { $cut = $rest.IndexOf('(') }
    if ($cut -ge 0) {
        $method = $rest.Substring(0, $cut)
        $argsText = $rest.Substring($cut + 1).TrimEnd(']', ')')
    } else {
        $space = $rest.IndexOf(' ')
        $method = if ($space -ge 0) { $rest.Substring(0, $space) } else { $rest }
        $argsText = ''
    }
    $method = ($method -split '#')[0]
    if (-not $text.Contains($typeName)) { Fail "[TargetKey类型] $($class)：$typeName 不在源码特性/方法体里" }
    if (-not $text.Contains($method)) { Fail "[TargetKey方法] $($class)：$method 不在源码特性/方法体里" }
    if ($argsText -ne '') {
        foreach ($arg in ($argsText -split ',')) {
            $a = $arg.Trim()
            $a = $a -replace '^(out|ref|this)\s+', ''
            $a = ($a -split '<')[0]
            if ($a -eq '') { continue }
            if (-not $text.Contains($a)) { Fail "[TargetKey参数] $($class)：$a 不在源码特性/方法体里" }
        }
    }
}

$groups = @('守卫', '投影owner')
$targetScopes = @('mirror', 'vault', 'both')
$guardActions = @('false', 'filter', 'postfix', 'recount')
$projectionActions = @('translate')
$specialActions = @('allow', 'suppress')

$baseSeen = @{}
foreach ($r in $baseRows) {
    if ($baseSeen.ContainsKey($r.Class)) { Fail "[基线重复] $($r.Class)" }
    $baseSeen[$r.Class] = $true
    if ($r.Cells.Count -lt 7) { Fail "[基线列缺失] $($r.Class)：需要 补丁类|TargetKey|组|目标|动作|逃生门|规则"; continue }
    $key = ($r.Cells[1] -replace '`', ''); $group = $r.Cells[2]; $target = $r.Cells[3]
    $action = $r.Cells[4]; $escape = $r.Cells[5]; $ids = ($r.Cells[6] -replace '`', '')
    if ($groups -notcontains $group) { Fail "[基数组非法] $($r.Class)：$group"; continue }
    if ($targetScopes -notcontains $target) { Fail "[基线目标非法] $($r.Class)：$target" }
    if ($group -eq '守卫' -and $guardActions -notcontains $action) {
        Fail "[基线动作非法] $($r.Class)：守卫只允许 $($guardActions -join '/')，实际 $action"
    }
    if ($group -eq '投影owner' -and $projectionActions -notcontains $action) {
        Fail "[基线动作非法] $($r.Class)：投影 owner 只允许 $($projectionActions -join '/')，实际 $action"
    }
    if ([string]::IsNullOrWhiteSpace($escape)) { Fail "[基线缺逃生门] $($r.Class)" }
    Test-TargetKey $r.Class $key
    Test-RuleIds $r.Class $ids
}
foreach ($c in $expectedGuards) {
    if (-not $baseSeen.ContainsKey($c)) { Fail "[基线缺守卫] $c" }
}
foreach ($c in $expectedProjection) {
    if (-not $baseSeen.ContainsKey($c)) { Fail "[基线缺投影owner] $c" }
}
foreach ($c in ($baseSeen.Keys | Sort-Object)) {
    if ($expectedGuards -notcontains $c -and $expectedProjection -notcontains $c) { Fail "[基线多行] $c 不在 19+10 预期集合里" }
}

if ($baseRows.Count -ne 29) { Fail "[基线数量] 需要 29 行（19 守卫 + 10 投影 owner），当前 $($baseRows.Count)" }
$baseKeys = @($baseRows | ForEach-Object { ($_.Cells[1] -replace '`', '') } | Sort-Object -Unique)
if ($baseKeys.Count -ne 28) { Fail "[基线去重] 期望 28 个不同 TargetKey，实际 $($baseKeys.Count)" }

# ---------- 8) 特例类：暂不注册表化，动作枚举保留 allow / suppress ----------
$expectedSpecial = @(
    'Patch_SDS_MapDeinitAllowMirrorDespawn',
    'Patch_SDS_MirrorNotHaulable',
    'Patch_SDS_MirrorNotMergeable'
)
$specialSeen = @{}
foreach ($r in $specialRows) {
    if ($specialSeen.ContainsKey($r.Class)) { Fail "[特例重复] $($r.Class)" }
    $specialSeen[$r.Class] = $true
    if ($r.Cells.Count -lt 5) { Fail "[特例列缺失] $($r.Class)：需要 补丁类|TargetKey|动作|原因|规则"; continue }
    $key = ($r.Cells[1] -replace '`', ''); $action = $r.Cells[2]
    $reason = $r.Cells[3]; $ids = ($r.Cells[4] -replace '`', '')
    if ($specialActions -notcontains $action) { Fail "[特例动作非法] $($r.Class)：$action（只允许 allow / suppress）" }
    if ([string]::IsNullOrWhiteSpace($reason)) { Fail "[特例缺原因] $($r.Class)" }
    Test-TargetKey $r.Class $key
    Test-RuleIds $r.Class $ids
}
foreach ($c in $expectedSpecial) {
    if (-not $specialSeen.ContainsKey($c)) { Fail "[特例缺行] $c" }
}
foreach ($c in ($specialSeen.Keys | Sort-Object)) {
    if ($expectedSpecial -notcontains $c) { Fail "[特例多行] $c 不在预期集合里" }
}
if ($specialRows.Count -ne 3) { Fail "[特例数量] 期望 3，实际 $($specialRows.Count)" }

# ---------- 9) 诊断覆盖清单 §一 指标对账 ----------
$diagPath = Join-Path $docsDir '诊断覆盖清单.md'
if (Test-Path $diagPath) {
    $descriptors = @($srcRows | ForEach-Object { Get-TargetDescriptor $_ })
    $baseDescriptors = @($baseRows | ForEach-Object { ($_.Cells[1] -replace '`', '') })
    $distinctCount = @(($descriptors + $baseDescriptors) | Sort-Object -Unique).Count
    $m1 = Select-String -Path $diagPath -Pattern '\|\s*Harmony 补丁类\s*\|\s*(\d+)\s*\|' | Select-Object -First 1
    if ($m1) {
        $docClasses = [int]$m1.Matches[0].Groups[1].Value
        if ($docClasses -ne $srcRows.Count) { Fail "[指标漂移] 诊断覆盖清单 Harmony 补丁类 $docClasses ≠ 实测 $($srcRows.Count)" }
    }
    $m2 = Select-String -Path $diagPath -Pattern '\|\s*注册表条目\s*\|\s*(\d+)\s*\|' | Select-Object -First 1
    if ($m2) {
        $docRegistry = [int]$m2.Matches[0].Groups[1].Value
        if ($docRegistry -ne $registryIds.Count) { Fail "[指标漂移] 诊断覆盖清单 注册表条目 $docRegistry ≠ 实测 $($registryIds.Count)" }
    }
    $m3 = Select-String -Path $diagPath -Pattern '\|\s*补丁点合计\s*\|\s*(\d+)\s*\|' | Select-Object -First 1
    if ($m3) {
        $docPoints = [int]$m3.Matches[0].Groups[1].Value
        $actualPoints = $srcRows.Count + $registryIds.Count
        if ($docPoints -ne $actualPoints) { Fail "[指标漂移] 诊断覆盖清单 补丁点合计 $docPoints ≠ 实测 $actualPoints" }
    }
    $m4 = Select-String -Path $diagPath -Pattern '\|\s*TargetMethod 文本键去重\s*\|\s*(\d+)\s*\|' | Select-Object -First 1
    if ($m4) {
        $docDistinct = [int]$m4.Matches[0].Groups[1].Value
        if ($docDistinct -ne $distinctCount) { Fail "[指标漂移] 诊断覆盖清单 TargetMethod 文本键去重 $docDistinct ≠ 实测 $distinctCount" }
    }
}

# ---------- 10) 兼容层允许引用面（allowlist） ----------
# 《公共API》§4.5：SDS_ModCompat* / SDS_Compat* 只允许引用
#   SDS_API（public + internal 桥）、SDS_Compat* 基础设施、SDS_Diag、公共 API 类型。
# 这里用 allowlist 而不是 blocklist：新增内部实现类时不会“漏网”。
$compatAllowedExact = @(
    'SDS_API',
    'SDS_Diag',
    'SDS_StockKey',
    'SDS_WithdrawMode',
    'SDS_ApiCaller',
    'SDS_ApiResultCode',
    'SDS_ApiStockInfo',
    'SDS_ReadVersions',
    'SDS_EETransmuteState'
)
$compatAllowedPrefixes = @('SDS_API', 'SDS_Compat', 'SDS_ModCompat')
$compatFilesToScan = New-Object System.Collections.Generic.List[object]
Get-ChildItem $srcDir -Filter 'SDS_ModCompat*.cs' -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    ForEach-Object { $compatFilesToScan.Add($_) }
# SDS_CompatReports.cs 自 2026-11-12 起属 SDS.Diag（公开面在 SDS.Diag_API.cs），不再属于兼容层扫描范围。
foreach ($extra in @('SDS_CompatServices.cs')) {
    $p = Find-SourceFile $extra
    if ($p) { $compatFilesToScan.Add((Get-Item $p)) }
}
foreach ($file in $compatFilesToScan) {
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($t.StartsWith('//') -or $t.StartsWith('*')) { continue }
        # 行尾注释不参与判定；字符串里的 // 极罕见，兼容层没有。
        $code = $lines[$i]
        $comment = $code.IndexOf('//')
        if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
        foreach ($m in [regex]::Matches($code, '\bSDS_[A-Za-z0-9_]+')) {
            $sym = $m.Value
            $ok = $compatAllowedExact -contains $sym
            if (-not $ok) {
                foreach ($prefix in $compatAllowedPrefixes) {
                    if ($sym.StartsWith($prefix)) { $ok = $true; break }
                }
            }
            if (-not $ok) {
                Fail ("[兼容直调] {0}:{1} → {2}" -f $file.Name, ($i + 1), $sym)
            }
        }
    }
}

# ---------- 11) 诊断覆盖清单 §一 计数对账 ----------
$catchCount = 0; $errCount = 0; $failCount = 0; $warnCount = 0
Get-ChildItem $srcDir -Filter '*.cs' -Recurse |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
    ForEach-Object {
        foreach ($line in [System.IO.File]::ReadAllLines($_.FullName)) {
            $t = $line.Trim()
            if ($t.StartsWith('//')) { continue }
            if ($line -match 'catch\s*\(\s*Exception') { $catchCount++ }
            if ($line -match 'SDS_Diag\.ErrorOnce') { $errCount++ }
            if ($line -match 'SDS_Diag\.Fail') { $failCount++ }
            if ($line -match 'SDS_Diag\.Warn') { $warnCount++ }
        }
    }
$metricChecks = @(
    @{ Label = 'catch (Exception)'; Pattern = '\|\s*`catch \(Exception\)`\s*\|\s*(\d+)\s*\|'; Value = $catchCount },
    @{ Label = 'SDS_Diag.ErrorOnce'; Pattern = '\|\s*`SDS_Diag\.ErrorOnce`\s*\|\s*(\d+)\s*\|'; Value = $errCount },
    @{ Label = 'SDS_Diag.Fail'; Pattern = '\|\s*`SDS_Diag\.Fail`[^|]*\|\s*(\d+)\s*\|'; Value = $failCount },
    @{ Label = 'SDS_Diag.Warn'; Pattern = '\|\s*`SDS_Diag\.Warn`[^|]*\|\s*(\d+)\s*\|'; Value = $warnCount }
)
foreach ($check in $metricChecks) {
    $m = Select-String -Path $diagPath -Pattern $check.Pattern | Select-Object -First 1
    if (-not $m) { Fail ("[指标缺行] 诊断覆盖清单 {0}" -f $check.Label); continue }
    $docValue = [int]$m.Matches[0].Groups[1].Value
    if ($docValue -ne $check.Value) {
        Fail ("[指标漂移] 诊断覆盖清单 {0} {1} ≠ 实测 {2}" -f $check.Label, $docValue, $check.Value)
    }
}

if ($failed -gt 0) {
    Write-Host ("补丁清单校验失败：{0} 项（特性 {1} / 清单 {2} / 动态 {3} / 基线 {4} / 特例 {5}）" -f $failed, $srcRows.Count, $docRows.Count, $dynRows.Count, $baseRows.Count, $specialRows.Count)
    exit 1
}
Write-Host ("补丁清单校验通过：特性类 {0} + 注册表 {1} = 补丁点 {2}；清单 {3} 行；动态 {4} 行；基线 {5} 行（19 守卫 + 10 投影 owner，{6} 个不同 TargetKey）；特例 {7} 行" -f $srcRows.Count, $registryIds.Count, ($srcRows.Count + $registryIds.Count), $docRows.Count, $dynRows.Count, $baseRows.Count, $baseKeys.Count, $specialRows.Count)
exit 0

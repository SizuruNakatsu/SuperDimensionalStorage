# 程序集边界校验（四道闸门：编译期 + 类型期）
#
# 校验《模块规则.md》四 / 五 与《模块责任划分.md》二（2026-11-12 起为拆分完成的第一版）：
#   1) 工程齐全：11 个 .csproj 与《模块责任划分.md》一 产物清单逐一对应，且全部登记在 SDS.sln
#      （2026-11-12 收编：原 Sdk + 8 个 Compat 共 9 个项目合并为 SDS.ThirdModCompat 单程序集）；
#   2) 引用图：ProjectReference 只允许《模块规则.md》四 声明方向（越界即失败）；
#   3) Harmony 引用：只有 Vanilla / ThirdModCompat / Tests 允许 0Harmony；
#   4) 类型期：不在《模块责任划分.md》二 public 白名单里的顶层 public 类型视为泄漏
#      （SDS.Contracts 与 SDS.Tests 不参与该项）；
#   5) InternalsVisibleTo 登记制（当前 SDS.Core → SDS.Rules；SDS.ThirdModCompat / SDS.Vanilla → SDS.Tests）；
#   6) SDS_Host.Install 只允许 SDS.Bootstrap 调用（SDS.Tests 测试替身例外）。
#
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckAssemblyBoundaries.ps1
#
# 本脚本只读工程文件与源码，不修改任何文件。

$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $PSScriptRoot
$slnPath = Join-Path $srcDir 'SDS.sln'

if (-not (Test-Path $srcDir)) { Write-Host "找不到源码目录：$srcDir"; exit 2 }

$failed = 0
function Fail([string]$msg) { Write-Host $msg; $script:failed++ }

$expectedProjects = @(
    'SDS.Contracts',
    'SDS.Diag',
    'SDS.Core',
    'SDS.Rules',
    'SDS.Api',
    'SDS.Vanilla',
    'SDS.ThirdModCompat',
    'SDS.Bootstrap',
    'SDS.Tests',
    'SDS.Core.Tests',
    'SDS.Rules.Tests'
)

$allNames = $expectedProjects

$allowedRefs = @{
    'SDS.Contracts'                = @()
    'SDS.Diag'                     = @('SDS.Contracts')
    'SDS.Core'                     = @('SDS.Contracts', 'SDS.Diag')
    'SDS.Rules'                    = @('SDS.Contracts', 'SDS.Diag', 'SDS.Core')
    'SDS.Api'                      = @('SDS.Contracts', 'SDS.Diag', 'SDS.Core', 'SDS.Rules')
    'SDS.Vanilla'                  = @('SDS.Contracts', 'SDS.Diag', 'SDS.Core', 'SDS.Rules')
    'SDS.ThirdModCompat'           = @('SDS.Contracts', 'SDS.Diag', 'SDS.Api')
    'SDS.Bootstrap'                = @('SDS.Contracts', 'SDS.Diag', 'SDS.Core', 'SDS.Rules', 'SDS.Api', 'SDS.Vanilla')
    'SDS.Tests'                    = $allNames
    'SDS.Core.Tests'               = @('SDS.Contracts', 'SDS.Diag', 'SDS.Core')
    'SDS.Rules.Tests'              = @('SDS.Contracts', 'SDS.Diag', 'SDS.Core', 'SDS.Rules')
}

# ---------- 1) 工程齐全 + 解决方案登记 ----------
foreach ($name in $expectedProjects) {
    $proj = Join-Path (Join-Path $srcDir $name) ($name + '.csproj')
    if (-not (Test-Path $proj)) { Fail "[缺工程] $name.csproj" }
}
if (Test-Path $slnPath) {
    $slnText = Get-Content $slnPath -Encoding UTF8 -Raw
    foreach ($name in $expectedProjects) {
        if ($slnText -notmatch [regex]::Escape($name + '.csproj')) {
            Fail "[缺解决方案条目] SDS.sln 未包含 $name.csproj"
        }
    }
} else {
    Fail "[缺解决方案] $slnPath"
}

# ---------- 2) 引用图 + 3) Harmony 引用 ----------
$csprojFiles = @(Get-ChildItem $srcDir -Filter *.csproj -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })

foreach ($proj in $csprojFiles) {
    $name = [System.IO.Path]::GetFileNameWithoutExtension($proj.Name)
    $text = Get-Content $proj.FullName -Encoding UTF8 -Raw
    if ($expectedProjects -notcontains $name) {
        Fail "[多工程] $($proj.FullName) 不在 §四 产物清单里"
        continue
    }

    $allowed = $allowedRefs[$name]
    foreach ($m in [regex]::Matches($text, '<ProjectReference\s+Include="([^"]+)"')) {
        $rel = $m.Groups[1].Value -replace '\\', '/'
        $refName = [System.IO.Path]::GetFileNameWithoutExtension(($rel -split '/')[-1])
        if ($allowedRefs.ContainsKey($refName) -and $allowed -contains $refName) { continue }
        Fail ("[越界引用] {0} -> {1}" -f $name, $refName)
    }

    # 测试工程例外：可引用 0Harmony 验证挂点工具，只进测试输出，不进生产。
    $harmonyOk = ($name -eq 'SDS.Vanilla') -or ($name -eq 'SDS.ThirdModCompat') -or ($name -eq 'SDS.Tests') -or ($name -eq 'SDS.Core.Tests') -or ($name -eq 'SDS.Rules.Tests')
    $usesHarmony = ($text -match '<Reference\s+Include="0Harmony"') -or ($text -match '<SdsHarmonyRef>\s*true\s*</SdsHarmonyRef>')
    if ($usesHarmony -and -not $harmonyOk) {
        Fail "[Harmony越界] $name 不允许引用 0Harmony"
    }
}

# ---------- 4) 类型期 public 白名单（§2.1） ----------
$publicWhitelist = @{
    'SDS.Diag'       = @('SDS_Diag', 'SDS_StaticState', 'ISDSResettable', 'SDS_SelfCheck',
                          'SDS_CompatReportHub', 'SDS_CompatPatchReport', 'SDS_CompatAdapterReport')
    'SDS.Core'       = @('SDS_Core', 'ISDS_LedgerRead', 'ISDS_LedgerHost')
    'SDS.Rules'      = @('SDS_Rules', 'SDS_Availability', 'SDS_ImplantUtility')
    'SDS.Api'        = @('SDS_API')
    'SDS.ThirdModCompat' = @()   # 插件程序集：被依赖 0，适配器与 Sdk 工具类全部 internal（Bootstrap 反射发现）
    'SDS.Bootstrap'  = @('SDS_Mod')
}
$vanillaExact = @(
    'SDS_Vanilla',
    'SDS_GameComponent_Grid', 'SDS_EndpointBuffer',
    'SDS_CompUsable', 'SDS_CompProperties_InstallChip', 'SDS_CompInstallChip',
    'SDS_Comp_SDS_DrawExtraOverlay'
)
$vanillaPrefixes = @('Building_SDS_', 'SDS_Proxy_', 'JobDriver_SDS_')

# 引擎按名实例化 / 序列化的类型：允许散落在非 API 文件，但禁止被其它模块引用（E8 引用侧）。
$engineVisibleExact = @(
    'SDS_GameComponent_Grid', 'SDS_EndpointBuffer',
    'SDS_CompUsable', 'SDS_CompProperties_InstallChip', 'SDS_CompInstallChip',
    'SDS_Comp_SDS_DrawExtraOverlay'
)
$engineVisiblePrefixes = $vanillaPrefixes
function Test-EngineVisible([string]$name) {
    if ($engineVisibleExact -contains $name) { return $true }
    foreach ($prefix in $engineVisiblePrefixes) {
        if ($name.StartsWith($prefix)) { return $true }
    }
    return $false
}

$typeRegex = '^\s*public\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*(?:class|struct|enum|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)'

$sourceFiles = @(Get-ChildItem $srcDir -Filter *.cs -Recurse -File |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })

foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if ($project -eq 'SDS.Contracts' -or $project -eq 'SDS.Tests' -or $project -eq 'SDS.Core.Tests' -or $project -eq 'SDS.Rules.Tests') { continue }
    $declared = @()
    foreach ($line in (Get-Content $file.FullName -Encoding UTF8)) {
        if ($line -match $typeRegex) { $declared += $Matches[1] }
    }
    if ($declared.Count -eq 0) { continue }

    if ($publicWhitelist.ContainsKey($project)) {
        $allow = $publicWhitelist[$project]
        foreach ($typeName in $declared) {
            if ($allow -notcontains $typeName) {
                Fail ("[public泄漏] {0}：{1}（{2}）" -f $project, $typeName, $file.Name)
            }
        }
    } elseif ($project -eq 'SDS.Vanilla') {
        foreach ($typeName in $declared) {
            $ok = $vanillaExact -contains $typeName
            if (-not $ok) {
                foreach ($prefix in $vanillaPrefixes) {
                    if ($typeName.StartsWith($prefix)) { $ok = $true; break }
                }
            }
            if (-not $ok) { Fail ("[public泄漏] SDS.Vanilla：{0}（{1}）" -f $typeName, $file.Name) }
        }
    } else {
        foreach ($typeName in $declared) {
            Fail ("[public无白名单] {0}：{1}（{2}）" -f $project, $typeName, $file.Name)
        }
    }
}

# ---------- 4.5) E8 公开面登记：public 顶层类型只允许在登记文件里（模块登记制） ----------
# 登记在案的模块：public 顶层类型只允许声明在登记形态的文件里（支持 * 通配，兼容 partial 分文件）。
# 未登记的模块（Bootstrap / ThirdModCompat）暂时只受 §4 的类型白名单约束。
$apiSurfaceFiles = @{
    'SDS.Diag' = @('SDS.Diag_API.cs')
    'SDS.Core' = @('SDS.Core_API.cs')
    'SDS.Rules' = @('SDS.Rules_API.cs')
    'SDS.Api'  = @('*_API*.cs')
    'SDS.Vanilla' = @('SDS.Vanilla_API.cs')
}
foreach ($project in $apiSurfaceFiles.Keys) {
    $projectDir = Join-Path $srcDir $project
    foreach ($pattern in $apiSurfaceFiles[$project]) {
        $found = @(Get-ChildItem $projectDir -Filter $pattern -File -Recurse -ErrorAction SilentlyContinue)
        if ($found.Count -eq 0) {
            Fail ("[公开面文件缺失] {0}：登记文件 {1} 不存在" -f $project, $pattern)
        }
    }
}
foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if (-not $apiSurfaceFiles.ContainsKey($project)) { continue }
    $patterns = $apiSurfaceFiles[$project]
    $isSurfaceFile = $false
    foreach ($pattern in $patterns) {
        if ($file.Name -like $pattern) { $isSurfaceFile = $true; break }
    }
    if ($isSurfaceFile) { continue }
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match $typeRegex) {
            if ($project -eq 'SDS.Vanilla' -and (Test-EngineVisible $Matches[1])) { continue }
            Fail ("[公开面文件越界] {0}：{1}:{2} 的 public 类型 {3} 必须声明在 {4}" -f `
                $project, $file.Name, ($i + 1), $Matches[1], ($patterns -join ' / '))
        }
    }
}

# ---------- 4.6) E8 消费侧：用到的 Contracts 类型必须在模块 接口.md 登记 ----------
$docsDir = Join-Path (Split-Path -Parent (Split-Path -Parent $srcDir)) '超维存储框架'
$contractsSymbols = @()
foreach ($file in ($sourceFiles | Where-Object { $_.FullName -match '\\SDS\.Contracts\\' })) {
    foreach ($line in (Get-Content $file.FullName -Encoding UTF8)) {
        if ($line -match $typeRegex) { $contractsSymbols += $Matches[1] }
    }
}
$contractsSymbols = @($contractsSymbols | Sort-Object -Unique)
foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if ($project -eq 'SDS.Contracts' -or $project -eq 'SDS.Tests' -or $project -eq 'SDS.Core.Tests' -or $project -eq 'SDS.Rules.Tests') { continue }
    $docPath = Join-Path $docsDir ($project + '/接口.md')
    if (-not (Test-Path $docPath)) { continue }
    $doc = Get-Content $docPath -Raw -Encoding UTF8
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $code = $lines[$i]
        $comment = $code.IndexOf('//')
        if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
        foreach ($m in [regex]::Matches($code, '\bSDS_[A-Za-z0-9_]+\b')) {
            if ($contractsSymbols -notcontains $m.Value) { continue }
            if ($doc -match [regex]::Escape($m.Value)) { continue }
            Fail ("[契约未登记] {0}：{1}:{2} → {3} 未在该模块 接口.md 的消费面登记" -f `
                $project, $file.Name, ($i + 1), $m.Value)
        }
    }
}

# ---------- 4.7) E8 引用侧：引擎可见类型禁止被其它模块引用 ----------
foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if ($project -eq 'SDS.Vanilla' -or $project -eq 'SDS.Contracts' -or $project -eq 'SDS.Tests' -or $project -eq 'SDS.Core.Tests' -or $project -eq 'SDS.Rules.Tests') {
        continue
    }
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $code = $lines[$i]
        $comment = $code.IndexOf('//')
        if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
        foreach ($m in [regex]::Matches($code, '\bSDS_[A-Za-z0-9_]+\b')) {
            if (Test-EngineVisible $m.Value) {
                Fail ("[引擎类型越界] {0}：{1}:{2} → {3} 只允许 Vanilla 本模块引用" -f `
                    $project, $file.Name, ($i + 1), $m.Value)
            }
        }
    }
}

# ---------- 4.8) E9 公开面文件不承载领域决策 ----------
# 登记模块的公开面文件里不得出现控制流；对外准入门 SDS_API 是唯一登记的例外。
$controlFlowRegex = '^\s+(if|else|for|foreach|while|switch|try|catch)\b'
foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if (-not $apiSurfaceFiles.ContainsKey($project)) { continue }
    if ($project -eq 'SDS.Api') { continue }
    $isSurfaceFile = $false
    foreach ($pattern in $apiSurfaceFiles[$project]) {
        if ($file.Name -like $pattern) { $isSurfaceFile = $true; break }
    }
    if (-not $isSurfaceFile) { continue }
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $code = $lines[$i]
        $comment = $code.IndexOf('//')
        if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
        if ($code -match $controlFlowRegex) {
            Fail ("[E9领域决策] {0}：{1}:{2}：公开面文件不得出现控制流（例外仅 SDS_API）" -f `
                $project, $file.Name, ($i + 1))
        }
    }
}

# ---------- 5) InternalsVisibleTo 登记制 ----------
# 允许的友元：授予方程序集 → 被授予方。新增必须在下表登记，未登记即失败。
$friendGrants = @{
    'SDS.Core'           = @('SDS.Rules')   # P3 启用：规则模块读取账本写接口（流程层已迁入 Rules）
    'SDS.ThirdModCompat' = @('SDS.Tests')   # 测试程序集直引 internal 适配器 / Sdk 工具类
    'SDS.Vanilla'        = @('SDS.Tests')   # 测试程序集直引 internal 宿主实现 / 补丁计数
}
foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    $matches5 = @(Select-String -Path $file.FullName -Pattern 'InternalsVisibleTo\("([^"]+)"\)' -Encoding UTF8)
    if ($matches5.Count -eq 0) {
        if (Select-String -Path $file.FullName -Pattern 'InternalsVisibleTo' -SimpleMatch -Quiet) {
            Fail "[友元格式] $($file.Name)：InternalsVisibleTo 必须写作 InternalsVisibleTo(\"程序集名\")"
        }
        continue
    }
    foreach ($m5 in $matches5) {
        $granted = $m5.Matches[0].Groups[1].Value
        $allowed = @(); if ($friendGrants.ContainsKey($project)) { $allowed = $friendGrants[$project] }
        if ($allowed -notcontains $granted) {
            Fail "[越权友元] $project/$($file.Name)：InternalsVisibleTo('$granted') 未登记"
        }
    }
}

# ---------- 6) SDS_Host.Install 只允许 Bootstrap（SDS.Tests 测试例外） ----------
foreach ($file in $sourceFiles) {
    if (-not (Select-String -Path $file.FullName -Pattern 'SDS_Host.Install' -SimpleMatch -Quiet)) { continue }
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if ($project -ne 'SDS.Bootstrap' -and $project -ne 'SDS.Tests' -and $project -ne 'SDS.Core.Tests' -and $project -ne 'SDS.Rules.Tests') {
        Fail "[Host.Install越界] $project/$($file.Name)：SDS_Host.Install 只允许 SDS.Bootstrap"
    }
}

# ---------- 7) SDS.Contracts 禁止符号（模块规则.md 6.1） ----------
$contractsDir = Join-Path $srcDir 'SDS.Contracts'
$contractFiles = @(Get-ChildItem $contractsDir -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
$bannedSymbols = @(
    @{ Pattern = 'Harmony';            Label = 'Harmony' },
    @{ Pattern = '\bWindow\b';         Label = 'UI.Window' },
    @{ Pattern = '\bITab\b';           Label = 'UI.ITab' },
    @{ Pattern = '\bWidgets\.';        Label = 'UI.Widgets' },
    @{ Pattern = '\bGUI\.';            Label = 'UI.GUI' },
    @{ Pattern = '\bPathFinder\b';     Label = '寻路.PathFinder' },
    @{ Pattern = '\bReachability\b';   Label = '寻路.Reachability' },
    @{ Pattern = '\bTraverseParms\b';  Label = '寻路.TraverseParms' },
    @{ Pattern = '\bGenClosest\b';     Label = '寻路.GenClosest' },
    @{ Pattern = '\bJobDriver\b';      Label = '行为.JobDriver' },
    @{ Pattern = '\bToil\b';           Label = '行为.Toil' },
    @{ Pattern = '\bThinkTree';        Label = '行为.ThinkTree' },
    @{ Pattern = '\bWorkGiver';        Label = '行为.WorkGiver' },
    @{ Pattern = '\bDutyDef\b';        Label = '行为.DutyDef' },
    @{ Pattern = '\bListerThings\b';   Label = '搜索.ListerThings' },
    @{ Pattern = '\bSlotGroup\b';      Label = '存储.SlotGroup' },
    @{ Pattern = '\bSDS_Core\b';       Label = '实现引用.SDS_Core' },
    @{ Pattern = '\bSDS_Diag\b';       Label = '实现引用.SDS_Diag' },
    @{ Pattern = '\bSDS_API\b';        Label = '实现引用.SDS_API' },
    @{ Pattern = '\bSDS_Ledger\b';     Label = '实现引用.SDS_Ledger' },
    @{ Pattern = '\bSDS_Vanilla\b';    Label = '实现引用.SDS_Vanilla' },
    @{ Pattern = '\bSDS_Mod\b';        Label = '实现引用.SDS_Mod' }
)
foreach ($file in $contractFiles) {
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($t.StartsWith('//')) { continue }
        foreach ($rule in $bannedSymbols) {
            if ($lines[$i] -match $rule.Pattern) {
                Fail ("[Contracts禁止符号] {0}:{1} → {2}" -f $file.Name, ($i + 1), $rule.Label)
            }
        }
    }
}

# ---------- 8) SDS.Core 禁止符号（模块规则.md 6.3） ----------
$coreDir = Join-Path $srcDir 'SDS.Core'
$coreFiles = @(Get-ChildItem $coreDir -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
$coreBanned = @(
    @{ Pattern = 'Harmony';                 Label = 'Harmony' },
    @{ Pattern = '\bJobDriver\b';           Label = 'JobDriver' },
    @{ Pattern = '\bToil\b';                Label = 'Toil' },
    @{ Pattern = '\bThinkTree';             Label = 'ThinkTree' },
    @{ Pattern = '\bWorkGiver';             Label = 'WorkGiver' },
    @{ Pattern = '\bSlotGroup\b';           Label = 'SlotGroup' },
    @{ Pattern = '\bReachability\b';        Label = 'Reachability' },
    @{ Pattern = '\bListerThings\b';        Label = 'ListerThings' },
    @{ Pattern = '\bGenClosest\b';          Label = 'GenClosest' },
    @{ Pattern = '\bResourceCounter\b';     Label = 'ResourceCounter' },
    @{ Pattern = '\bIHaulDestination\b';    Label = 'IHaulDestination' },
    @{ Pattern = '\bGenPlace\b';            Label = 'GenPlace' },
    @{ Pattern = '\bGenSpawn\b';            Label = 'GenSpawn' },
    @{ Pattern = '\bFind\.';                Label = 'Find.*' },
    @{ Pattern = '\.mapPawns\b';            Label = 'mapPawns' },
    @{ Pattern = '\bWindow\b';              Label = 'UI.Window' },
    @{ Pattern = '\bITab\b';                Label = 'UI.ITab' },
    @{ Pattern = '\bWidgets\.';             Label = 'UI.Widgets' },
    @{ Pattern = '\bSDS_ModCompat';         Label = '兼容层引用' },
    @{ Pattern = '\bSDS_Vanilla\b';         Label = 'Vanilla 引用' }
)
foreach ($file in $coreFiles) {
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $t = $lines[$i].Trim()
        if ($t.StartsWith('//')) { continue }
        foreach ($rule in $coreBanned) {
            if ($lines[$i] -match $rule.Pattern) {
                Fail ("[Core禁止符号] {0}:{1} → {2}" -f $file.Name, ($i + 1), $rule.Label)
            }
        }
        if ($lines[$i] -match '\bIThingHolder\b' -and $file.Name -ne 'SDS_Ledger.cs') {
            Fail ("[Core禁止符号] {0}:{1} → IThingHolder（仅 SDS_Ledger 的私有 holder 允许）" -f $file.Name, ($i + 1))
        }
    }
}

# ---------- 9) SDS_Config.Current 只允许 Bootstrap 写入 ----------
foreach ($file in $sourceFiles) {
    $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
    $project = ($rel -split '\\')[0]
    if ($project -eq 'SDS.Tests' -or $project -eq 'SDS.Core.Tests' -or $project -eq 'SDS.Rules.Tests') { continue }
    $lines = Get-Content $file.FullName -Encoding UTF8
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match 'SDS_Config\.Current\s*=') {
            if ($project -ne 'SDS.Bootstrap') {
                Fail ("[Config.Current越界写] {0}:{1} 只允许 SDS.Bootstrap 写入" -f $rel, ($i + 1))
            }
        }
    }
}

# ---------- 10) SDS.Vanilla 通道分部（虚拟 / 保真两向禁引） ----------
# 虚拟专属目录 Mirrors/ 与保真专属目录 Preserve/ 互不引用对方目录里声明的顶层类型；
# 共享目录（Endpoints / Foundation / Jobs / Query / TradeTransport / UI / Install）是唯一合法桥。
# 依据：SDS.Vanilla/规则.md 通道分部节；端口 / UI / 存档 / 回收 / 挂点按设计原则必须共用，
# 可拆分的只有两个通道专属目录之间的直接依赖。
$vanillaDir = Join-Path $srcDir 'SDS.Vanilla'
$mirrorsDir = Join-Path $vanillaDir 'Mirrors'
$preserveDir = Join-Path $vanillaDir 'Preserve'
$typeDeclRegex = '^\s*(?:public|internal)\s+(?:(?:static|sealed|abstract|partial|readonly|ref)\s+)*(?:class|struct|enum|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)'
function Get-VanillaFolderTypes([string]$folder) {
    $names = @()
    $folderFiles = @(Get-ChildItem $folder -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    foreach ($folderFile in $folderFiles) {
        foreach ($line in (Get-Content $folderFile.FullName -Encoding UTF8)) {
            if ($line -match $typeDeclRegex) { $names += $Matches[1] }
        }
    }
    return @($names | Sort-Object -Unique)
}
function Test-VanillaChannelRefs([string]$sideDir, [string]$sideLabel, [string[]]$bannedTypes, [string]$otherLabel) {
    if ($bannedTypes.Count -eq 0) { return }
    $sideFiles = @(Get-ChildItem $sideDir -Filter *.cs -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    foreach ($sideFile in $sideFiles) {
        $lines = Get-Content $sideFile.FullName -Encoding UTF8
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $code = $lines[$i]
            $comment = $code.IndexOf('//')
            if ($comment -ge 0) { $code = $code.Substring(0, $comment) }
            foreach ($m in [regex]::Matches($code, '\bSDS_[A-Za-z0-9_]+\b')) {
                if ($bannedTypes -contains $m.Value) {
                    Fail ("[通道分部越界] {0}/{1}:{2} → {3} 属 {4} 专属类型（{5} 侧禁引）" -f `
                        $sideLabel, $sideFile.Name, ($i + 1), $m.Value, $otherLabel, $sideLabel)
                }
            }
        }
    }
}
if ((Test-Path $mirrorsDir) -and (Test-Path $preserveDir)) {
    $mirrorsTypes = Get-VanillaFolderTypes $mirrorsDir
    $preserveTypes = Get-VanillaFolderTypes $preserveDir
    Test-VanillaChannelRefs $mirrorsDir 'Mirrors' $preserveTypes 'Preserve'
    Test-VanillaChannelRefs $preserveDir 'Preserve' $mirrorsTypes 'Mirrors'
}

if ($failed -gt 0) {
    Write-Host ("程序集边界校验失败：{0} 项（工程 {1}）" -f $failed, $csprojFiles.Count)
    exit 1
}
Write-Host ("程序集边界校验通过：工程 {0}；源码文件 {1}；引用图 / 白名单 / 友元 / Host.Install / Contracts与Core禁止符号 / Config.Current写点 / 通道分部 均无越界" -f $csprojFiles.Count, $sourceFiles.Count)
exit 0

# 公开面清单（派生工件，不定义规则）
#
# 目的：让「模块公开面变动」成为一个可审的 diff 单元（《设计原则.md》§三 L2/L3 的工件）。
# 口径：只扫描各模块登记形态的公开面文件（*_API*.cs），提取 public 顶层类型与 public 成员。
#       —— 与 CheckAssemblyBoundaries.ps1 §4.5 配合：公开类型必须声明在这些文件里，
#          因此本清单即为该模块对外可达面的完整集合。
# 本脚本不修改源码；-Write 只重写 Tools/PublicSurface/ 下的基线文本。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\DumpPublicSurface.ps1           # 打印
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\DumpPublicSurface.ps1 -Write    # 更新基线
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\DumpPublicSurface.ps1 -Check    # 对账，有差异即非零退出

[CmdletBinding()]
param(
    [switch]$Write,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $PSScriptRoot
$baselineDir = Join-Path $PSScriptRoot 'PublicSurface'

$typeRegex = '^\s{0,6}public\s+(?:(?:static|sealed|abstract|partial|readonly|ref|new)\s+)*(class|struct|enum|interface|record)\s+([A-Za-z_][A-Za-z0-9_]*)'
$memberRegex = '^\s{8,}public\s'
$ifaceMemberRegex = '^[A-Za-z_<][^=]*[;}]\s*$'

function Normalize([string]$text) {
    return ([regex]::Replace($text, '\s+', ' ')).Trim()
}

# 只保留签名：去掉表达式体与方法体，避免转发实现变动污染公开面 diff。
function SignatureOf([string]$text) {
    $t = Normalize $text
    $arrow = $t.IndexOf('=>')
    if ($arrow -ge 0) { $t = $t.Substring(0, $arrow).Trim() }
    $brace = $t.IndexOf(' {')
    if ($brace -ge 0) { $t = $t.Substring(0, $brace).Trim() }
    return $t
}

function Get-SurfaceLines([string]$path) {
    $lines = Get-Content $path -Encoding UTF8
    $out = New-Object System.Collections.Generic.List[string]
    $currentType = $null
    $currentIsInterface = $false
    $pending = $null
    $paren = 0
    $depth = 0

    foreach ($raw in $lines) {
        $line = $raw.Trim()
        $open = ([regex]::Matches($line, '\{')).Count
        $close = ([regex]::Matches($line, '\}')).Count
        $justDeclared = $false

        if ($line -eq '' -or $line.StartsWith('//')) {
            # 空行 / 行注释：不改变深度，也不结束跨行声明
        }
        elseif ($pending -ne $null) {
            $pending = $pending + ' ' + $line
            $paren += ([regex]::Matches($line, '\(')).Count - ([regex]::Matches($line, '\)')).Count
            if ($paren -le 0 -and ($line -match '[;{]' -or $line.Contains('=>'))) {
                $out.Add($currentType + ' :: ' + (SignatureOf $pending))
                $pending = $null
                $paren = 0
            }
        }
        elseif ($depth -eq 1 -and $line -match $typeRegex) {
            $currentIsInterface = ($Matches[1] -eq 'interface')
            $currentType = $Matches[2]
            $out.Add('[type] ' + $currentType + ' :: ' + (Normalize $line))
            $justDeclared = $true
        }
        elseif ($depth -eq 2 -and $currentType -ne $null -and $raw -match $memberRegex) {
            $paren = ([regex]::Matches($line, '\(')).Count - ([regex]::Matches($line, '\)')).Count
            if ($paren -le 0 -and ($line -match '[;{]' -or $line.Contains('=>'))) {
                $out.Add($currentType + ' :: ' + (SignatureOf $line))
                $paren = 0
            } else {
                $pending = $line
            }
        }
        elseif ($depth -eq 2 -and $currentIsInterface -and $line -match $ifaceMemberRegex) {
            $out.Add($currentType + ' :: ' + (SignatureOf $line))
        }

        $depth += $open - $close
        if (-not $justDeclared -and $depth -le 1 -and $pending -eq $null) {
            $currentType = $null
            $currentIsInterface = $false
        }
    }
    return $out
}

function Get-SurfaceByProject {
    $result = @{}
    $targets = New-Object System.Collections.Generic.List[object]
    # 已登记公开面文件
    Get-ChildItem $srcDir -Filter '*_API*.cs' -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        Where-Object { $_.FullName -notmatch '\\SDS\.Tests\\' } |
        ForEach-Object { $targets.Add($_) }
    # 语言面模块（Contracts）：类型定义处没有 facade，整模块扫描
    Get-ChildItem (Join-Path $srcDir 'SDS.Contracts') -Filter '*.cs' -Recurse -File |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' } |
        ForEach-Object { $targets.Add($_) }
    $apiFiles = @($targets | Sort-Object FullName -Unique)
    foreach ($file in $apiFiles) {
        $rel = $file.FullName.Substring($srcDir.Length).TrimStart('\')
        $project = ($rel -split '\\')[0]
        if (-not $result.ContainsKey($project)) {
            $result[$project] = New-Object System.Collections.Generic.List[string]
        }
        foreach ($line in (Get-SurfaceLines $file.FullName)) {
            if (-not $result[$project].Contains($line)) { $result[$project].Add($line) }
        }
    }
    return $result
}

function Get-Baseline([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    return @(Get-Content $path -Encoding UTF8 |
        Where-Object { $_ -ne '' -and -not $_.StartsWith('#') })
}

$surface = Get-SurfaceByProject
$projects = @($surface.Keys | Sort-Object)

if ($Write) {
    if (-not (Test-Path $baselineDir)) { New-Item -ItemType Directory -Path $baselineDir | Out-Null }
    foreach ($project in $projects) {
        $target = Join-Path $baselineDir ($project + '.public.txt')
        $header = @(
            "# $project 公开面基线（派生工件，勿手改）",
            '# 生成：Tools/DumpPublicSurface.ps1 -Write；校验：Tools/DumpPublicSurface.ps1 -Check',
            '# 差异即公开面变更：按《设计原则.md》§三 判定 L1 / L2 / L3。'
        )
        $body = @($surface[$project] | Sort-Object)
        [System.IO.File]::WriteAllLines($target, [string[]]($header + $body),
            (New-Object System.Text.UTF8Encoding($true)))
        Write-Host ("[写入] {0}：{1} 条" -f $target, $body.Count)
    }
    exit 0
}

if ($Check) {
    $failed = 0
    foreach ($project in $projects) {
        $target = Join-Path $baselineDir ($project + '.public.txt')
        $baseline = Get-Baseline $target
        $current = @($surface[$project] | Sort-Object)
        if ($baseline -eq $null) {
            Write-Host ("[缺少基线] {0}：请先运行 -Write 并提交基线" -f $project)
            $failed++
            continue
        }
        $added = @($current | Where-Object { $baseline -notcontains $_ })
        $removed = @($baseline | Where-Object { $current -notcontains $_ })
        foreach ($item in $added) { Write-Host ("[新增] {0} :: {1}" -f $project, $item); $failed++ }
        foreach ($item in $removed) { Write-Host ("[删除] {0} :: {1}" -f $project, $item); $failed++ }
    }
    if ($failed -gt 0) {
        Write-Host ("[公开面] 有 {0} 处变更：确认分级并运行 -Write 更新基线。" -f $failed)
        exit 1
    }
    Write-Host "[公开面] 与基线一致。"
    exit 0
}

foreach ($project in $projects) {
    Write-Host ('## ' + $project)
    foreach ($line in @($surface[$project] | Sort-Object)) { Write-Host ('  ' + $line) }
}
exit 0

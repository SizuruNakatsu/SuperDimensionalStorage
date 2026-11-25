# 规则 ID 覆盖校验（L2 变更提交前必须跑）
#
# 校验 规则索引.md 里的每个 ID：
#   1) 在其「权威文档」里至少出现一次；
#   2) 若「代码锚点」不是 —，在该源码文件里至少出现一次。
#
# 用法：powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\CheckRuleIds.ps1
#
# 路径：脚本在 超维存储/Source/Tools；源码根 = 上一级；设计文档全部位于
#       超维存储框架/（2026-11-12 迁移；旧框架/ 为纯历史存档，不再被读取）。
#       -SourceRoot 可覆盖源码根，用于对历史基线做回归对照。

param([string]$SourceRoot = '')

$ErrorActionPreference = 'Stop'
$srcDir = Split-Path -Parent $PSScriptRoot
$root = Split-Path -Parent (Split-Path -Parent $srcDir)
$docsDir = Join-Path $root '超维存储框架'
if ($SourceRoot) { $srcDir = $SourceRoot }
$indexPath = Join-Path $docsDir '规则索引.md'

if (-not (Test-Path $indexPath)) { Write-Host "找不到 规则索引.md" ; exit 2 }
if (-not (Test-Path $srcDir)) { Write-Host "找不到源码目录：$srcDir" ; exit 2 }

$rows = Get-Content $indexPath -Encoding UTF8 | Where-Object { $_ -match '^\|\s*[RWES]\d+[a-z]?\s*\|' }
if (-not $rows -or $rows.Count -eq 0) { Write-Host "规则索引里没有任何规则行" ; exit 2 }

# 代码锚点只写文件名；新布局按模块分目录，所以按文件名递归解析。
# 同名文件可能存在于多个模块（如 Contracts / Vanilla 都有 SDS_UI.cs）：
#   优先返回「真正包含该规则 ID」的那一份，其次才是第一个同名文件。
function Resolve-SourceFile([string]$name, [string]$id = '') {
    $direct = Join-Path $srcDir $name
    if (Test-Path $direct) { return $direct }
    $hits = @(Get-ChildItem $srcDir -Filter $name -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })
    if ($hits.Count -eq 0) { return '' }
    if ($id) {
        foreach ($hit in $hits) {
            if (Select-String -Path $hit.FullName -Pattern $id -SimpleMatch -Quiet) {
                return $hit.FullName
            }
        }
    }
    return $hits[0].FullName
}

$failed = 0
$checked = 0
foreach ($row in $rows) {
    $cells = $row.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
    if ($cells.Count -lt 5) { continue }
    $id = $cells[0]
    $docLabel = ($cells[2] -replace '`', '')
    $docName = ($docLabel -split '\s+')[0]
    $codeFile = ($cells[3] -replace '`', '')
    $docPath = Join-Path $docsDir ($docName + '.md')
    $checked++

    if (-not (Test-Path $docPath)) {
        Write-Host "[缺文档] $id -> $docName.md 不存在"
        $failed++
    } elseif (-not (Select-String -Path $docPath -Pattern $id -SimpleMatch -Quiet)) {
        Write-Host "[缺引用] $id 未出现在 $docName.md"
        $failed++
    }

    if ($codeFile -ne '—' -and $codeFile -ne '-' -and $codeFile.Length -gt 0) {
        $codePath = Resolve-SourceFile $codeFile $id
        if (-not $codePath) {
            Write-Host "[缺源码] $id -> $codeFile 不存在"
            $failed++
        } elseif (-not (Select-String -Path $codePath -Pattern $id -SimpleMatch -Quiet)) {
            Write-Host "[缺锚点] $id 未出现在 $codeFile"
            $failed++
        }
    }
}

if ($failed -gt 0) {
    Write-Host "规则 ID 覆盖校验失败：$failed 项（共 $checked 条）"
    exit 1
}
Write-Host "规则 ID 覆盖校验通过：$checked 条"
exit 0

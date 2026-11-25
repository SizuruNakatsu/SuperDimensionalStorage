# 测试构建范围机器校验（E11 的测试侧扩展）
#
# 规则：测试构建只允许编译测试工程，不得重建任何生产 DLL。
# 做法：
#   1) 可选先跑 BuildModule.ps1（只构建目标模块并做范围校验）；
#   2) 记录 超维存储/Assemblies/*.dll 的 SHA256；
#   3) dotnet build <测试工程> --no-dependencies（只编译测试工程）；
#   4) 再记录 SHA256；任何生产 DLL 变化即失败；
#   5) dotnet test --no-build --no-restore 跑已编译的测试。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\TestModule.ps1 -Module SDS.ThirdModCompat
#   ... -TestProject SDS.Tests -Filter "FullyQualifiedName~CompatAdapterTests"
#
# 退出码：0 = 通过；1 = 生产 DLL 被测试构建改动 / 构建或测试失败；2 = 找不到测试工程。

param(
    [string]$Module,
    [string]$TestProject = 'SDS.Tests',
    [string[]]$AllowChanged = @(),
    [string]$Filter,
    [string]$Configuration = 'Debug',
    [switch]$AcknowledgeFullRebuild,
    [switch]$AcknowledgeReferenceChange
)

$ErrorActionPreference = 'Stop'

$srcDir = Split-Path -Parent $PSScriptRoot
$moduleRoot = Split-Path -Parent $srcDir
$asmDir = Join-Path $moduleRoot 'Assemblies'
$testCsproj = Join-Path (Join-Path $srcDir $TestProject) ($TestProject + '.csproj')

if (-not (Test-Path -LiteralPath $testCsproj)) {
    Write-Host "[Test] 找不到测试工程：$testCsproj"
    exit 2
}

function Get-AssemblyHashes {
    $map = @{}
    if (-not (Test-Path -LiteralPath $asmDir)) {
        return $map
    }
    Get-ChildItem -LiteralPath $asmDir -Filter *.dll -File | ForEach-Object {
        $map[[System.IO.Path]::GetFileNameWithoutExtension($_.Name)] =
            (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    return $map
}

if ($Module) {
    $buildModule = Join-Path $PSScriptRoot 'BuildModule.ps1'
    $buildModuleArgs = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $buildModule,
        '-Module', $Module, '-Configuration', $Configuration)
    if ($AllowChanged.Count -gt 0) {
        $buildModuleArgs += @('-AllowChanged', ($AllowChanged -join ','))
    }
    if ($AcknowledgeFullRebuild) {
        $buildModuleArgs += '-AcknowledgeFullRebuild'
    }
    if ($AcknowledgeReferenceChange) {
        $buildModuleArgs += '-AcknowledgeReferenceChange'
    }
    & powershell @buildModuleArgs
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[Test] 模块构建 / 范围校验失败，已停止。"
        exit $LASTEXITCODE
    }
}

$before = Get-AssemblyHashes

$buildArgs = @('build', $testCsproj, '-c', $Configuration, '-v', 'minimal', '--no-dependencies')
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$after = Get-AssemblyHashes
$names = New-Object 'System.Collections.Generic.List[string]'
foreach ($key in $before.Keys) { if (-not $names.Contains($key)) { $names.Add($key) } }
foreach ($key in $after.Keys) { if (-not $names.Contains($key)) { $names.Add($key) } }

$changed = @()
foreach ($name in $names) {
    if ($before[$name] -ne $after[$name]) {
        $changed += $name
    }
}
$changed = @($changed | Sort-Object)

if ($changed.Count -gt 0) {
    Write-Host ("[Test] 测试构建改动了生产程序集：" + ($changed -join ', '))
    Write-Host "[Test] 必须使用 --no-dependencies；不得让测试工程重建生产 DLL。"
    exit 1
}

Write-Host "[Test] 测试构建未改动任何生产程序集。"
if ($Module) {
    Write-Host ("[Test] 已先构建模块：" + $Module)
}

$testArgs = @('test', $testCsproj, '-c', $Configuration, '--no-build', '--no-restore', '-v', 'minimal')
if ($Filter) {
    $testArgs += @('--filter', $Filter)
}
& dotnet @testArgs
exit $LASTEXITCODE

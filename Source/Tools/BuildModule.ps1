# E11 构建范围与取证（权威：设计原则.md §三）
#
# 目标：源码未变的模块，其 DLL 不得因构建副作用被重写。
# 做法：
#   1) 用 .build-state.json 记录每个模块的源码 / 公开面 / DLL 指纹，以及 GameDir / props / SDK / 引用程序集指纹；
#   2) 源码未变且 DLL 指纹一致 → 跳过该模块；源码变化或 DLL 缺失 → 只构建该模块（--no-dependencies）；
#   3) 引用态变化（GameDir / props / SDK / 引用程序集）默认拒绝构建，必须显式 -AcknowledgeReferenceChange；
#   4) 公开面变化或 InternalsVisibleTo 友元耦合变化时，依赖模块必须重编，但默认拒绝，必须显式 -AllowChanged 列出；
#   5) 源码未变但 DLL 指纹与状态不一致 → 拒绝构建，防止旧构建副作用或外部修改被静默接受；
#   6) 构建后仍比对 Assemblies/*.dll 的 SHA256；未批准的 DLL 变化会从备份回滚并失败。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File .\Tools\BuildModule.ps1 -Module SDS.Vanilla
#   ... -Module SDS.Vanilla -DryRun                    # 只打印本次会构建哪些模块
#   ... -Module SDS.Vanilla -InitializeState           # 只写状态基线，不构建
#   ... -Module SDS.Vanilla -AcknowledgeReferenceChange
#   ... -Module SDS.Vanilla -ForceRebuild              # 显式全量重编目标闭包
#   ... -Module SDS.Vanilla -AllowChanged SDS.Core,SDS.Vanilla
#   ... -Module SDS.Vanilla -NoDependencies
#   ... -Module SDS.Vanilla -AcknowledgeFullRebuild    # 兼容旧用法，等价于 -AcknowledgeReferenceChange
#
# 退出码：0 = 范围内；1 = 越界 / 未批准的引用态或公开面变化；2 = 找不到工程或引用程序集。

param(
    [Parameter(Mandatory = $true)]
    [string]$Module,
    [string[]]$AllowChanged = @(),
    [switch]$NoDependencies,
    [switch]$AcknowledgeFullRebuild,
    [switch]$AcknowledgeReferenceChange,
    [switch]$ForceRebuild,
    [switch]$InitializeState,
    [switch]$DryRun,
    [string]$GameDir,
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$srcDir = Split-Path -Parent $PSScriptRoot
$moduleRoot = Split-Path -Parent $srcDir
$asmDir = Join-Path $moduleRoot 'Assemblies'
$statePath = Join-Path $PSScriptRoot '.build-state.json'
$publicSurfaceDir = Join-Path $PSScriptRoot 'PublicSurface'

# -AllowChanged 可能来自命令行 "a,b" 或数组；统一拆开。
$AllowChanged = @($AllowChanged |
    ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrEmpty($_) })

if ($AcknowledgeFullRebuild) {
    $AcknowledgeReferenceChange = $true
}

$script:backupDir = Join-Path ([System.IO.Path]::GetTempPath()) ('sds-build-backup-' + [Guid]::NewGuid().ToString('N'))
$script:backupTimestamps = @{}

function Get-FileSha256([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        return 'MISSING'
    }
    return (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}

function Get-TextSha256([string]$text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($text)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '')
    }
    finally {
        $sha.Dispose()
    }
}

function Resolve-GameDir([string]$explicit) {
    if (-not [string]::IsNullOrEmpty($explicit)) {
        return [System.IO.Path]::GetFullPath($explicit)
    }
    if (-not [string]::IsNullOrEmpty($env:GameDir)) {
        return [System.IO.Path]::GetFullPath($env:GameDir)
    }
    $propsPath = Join-Path $srcDir 'Directory.Build.props'
    if (Test-Path -LiteralPath $propsPath) {
        [xml]$doc = Get-Content -LiteralPath $propsPath -Raw
        foreach ($pg in $doc.Project.PropertyGroup) {
            if ($pg.GameDir) {
                return [System.IO.Path]::GetFullPath([string]$pg.GameDir)
            }
        }
    }
    return $null
}

function Get-ModuleCsproj([string]$name) {
    return Join-Path (Join-Path $srcDir $name) ($name + '.csproj')
}

function Get-InternalsGrantees([string]$name) {
    $result = New-Object 'System.Collections.Generic.List[string]'
    $dir = Join-Path $srcDir $name
    if (-not (Test-Path -LiteralPath $dir)) {
        return $result.ToArray()
    }
    Get-ChildItem -LiteralPath $dir -Recurse -File -Filter *.cs | Where-Object {
        $_.FullName -notmatch '\\obj\\' -and $_.FullName -notmatch '\\bin\\'
    } | ForEach-Object {
        $text = Get-Content -LiteralPath $_.FullName -Raw
        foreach ($match in [regex]::Matches($text, 'InternalsVisibleTo\("([^"]+)"\)')) {
            $grantee = $match.Groups[1].Value
            if (-not $result.Contains($grantee)) {
                [void]$result.Add($grantee)
            }
        }
    }
    return $result.ToArray()
}

function Get-ProjectReferences([string]$csprojPath) {
    $refs = New-Object 'System.Collections.Generic.List[string]'
    if (-not (Test-Path -LiteralPath $csprojPath)) {
        return $refs.ToArray()
    }
    [xml]$doc = Get-Content -LiteralPath $csprojPath -Raw
    foreach ($pg in $doc.Project.ItemGroup) {
        foreach ($ref in $pg.ProjectReference) {
            if ($ref -ne $null -and $ref.Include) {
                $full = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $csprojPath) $ref.Include))
                $name = [System.IO.Path]::GetFileNameWithoutExtension($full)
                if (-not $refs.Contains($name)) {
                    [void]$refs.Add($name)
                }
            }
        }
    }
    return $refs.ToArray()
}

function Get-AllModules {
    $result = New-Object 'System.Collections.Generic.List[string]'
    Get-ChildItem -LiteralPath $srcDir -Directory | ForEach-Object {
        if ($_.Name -like '*.Tests') {
            return
        }
        $csproj = Join-Path $_.FullName ($_.Name + '.csproj')
        if (Test-Path -LiteralPath $csproj) {
            [void]$result.Add($_.Name)
        }
    }
    return $result.ToArray()
}

function Get-ModuleClosure([string]$root) {
    $result = New-Object 'System.Collections.Generic.List[string]'
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue($root)
    while ($pending.Count -gt 0) {
        $name = $pending.Dequeue()
        if ($result.Contains($name)) {
            continue
        }
        [void]$result.Add($name)
        foreach ($ref in (Get-ProjectReferences (Get-ModuleCsproj $name))) {
            if (-not $result.Contains($ref)) {
                $pending.Enqueue($ref)
            }
        }
    }
    return $result.ToArray()
}

function Get-BuildOrder([string[]]$modules) {
    $result = New-Object 'System.Collections.Generic.List[string]'
    $state = @{}
    foreach ($name in $modules) {
        $state[$name] = 0
    }
    $visit = $null
    $visit = {
        param([string]$name)
        if (-not $state.ContainsKey($name)) {
            return
        }
        if ($state[$name] -eq 2 -or $state[$name] -eq 1) {
            return
        }
        $state[$name] = 1
        foreach ($ref in (Get-ProjectReferences (Get-ModuleCsproj $name))) {
            if ($state.ContainsKey($ref)) {
                & $visit $ref
            }
        }
        $state[$name] = 2
        [void]$result.Add($name)
    }
    foreach ($name in $modules) {
        & $visit $name
    }
    return $result.ToArray()
}

function Get-ModuleSourceHash([string]$name) {
    $dir = Join-Path $srcDir $name
    $csproj = Get-ModuleCsproj $name
    $files = New-Object 'System.Collections.Generic.List[string]'
    if (Test-Path -LiteralPath $dir) {
        Get-ChildItem -LiteralPath $dir -Recurse -File | Where-Object {
            $_.FullName -notmatch '\\obj\\' -and
            $_.FullName -notmatch '\\bin\\' -and
            ($_.Extension -eq '.cs' -or $_.Extension -eq '.props' -or $_.Extension -eq '.targets')
        } | ForEach-Object {
            [void]$files.Add($_.FullName)
        }
    }
    if (Test-Path -LiteralPath $csproj) {
        [void]$files.Add($csproj)
    }
    $sorted = [string[]]$files.ToArray()
    [System.Array]::Sort($sorted, [System.StringComparer]::Ordinal)
    $lines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in $sorted) {
        $rel = $file.Substring($srcDir.Length).TrimStart('\', '/')
        $lines.Add($rel + '=' + (Get-FileSha256 $file))
    }
    return Get-TextSha256 ($lines -join "`n")
}

function Get-PublicSurfaceHash([string]$name) {
    $path = Join-Path $publicSurfaceDir ($name + '.public.txt')
    return Get-FileSha256 $path
}

function Get-ModuleStateSnapshot([string]$name, $sourceState) {
    return @{
        sourceHash = $sourceState.sourceHash
        publicSurfaceHash = $sourceState.publicSurfaceHash
        dllHash = Get-FileSha256 (Join-Path $asmDir ($name + '.dll'))
    }
}

function Get-GlobalState([string]$gameDir) {
    $lines = New-Object 'System.Collections.Generic.List[string]'
    $lines.Add('GameDir=' + $gameDir)
    foreach ($name in @('Assembly-CSharp.dll', 'UnityStubs.dll')) {
        $lines.Add($name + '=' + (Get-FileSha256 (Join-Path $gameDir $name)))
    }
    $harmonyPath = [System.IO.Path]::GetFullPath((Join-Path $srcDir '..\..\.mod\harmony\1.7\Assemblies\0Harmony.dll'))
    $lines.Add('0Harmony=' + (Get-FileSha256 $harmonyPath))
    foreach ($name in @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Build.user.props')) {
        $lines.Add($name + '=' + (Get-FileSha256 (Join-Path $srcDir $name)))
    }
    $dotnetVersion = (& dotnet --version) 2>$null
    $lines.Add('dotnet=' + $dotnetVersion)
    return @{
        hash = Get-TextSha256 ($lines -join "`n")
        gameDir = $gameDir
        lines = $lines.ToArray()
    }
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

function Backup-Assemblies {
    if (-not (Test-Path -LiteralPath $asmDir)) {
        return
    }
    New-Item -ItemType Directory -Path $script:backupDir -Force | Out-Null
    Get-ChildItem -LiteralPath $asmDir -Filter *.dll -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $script:backupDir $_.Name) -Force
        $script:backupTimestamps[$_.Name] = $_.LastWriteTime
    }
}

function Restore-Dll([string]$fileName) {
    $source = Join-Path $script:backupDir $fileName
    $target = Join-Path $asmDir $fileName
    if (-not (Test-Path -LiteralPath $source)) {
        return
    }
    Copy-Item -LiteralPath $source -Destination $target -Force
    if ($script:backupTimestamps.ContainsKey($fileName)) {
        (Get-Item -LiteralPath $target).LastWriteTime = $script:backupTimestamps[$fileName]
    }
}

function Restore-AllDlls($beforeKeys) {
    if (Test-Path -LiteralPath $script:backupDir) {
        Get-ChildItem -LiteralPath $script:backupDir -Filter *.dll -File | ForEach-Object {
            Restore-Dll $_.Name
        }
    }
    # 构建失败时，删除本次新建、构建前不存在的 DLL，避免留下半成品。
    if ($beforeKeys -ne $null -and (Test-Path -LiteralPath $asmDir)) {
        $current = Get-AssemblyHashes
        foreach ($name in $current.Keys) {
            if (-not $beforeKeys.ContainsKey($name)) {
                Remove-Item -LiteralPath (Join-Path $asmDir ($name + '.dll')) -Force
            }
        }
    }
}

function Cleanup-Backup {
    if (Test-Path -LiteralPath $script:backupDir) {
        Remove-Item -LiteralPath $script:backupDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}

function Read-BuildState {
    if (-not (Test-Path -LiteralPath $statePath)) {
        return $null
    }
    try {
        $raw = Get-Content -LiteralPath $statePath -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $null
        }
        return $raw | ConvertFrom-Json
    }
    catch {
        Write-Host ("[E11] 构建状态文件无法解析，将按无基线处理：" + $statePath)
        return $null
    }
}

function Write-BuildState($state) {
    $json = $state | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($statePath, $json, (New-Object System.Text.UTF8Encoding($false)))
}

function Get-PreviousModuleState($state, [string]$name) {
    if ($state -eq $null -or $state.modules -eq $null) {
        return $null
    }
    $property = $state.modules.PSObject.Properties[$name]
    if ($property -eq $null) {
        return $null
    }
    return $property.Value
}

function Get-TransitiveDependents([string[]]$roots, $dependents) {
    $result = New-Object 'System.Collections.Generic.List[string]'
    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    foreach ($root in $roots) {
        $pending.Enqueue($root)
    }
    while ($pending.Count -gt 0) {
        $name = $pending.Dequeue()
        $children = $dependents[$name]
        if ($children -eq $null) {
            continue
        }
        foreach ($child in $children) {
            if ($roots -contains $child -or $result.Contains($child)) {
                continue
            }
            [void]$result.Add($child)
            $pending.Enqueue($child)
        }
    }
    return $result.ToArray()
}

# ---------- 环境与引用态 ----------

$gameDir = Resolve-GameDir $GameDir
if ([string]::IsNullOrEmpty($gameDir)) {
    Write-Host "[E11] 无法确定 GameDir；请通过 -GameDir 或环境变量 GameDir 指定 RimWorld 目录。"
    exit 2
}
foreach ($required in @('Assembly-CSharp.dll', 'UnityStubs.dll')) {
    $path = Join-Path $gameDir $required
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host ("[E11] 引用程序集缺失：" + $path)
        Write-Host "[E11] 请修正 GameDir；不会在引用态不完整时构建。"
        exit 2
    }
}

$globalState = Get-GlobalState $gameDir
$closure = Get-ModuleClosure $Module
if (-not ($closure -contains $Module)) {
    Write-Host ("[E11] 找不到模块工程：" + (Get-ModuleCsproj $Module))
    exit 2
}

$moduleStates = @{}
foreach ($name in $closure) {
    $moduleStates[$name] = @{
        sourceHash = Get-ModuleSourceHash $name
        publicSurfaceHash = Get-PublicSurfaceHash $name
    }
}

$state = Read-BuildState

if ($InitializeState) {
    if ($DryRun) {
        Write-Host "[E11] DryRun：将初始化构建状态，但不写入。"
        exit 0
    }
    $newModules = @{}
    if ($state -ne $null -and $state.modules -ne $null) {
        foreach ($property in $state.modules.PSObject.Properties) {
            $newModules[$property.Name] = $property.Value
        }
    }
    foreach ($name in $closure) {
        $newModules[$name] = Get-ModuleStateSnapshot $name $moduleStates[$name]
        if (-not (Test-Path -LiteralPath (Join-Path $asmDir ($name + '.dll')))) {
            Write-Host ("[E11] 警告：" + $name + ".dll 不存在；基线可能不完整，建议先正常构建一次。")
        }
    }
    Write-BuildState @{
        version = 1
        globalHash = $globalState.hash
        gameDir = $gameDir
        modules = $newModules
    }
    Write-Host ("[E11] 已初始化构建状态：" + $statePath)
    Write-Host "[E11] 注意：-InitializeState 只记录当前源码与当前 DLL；若两者不一致，后续不会自动重编。请仅在已知构建成功后使用。"
    exit 0
}

if ($state -eq $null) {
    Write-Host "[E11] 未找到构建状态：" + $statePath
    Write-Host "[E11] 请先运行 -InitializeState 建立基线（不构建），再执行正常构建。"
    exit 1
}

$referenceChanged = ($state.globalHash -ne $globalState.hash)
if ($referenceChanged -and -not $AcknowledgeReferenceChange -and -not $ForceRebuild) {
    Write-Host "[E11] 引用态变化：GameDir / Directory.Build.props / SDK / 引用程序集与上次构建不同。"
    Write-Host "[E11] 源码未变的 DLL 不得因此更新；如确需全量重编，请加 -AcknowledgeReferenceChange。"
    Write-Host "[E11] 当前引用态："
    foreach ($line in $globalState.lines) {
        Write-Host ("  " + $line)
    }
    exit 1
}

# 源码未变时，DLL 必须与上次记录一致；不一致说明被外部修改或旧构建副作用重写。
if (-not $referenceChanged -and -not $ForceRebuild) {
    foreach ($name in $closure) {
        $previous = Get-PreviousModuleState $state $name
        if ($previous -eq $null -or $previous.dllHash -eq $null) {
            continue
        }
        if ($previous.sourceHash -ne $moduleStates[$name].sourceHash) {
            continue
        }
        $currentDll = Get-FileSha256 (Join-Path $asmDir ($name + '.dll'))
        if ($currentDll -eq 'MISSING') {
            continue
        }
        if ($previous.dllHash -ne $currentDll) {
            Write-Host ("[E11] 源码未变但 DLL 与构建状态不一致：" + $name)
            Write-Host "[E11] 可能被外部修改或旧构建副作用重写；请确认后用 -InitializeState 接受当前 artifact，或恢复 DLL。"
            exit 1
        }
    }
}

$dirty = New-Object 'System.Collections.Generic.List[string]'
if ($ForceRebuild) {
    foreach ($name in $closure) {
        [void]$dirty.Add($name)
    }
    Write-Host "[E11] -ForceRebuild：目标闭包全量重编。"
}
elseif ($referenceChanged) {
    foreach ($name in $closure) {
        [void]$dirty.Add($name)
    }
    Write-Host "[E11] 已确认引用态变化：目标闭包全量重编。"
}
else {
    foreach ($name in $closure) {
        $previous = Get-PreviousModuleState $state $name
        $dll = Join-Path $asmDir ($name + '.dll')
        if (-not (Test-Path -LiteralPath $dll)) {
            [void]$dirty.Add($name)
            Write-Host ("[E11] " + $name + ".dll 缺失：本次必须构建。")
            continue
        }
        if ($previous -eq $null -or $previous.sourceHash -ne $moduleStates[$name].sourceHash) {
            [void]$dirty.Add($name)
        }
    }
}

# ---------- 公开面 / 友元耦合涟漪 ----------

$ripple = New-Object 'System.Collections.Generic.List[string]'
if (-not $referenceChanged -and -not $ForceRebuild -and $dirty.Count -gt 0) {
    $publicChanged = New-Object 'System.Collections.Generic.List[string]'
    foreach ($name in $dirty) {
        $previous = Get-PreviousModuleState $state $name
        if ($previous -eq $null -or $previous.publicSurfaceHash -ne $moduleStates[$name].publicSurfaceHash) {
            [void]$publicChanged.Add($name)
        }
    }

    # InternalsVisibleTo：授权模块的源码变化会改变友元模块的编译面，按耦合处理。
    $friendChanged = New-Object 'System.Collections.Generic.List[string]'
    foreach ($name in $dirty) {
        foreach ($grantee in (Get-InternalsGrantees $name)) {
            if ($grantee -like '*.Tests' -or $grantee -eq $name -or $dirty -contains $grantee) {
                continue
            }
            if (-not $friendChanged.Contains($grantee)) {
                [void]$friendChanged.Add($grantee)
            }
        }
    }

    if ($publicChanged.Count -gt 0 -or $friendChanged.Count -gt 0) {
        $outside = @()
        if ($publicChanged.Count -gt 0) {
            $allModules = Get-AllModules
            $dependents = @{}
            foreach ($name in $allModules) {
                foreach ($ref in (Get-ProjectReferences (Get-ModuleCsproj $name))) {
                    if (-not $dependents.ContainsKey($ref)) {
                        $dependents[$ref] = @()
                    }
                    $dependents[$ref] += $name
                }
            }
            $allDependents = Get-TransitiveDependents $publicChanged.ToArray() $dependents
            foreach ($name in $allDependents) {
                if ($closure -notcontains $name) {
                    $outside += $name
                }
                elseif ($dirty -notcontains $name -and -not $ripple.Contains($name)) {
                    [void]$ripple.Add($name)
                }
            }
        }
        foreach ($grantee in $friendChanged) {
            if ($closure -notcontains $grantee) {
                $outside += $grantee
            }
            elseif (-not $ripple.Contains($grantee)) {
                [void]$ripple.Add($grantee)
            }
        }
        $outside = @($outside | Sort-Object -Unique)
        if ($outside.Count -gt 0) {
            Write-Host ("[E11] 公开面 / 友元耦合变化影响本次闭包外的模块：" + ($outside -join ', '))
            Write-Host "[E11] 请分别构建这些模块，或扩大本次构建范围后重跑。"
            exit 1
        }
        $unapproved = @()
        foreach ($name in $ripple) {
            if ($AllowChanged -notcontains $name) {
                $unapproved += $name
            }
        }
        if ($unapproved.Count -gt 0) {
            Write-Host ("[E11] 公开面 / 友元耦合变化导致以下模块必须重编（其源码未变）：" + ($ripple -join ', '))
            Write-Host ("[E11] 审阅公开面 diff / InternalsVisibleTo 后，显式加 -AllowChanged " + ($ripple -join ','))
            exit 1
        }
        foreach ($name in $ripple) {
            [void]$dirty.Add($name)
            Write-Host ("[E11] 依赖耦合涟漪：允许重编 " + $name)
        }
    }
}

if ($dirty.Count -eq 0) {
    if ($DryRun) {
        Write-Host ("[E11] 源码未变：本次无需构建（闭包 " + ($closure -join ', ') + "）。")
    }
    else {
        Write-Host ("[E11] 源码未变：跳过构建（闭包 " + ($closure -join ', ') + "）。")
    }
    exit 0
}

foreach ($name in $AllowChanged) {
    if ($closure -notcontains $name -and $dirty -notcontains $name) {
        Write-Host ("[E11] 警告：-AllowChanged 中的 " + $name + " 不在本次闭包/重编集合内，忽略。")
    }
}

if ($NoDependencies) {
    $dependencyDirty = @()
    foreach ($name in $dirty) {
        if ($name -ne $Module) {
            $dependencyDirty += $name
        }
    }
    if ($dependencyDirty.Count -gt 0) {
        Write-Host ("[E11] 注意：-NoDependencies 跳过源码已变化的依赖：" + ($dependencyDirty -join ', '))
    }
    if ($dirty -contains $Module) {
        $buildOrder = @($Module)
    }
    else {
        Write-Host "[E11] 目标模块源码未变，且 -NoDependencies 不构建依赖：本次无构建。"
        exit 0
    }
}
else {
    $buildOrder = Get-BuildOrder $dirty.ToArray()
}

Write-Host ("[E11] 本次构建：" + ($buildOrder -join ' -> '))
if ($DryRun) {
    Write-Host "[E11] DryRun：不构建、不写状态、不修改 Assemblies。"
    exit 0
}

# 公开面基线必须与当前源码一致；不一致时先人工审阅并更新基线（L2）。
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'DumpPublicSurface.ps1') -Check
if ($LASTEXITCODE -ne 0) {
    Write-Host "[E11] 公开面与基线不一致；先审阅并更新 PublicSurface 基线（L2），再构建。"
    exit 1
}

Backup-Assemblies
$before = Get-AssemblyHashes

try {
    foreach ($name in $buildOrder) {
        $csproj = Get-ModuleCsproj $name
        if (-not (Test-Path -LiteralPath $csproj)) {
            Write-Host ("[E11] 找不到模块工程：" + $csproj)
            Restore-AllDlls $before
            exit 2
        }
        $buildArgs = @(
            'build', $csproj, '-c', $Configuration, '-v', 'minimal',
            '--no-dependencies', ('-p:GameDir=' + $gameDir)
        )
        & dotnet @buildArgs
        if ($LASTEXITCODE -ne 0) {
            Restore-AllDlls $before
            exit $LASTEXITCODE
        }
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

    $unexpected = @()
    foreach ($name in $changed) {
        if ($dirty -notcontains $name -and $AllowChanged -notcontains $name) {
            $unexpected += $name
        }
    }
    if ($unexpected.Count -gt 0) {
        foreach ($name in $unexpected) {
            Restore-Dll ($name + '.dll')
        }
        Write-Host ("[E11] 源码未变且未批准的 DLL 被重写，已回滚：" + ($unexpected -join ', '))
        exit 1
    }

    foreach ($name in $buildOrder) {
        if ($changed -notcontains $name) {
            Write-Host ("[E11] 警告：" + $name + " 源码变化但 DLL 未变化（可能只改了注释/空白）。")
        }
    }

    $newModules = @{}
    if ($state.modules -ne $null) {
        foreach ($property in $state.modules.PSObject.Properties) {
            $newModules[$property.Name] = $property.Value
        }
    }
    foreach ($name in $closure) {
        $newModules[$name] = Get-ModuleStateSnapshot $name $moduleStates[$name]
    }
    Write-BuildState @{
        version = 1
        globalHash = $globalState.hash
        gameDir = $gameDir
        modules = $newModules
    }

    if ($changed.Count -eq 0) {
        Write-Host "[E11] 构建范围校验通过：无程序集变化（增量跳过）。"
    }
    else {
        Write-Host ("[E11] 构建范围校验通过：变化 = " + ($changed -join ', '))
    }
}
finally {
    Cleanup-Backup
}
exit 0

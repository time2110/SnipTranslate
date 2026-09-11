param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path $PSScriptRoot -Parent
$artifactRoot = Join-Path $repositoryRoot 'artifacts'
$publishDirectory = Join-Path $artifactRoot 'publish\win-x64'
$installerDirectory = Join-Path $artifactRoot 'installer'
$appProject = Join-Path $repositoryRoot 'src\SnipTranslate.App\SnipTranslate.App.csproj'
$workerProject = Join-Path $repositoryRoot 'src\SnipTranslate.OcrWorker\SnipTranslate.OcrWorker.csproj'
$installerScript = Join-Path $repositoryRoot 'installer\SnipTranslate.iss'
$nugetConfig = Join-Path $repositoryRoot 'NuGet.Config'
$modelsDirectory = Join-Path $repositoryRoot 'src\SnipTranslate.OcrWorker\models'
$localDotnet = Join-Path $repositoryRoot '.dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localDotnet) { $localDotnet } else { 'dotnet' }

$env:DOTNET_CLI_HOME = Join-Path $repositoryRoot '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $repositoryRoot '.nuget\packages'
$env:APPDATA = Join-Path $repositoryRoot '.appdata'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

function Assert-Success([string]$step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$step 失败，退出代码：$LASTEXITCODE"
    }
}

function Find-InnoCompiler {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $command.Source
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe')
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe')
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe')
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }
    return $candidates | Select-Object -First 1
}

function Install-InnoSetup {
    Write-Host '未检测到 Inno Setup，正在从官网安装到当前用户…' -ForegroundColor Yellow
    $temporaryInstaller = Join-Path ([System.IO.Path]::GetTempPath()) 'SnipTranslate-InnoSetup.exe'
    Invoke-WebRequest `
        -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' `
        -OutFile $temporaryInstaller
    $signature = Get-AuthenticodeSignature -LiteralPath $temporaryInstaller
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
        throw "Inno Setup 安装程序数字签名验证失败：$($signature.Status)"
    }

    $process = Start-Process -FilePath $temporaryInstaller `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER' `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup 安装失败，退出代码：$($process.ExitCode)"
    }
}

$modelFiles = Get-ChildItem -LiteralPath $modelsDirectory -File -ErrorAction SilentlyContinue
if (-not $modelFiles) {
    Write-Host '未找到 OCR 模型，正在下载…' -ForegroundColor Yellow
    & (Join-Path $PSScriptRoot 'Get-RapidOcrModels.ps1')
    Assert-Success '下载 OCR 模型'
}

New-Item -ItemType Directory -Force -Path $artifactRoot, $installerDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $env:APPDATA 'NuGet') | Out-Null
Copy-Item -LiteralPath $nugetConfig -Destination (Join-Path $env:APPDATA 'NuGet\NuGet.Config') -Force
$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($resolvedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw '发布目录超出 artifacts，已停止清理。'
}
if (Test-Path -LiteralPath $resolvedPublishDirectory) {
    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedPublishDirectory | Out-Null

Write-Host '正在还原主程序和 OCR Worker 依赖…' -ForegroundColor Cyan
& $dotnet restore $appProject --runtime win-x64 --configfile $nugetConfig
Assert-Success '还原主程序依赖'
& $dotnet restore $workerProject --runtime win-x64 --configfile $nugetConfig
Assert-Success '还原 OCR Worker 依赖'

Write-Host '正在发布主程序（Windows x64，自包含）…' -ForegroundColor Cyan
& $dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $resolvedPublishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
Assert-Success '发布主程序'

$workerPublishDirectory = Join-Path $resolvedPublishDirectory 'ocr'
Write-Host '正在发布 OCR Worker 和模型…' -ForegroundColor Cyan
& $dotnet publish $workerProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $workerPublishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
Assert-Success '发布 OCR Worker'

$iscc = Find-InnoCompiler
if (-not $iscc) {
    Install-InnoSetup
    $iscc = Find-InnoCompiler
}
if (-not $iscc) {
    throw 'Inno Setup 已安装但找不到 ISCC.exe，请重新打开终端后再运行本命令。'
}

Write-Host '正在生成安装包…' -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DPublishDir=$resolvedPublishDirectory" "/DOutputDir=$installerDirectory" $installerScript
Assert-Success '生成安装包'

$installer = Get-Item -LiteralPath (Join-Path $installerDirectory "SnipTranslate-Setup-$Version-win-x64.exe")
Write-Host ''
Write-Host '安装包生成成功：' -ForegroundColor Green
Write-Host $installer.FullName
Write-Host ("大小：{0:N1} MB" -f ($installer.Length / 1MB))

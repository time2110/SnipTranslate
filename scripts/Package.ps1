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
        throw "$step failed with exit code $LASTEXITCODE"
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
    Write-Host 'Inno Setup was not found. Installing it for the current user...' -ForegroundColor Yellow
    $temporaryInstaller = Join-Path ([System.IO.Path]::GetTempPath()) 'SnipTranslate-InnoSetup.exe'
    Invoke-WebRequest `
        -Uri 'https://github.com/jrsoftware/issrc/releases/download/is-7_1_0/innosetup-7.1.0-x64.exe' `
        -OutFile $temporaryInstaller
    $signature = Get-AuthenticodeSignature -LiteralPath $temporaryInstaller
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'Pyrsys B\.V\.') {
        throw "Inno Setup signature verification failed: $($signature.Status)"
    }

    $process = Start-Process -FilePath $temporaryInstaller `
        -ArgumentList '/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/CURRENTUSER' `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Inno Setup installation failed with exit code $($process.ExitCode)"
    }
}

Write-Host 'Checking OCR models...' -ForegroundColor Cyan
& (Join-Path $PSScriptRoot 'Get-RapidOcrModels.ps1')

New-Item -ItemType Directory -Force -Path $artifactRoot, $installerDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $env:APPDATA 'NuGet') | Out-Null
Copy-Item -LiteralPath $nugetConfig -Destination (Join-Path $env:APPDATA 'NuGet\NuGet.Config') -Force
$resolvedArtifactRoot = [System.IO.Path]::GetFullPath($artifactRoot)
$resolvedPublishDirectory = [System.IO.Path]::GetFullPath($publishDirectory)
if (-not $resolvedPublishDirectory.StartsWith($resolvedArtifactRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The publish directory is outside artifacts. Cleanup was stopped.'
}
if (Test-Path -LiteralPath $resolvedPublishDirectory) {
    Remove-Item -LiteralPath $resolvedPublishDirectory -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedPublishDirectory | Out-Null

Write-Host 'Restoring app and OCR Worker dependencies...' -ForegroundColor Cyan
& $dotnet restore $appProject --runtime win-x64 --configfile $nugetConfig
Assert-Success 'App dependency restore'
& $dotnet restore $workerProject --runtime win-x64 --configfile $nugetConfig
Assert-Success 'OCR Worker dependency restore'

Write-Host 'Publishing the Windows x64 self-contained app...' -ForegroundColor Cyan
& $dotnet publish $appProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $resolvedPublishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
Assert-Success 'App publish'

$workerPublishDirectory = Join-Path $resolvedPublishDirectory 'ocr'
Write-Host 'Publishing OCR Worker and models...' -ForegroundColor Cyan
& $dotnet publish $workerProject `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $workerPublishDirectory `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false
Assert-Success 'OCR Worker publish'

$iscc = Find-InnoCompiler
if (-not $iscc) {
    Install-InnoSetup
    $iscc = Find-InnoCompiler
}
if (-not $iscc) {
    throw 'Inno Setup is installed but ISCC.exe was not found. Reopen the terminal and retry.'
}

Write-Host 'Building the installer...' -ForegroundColor Cyan
& $iscc "/DAppVersion=$Version" "/DPublishDir=$resolvedPublishDirectory" "/DOutputDir=$installerDirectory" $installerScript
Assert-Success 'Installer build'

$installer = Get-Item -LiteralPath (Join-Path $installerDirectory "SnipTranslate-Setup-$Version-win-x64.exe")
Write-Host ''
Write-Host 'Installer created successfully:' -ForegroundColor Green
Write-Host $installer.FullName
Write-Host ("Size: {0:N1} MB" -f ($installer.Length / 1MB))

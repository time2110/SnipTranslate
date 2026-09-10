param(
    [string]$Destination = (Join-Path $PSScriptRoot '..\src\SnipTranslate.OcrWorker\models')
)

$ErrorActionPreference = 'Stop'

$models = @(
    @{
        Name = 'ch_PP-OCRv5_mobile_det.onnx'
        Sha256 = '4D97C44A20D30A81AAD087D6A396B08F786C4635742AFC391F6621F5C6AE78AE'
    },
    @{
        Name = 'ch_PP-OCRv5_rec_mobile_infer.onnx'
        Sha256 = '5825FC7EBF84AE7A412BE049820B4D86D77620F204A041697B0494669B1742C5'
    },
    @{
        Name = 'ch_ppocr_mobile_v2.0_cls_infer.onnx'
        Sha256 = 'E47ACEDF663230F8863FF1AB0E64DD2D82B838FCEB5957146DAB185A89D6215C'
    },
    @{
        Name = 'ppocrv5_dict.txt'
        Sha256 = 'D1979E9F794C464C0D2E0B70A7FE14DD978E9DC644C0E71F14158CDF8342AF1B'
    }
)

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

foreach ($model in $models) {
    $target = Join-Path $Destination $model.Name
    if (Test-Path -LiteralPath $target) {
        $existingHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($existingHash -eq $model.Sha256) {
            Write-Host "已存在并通过校验: $($model.Name)"
            continue
        }
    }

    $url = "https://raw.githubusercontent.com/RapidAI/RapidOCRCSharp/main/RapidOCRConsole/models/$($model.Name)"
    Write-Host "正在下载: $($model.Name)"
    Invoke-WebRequest -Uri $url -OutFile $target

    $downloadedHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($downloadedHash -ne $model.Sha256) {
        throw "模型校验失败: $($model.Name)"
    }
}

Write-Host 'RapidOCR 模型准备完成。'


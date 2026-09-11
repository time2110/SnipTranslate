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
    },
    @{
        Name = 'en_PP-OCRv5_rec_mobile.onnx'
        Url = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/rec/en_PP-OCRv5_rec_mobile.onnx'
        Sha256 = 'C3461ADD59BB4323ECBA96A492AB75E06DDA42467C9E3D0C18DB5D1D21924BE8'
    },
    @{
        Name = 'ppocrv5_en_dict.txt'
        Url = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv5/rec/en_PP-OCRv5_rec_mobile/ppocrv5_en_dict.txt'
        Sha256 = 'E025A66D31F327BA0C232E03F407AE8D105E1E709E7CCB3F408AA778C24E70D6'
    },
    @{
        Name = 'korean_PP-OCRv5_rec_mobile.onnx'
        Url = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/onnx/PP-OCRv5/rec/korean_PP-OCRv5_rec_mobile.onnx'
        Sha256 = 'CD6E2EA50F6943CA7271EB8C56A877A5A90720B7047FE9C41A2E541A25773C9B'
    },
    @{
        Name = 'ppocrv5_korean_dict.txt'
        Url = 'https://www.modelscope.cn/models/RapidAI/RapidOCR/resolve/v3.9.2/paddle/PP-OCRv5/rec/korean_PP-OCRv5_rec_mobile/ppocrv5_korean_dict.txt'
        Sha256 = 'A88071C68C01707489BAA79EBE0405B7BEB5CCA229F4FC94CC3EF992328802D7'
    }
)

New-Item -ItemType Directory -Force -Path $Destination | Out-Null

foreach ($model in $models) {
    $target = Join-Path $Destination $model.Name
    if (Test-Path -LiteralPath $target) {
        $existingHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
        if ($existingHash -eq $model.Sha256) {
            Write-Host "Already present and verified: $($model.Name)"
            continue
        }
    }

    $url = if ($model.Url) { $model.Url } else { "https://raw.githubusercontent.com/RapidAI/RapidOCRCSharp/main/RapidOCRConsole/models/$($model.Name)" }
    Write-Host "Downloading: $($model.Name)"
    Invoke-WebRequest -Uri $url -OutFile $target

    $downloadedHash = (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash
    if ($downloadedHash -ne $model.Sha256) {
        throw "Model checksum verification failed: $($model.Name)"
    }
}

Write-Host 'RapidOCR models are ready.'

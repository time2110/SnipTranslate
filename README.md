# SnipTranslate

Windows 原生的极速截图、贴图、OCR 与翻译工具。

当前开发阶段：可运行的截图 + 贴图 + RapidOCR + Google 翻译 MVP。

## 默认快捷键

- `F1`：截图
- `Ctrl+F1`：截图翻译模式
- `Enter`：复制选区
- `Space`：贴图
- `Ctrl+S`：保存
- `Esc`：取消
- `O`：识别当前选区
- `T`：识别并翻译当前选区

## 已实现

- 多显示器冻结与自由框选
- 8 个选区控制点、尺寸标签、像素放大镜、HEX 颜色
- 无需点击的悬停放大镜；Shift 切换 HEX/RGB，C 复制当前显示的颜色值
- 双击/Enter 复制，Ctrl+S 保存，Space 贴图
- 画笔、矩形、箭头和文字标注，支持颜色、粗细、字号与粗体
- 工具选中高亮，并切换为相应操作光标
- 独立 RapidOCR Worker，10 分钟空闲自动释放
- PP-OCRv5 mobile 本地中英文 OCR
- 基于识别框坐标的行序与段落整理
- 截图选区旁内嵌 OCR/翻译结果卡片，不切换窗口
- 默认自动识别中文/英文并互译，也可手动选择中、英、日、韩语言
- Google Web 翻译，支持直接复制原文或译文
- 系统代理、直连、自定义 HTTP/HTTPS/SOCKS5 代理
- 代理密码使用 Windows DPAPI 加密

## 构建

首次构建先下载 RapidOCR 模型：

```powershell
.\scripts\Get-RapidOcrModels.ps1
.\scripts\Build.ps1
```

运行：

```powershell
.\src\SnipTranslate.App\bin\Debug\net10.0-windows\SnipTranslate.exe
```

托盘右键打开设置，可选择 Google 翻译目标语言和代理。Google Web 是非正式接口，可能发生限流或协议变化。

## 生成 Windows 安装包

一条命令生成自包含的 Windows x64 安装程序：

```powershell
.\scripts\Package.ps1
```

详细说明见 [安装包制作与使用](docs/安装包制作与使用.md)。

模型来源：[RapidAI/RapidOCRCSharp](https://github.com/RapidAI/RapidOCRCSharp)，许可证见上游项目。

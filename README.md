# SnipTranslate

Windows 原生的极速截图、贴图、OCR 与翻译工具。

当前开发阶段：可运行的截图 + 贴图 + RapidOCR + 多服务翻译 MVP。

## 默认快捷键

- `F1`：截图（可在设置中修改）
- `Ctrl+F1`：截图翻译模式（可在设置中修改）
- `Enter`：复制选区
- `Space`：贴图
- `Ctrl+S`：保存
- `O`：识别当前选区
- `T`：识别并翻译当前选区
- `Ctrl+R`：在当前选区上增强重新识别
- `Esc`：立即退出整套截图/OCR/翻译界面

## 已实现

- 多显示器冻结与自由框选
- 鼠标悬停自动识别控件；滚轮切换控件/窗口/屏幕，按住 Alt 临时关闭吸附，拖动自由框选
- 8 个选区控制点、尺寸标签、像素放大镜、HEX 颜色
- 无需点击的悬停放大镜；Shift 切换 HEX/RGB，C 复制当前显示的颜色值
- 双击/Enter 复制，Ctrl+S 保存，Space 贴图
- 画笔、矩形、箭头和文字标注，支持颜色、粗细、字号与粗体
- 工具选中高亮，并切换为相应操作光标
- 独立 RapidOCR Worker，10 分钟空闲自动释放
- PP-OCRv5 本地中英日 OCR、英文优先模型与韩文 OCR；支持 Mobile 快速模式、Server 准确模式和旋转文字识别
- 英文小字图自动放大、灰度对比度增强；自动语言模式会用英文专用模型复核并按置信度择优
- 基于识别框坐标的行序与段落整理
- 截图选区旁内嵌 OCR/翻译结果卡片，不切换窗口
- 默认自动识别中文/英文并互译，也可手动选择中、英、日、韩语言
- Google Web、微软/Bing Translator、LibreTranslate、MyMemory 和自定义 HTTP API
- 翻译 API 可新增、独立编辑、启用/停用、删除和拖动排序；测试按钮只测试当前所选接口
- 失败时按顺序自动切换；429、鉴权失败或连续故障会暂时熔断失效接口
- 支持直接复制原文或译文；默认按中英文自动互译
- 系统代理、直连、自定义 HTTP/HTTPS/SOCKS5 代理
- 设置中可随时开启或关闭当前用户的开机自启动
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

托盘右键打开设置，可修改全局快捷键、默认 OCR 语言、翻译服务、目标语言和代理。Google Web 与 MyMemory 无需 Key；微软/Bing 使用 Azure Translator Key；LibreTranslate 支持公共或自建实例。Google Web 是非正式接口，可能发生限流或协议变化。

自定义 HTTP API 使用 `POST application/json`。请求模板支持 `{text}`、`{source}`、`{target}` 占位符，响应路径支持点号与数组下标，例如 `data.translatedText` 或 `0.translations.0.text`。API Key 可配置到任意请求头。

## 生成 Windows 安装包

一条命令生成自包含的 Windows x64 安装程序：

```powershell
.\scripts\Package.cmd
```

也可显式地为当前进程绕过 PowerShell 执行策略：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

详细说明见 [安装包制作与使用](docs/安装包制作与使用.md)。

开发过程中的 Windows、OCR、翻译与打包问题见 [技术难点与兼容性问题复盘](docs/技术难点与兼容性问题.md)。

模型来源：[RapidAI/RapidOCRCSharp](https://github.com/RapidAI/RapidOCRCSharp)，许可证见上游项目。

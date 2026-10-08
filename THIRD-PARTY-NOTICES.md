# 来源与第三方声明

## Tendedero

- 作者：Alejandro Buján
- 源码：https://github.com/alejandrobujan/tendedero
- 对照提交：`d991454`
- 范围：截图集合、绳线布局、指针展开/收起、点击/长按/拖放意图、生命周期及动画概念。Windows C# 实现重新编写；未使用 Swift 工程、私有 macOS API、上游名称、图标或 docs 图片作为品牌资产。
- 具体对应与主动改变的文件语义见 `docs/SOURCE-MAP.md`。

原始许可保留如下：

```text
MIT License

Copyright (c) 2026 Alejandro Buján

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.

---

Name and icon

The MIT License above covers the source code. It does not grant any rights
to the Tendedero name or the Tendedero icon, including the icon artwork drawn
by scripts/make-icon.swift and the images in the docs folder.

You may build and use the app as published, and you may refer to Tendedero
to say where your work comes from. Modified or redistributed versions must
use a different name and a different icon, and must not suggest that they
are Tendedero or are endorsed by its author.
```

## Microsoft .NET / WPF

应用仅依赖平台 SDK 与运行时，没有其他应用运行时 NuGet 包。发布脚本读取包内运行时版本，从同版本官方 NuGet 运行时包复制许可与第三方声明到 `licenses`。验证脚本要求这些文件存在，缺失时发布失败。

- `licenses/dotnet-runtime-license.txt`：Microsoft.NETCore.App.Runtime.win-x64 的 MIT 许可。
- `licenses/dotnet-runtime-third-party-notices.txt`：同版本运行时第三方声明。
- `licenses/windows-desktop-license.txt`：Microsoft.WindowsDesktop.App.Runtime.win-x64 的许可。

.NET、WPF 来源分别为 https://github.com/dotnet/runtime 和 https://github.com/dotnet/wpf 。

## Shotline 原创资产

`src/Shotline/Assets/Shotline.svg`、`Shotline.ico` 与 `scripts/make-icon.py` 为本项目原创绘制，不由上游图标衍生，按本项目 MIT 许可发布。界面使用系统提供的中文字体；不分发字体文件。

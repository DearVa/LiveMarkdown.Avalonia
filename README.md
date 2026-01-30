[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/DearVa/LiveMarkdown.Avalonia)
[![netstandard2.0](https://img.shields.io/badge/netstandard-2.0-blue.svg)](https://docs.microsoft.com/en-us/dotnet/standard/net-standard)
[![Avalonia](https://img.shields.io/badge/Avalonia-11-blue.svg)](https://avaloniaui.net/)
[![License](https://img.shields.io/badge/License-Apache%202.0-blue.svg)](LICENSE)
[![GitHub issues](https://img.shields.io/github/issues/DearVa/LiveMarkdown.Avalonia.svg)](https://github.com/DearVa/LiveMarkdown.Avalonia/issues)
[![NuGet](https://img.shields.io/nuget/v/LiveMarkdown.Avalonia.svg)](https://www.nuget.org/packages/LiveMarkdown.Avalonia/)

![demo.gif](https://raw.githubusercontent.com/DearVa/LiveMarkdown.Avalonia/main/img/demo.gif)

## 👋 Introduction

`LiveMarkdown.Avalonia` is a High-performance Markdown viewer for Avalonia applications.
It supports **real-time rendering** of Markdown content, so it's ideal for applications that require dynamic text updating, **especially when streaming large model outputs**.

## ⭐ Features

- 🚀 **High-performance rendering powered by [Markdig](https://github.com/xoofx/markdig)**
- 🔄 **Real-time updates**: Automatically re-renders changes in Markdown content
- 🎨 **Customizable styles**: Easily style Markdown elements using Avalonia's powerful styling system
- 🔗 **Link support**: Clickable links with customizable behavior
- 📊 **Table support**: Render tables with proper formatting
- 📜 **Code block syntax highlighting**: Supports multiple languages with [TextMateSharp](https://github.com/danipen/TextMateSharp)
- 🖼️ **Image support**: Load online, local even `avares` images asynchronously
- ✍️ **Selectable text**: Text can be selected across different Markdown elements

> [!NOTE]
> This library currently only supports `Append` and `Clear` operations on the Markdown content, which is enough for LLM streaming scenarios.

> [!WARNING]
> Known issue: Avalonia 11.3.5 and 11.3.6 changed text layout behavior, which may cause some text offset issues in certain scenarios. e.g. code inline has extra bottom margin, wried italic font rendering, etc.
> 
> Please use 11.3.0 ~ 11.3.4 or >= 11.3.7 to avoid this problem.

## ❤️ Sponsor

This project is fully open-source and free. Your support will improve this project a lot. I sincerely thank all my sponsors!

<a href="https://www.buymeacoffee.com/artemisli"><img src="https://img.buymeacoffee.com/button-api/?text=Support%20%20Me&emoji=&slug=artemisli&button_colour=FFDD00&font_colour=000000&font_family=Comic&outline_colour=000000&coffee_colour=ffffff" alt="Buy me a coffee"/></a>
<a href="https://afdian.com/a/DearVa"><img width="200" src="https://pic1.afdiancdn.com/static/img/welcome/button-sponsorme.png" alt="爱发电"></a>
<a href="https://app.fossa.com/projects/git%2Bgithub.com%2FDearVa%2FLiveMarkdown.Avalonia?ref=badge_shield" alt="FOSSA Status"><img src="https://app.fossa.com/api/projects/git%2Bgithub.com%2FDearVa%2FLiveMarkdown.Avalonia.svg?type=shield"/></a>

## ✈️ Roadmap

- [x] Basic Markdown rendering
- [x] Real-time updates
- [x] Link support
- [x] Table support
- [x] Code block syntax highlighting
- [x] Image support
  - [x] Bitmap
  - [x] SVG
  - [x] Online images
  - [x] Local images
  - [x] `avares` images
- [x] Selectable text across elements
- [x] LaTeX support
- [ ] HTML rendering

## 🚀 Getting Started

### 1. Install the NuGet package

You can install the latest version from NuGet CLI:

```bash
dotnet add package LiveMarkdown.Avalonia
```

or use the NuGet Package Manager in your IDE.

### 2. Register the Markdown styles in your Avalonia application

```xml
<Application
  x:Class="YourAppClass" xmlns="https://github.com/avaloniaui"
  xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" RequestedThemeVariant="Default">

  <Application.Styles>
    <!-- Your other styles here -->
    <StyleInclude Source="avares://LiveMarkdown.Avalonia/Styles.axaml"/>
  </Application.Styles>

  <Application.Resources>
    <!-- Your other resources here -->
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ResourceInclude Source="avares://LiveMarkdown.Avalonia/Defaults.axaml"/>
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

### 3. Use the `MarkdownRenderer` control in your XAML

Add the `MarkdownRenderer` control to your `.axaml` file:
```xml
<YourControl
  xmlns:md="clr-namespace:LiveMarkdown.Avalonia;assembly=LiveMarkdown.Avalonia">
  <md:MarkdownRenderer x:Name="MarkdownRenderer"/>
</YourControl>
```

Then you can manage the Markdown content in your code-behind:

```csharp
// ObservableStringBuilder is used for efficient string updates
var markdownBuilder = new ObservableStringBuilder();
MarkdownRenderer.MarkdownBuilder = markdownBuilder;

// Append Markdown content, this will trigger re-rendering
markdownBuilder.Append("# Hello, Markdown!");
markdownBuilder.Append("\n\nThis is a **live** Markdown viewer for Avalonia applications.");

// Clear the content
markdownBuilder.Clear();
```

If you want to load local images with relative paths, you can set the `MarkdownRenderer.ImageBasePath` property.

### 4. Enable LaTeX rendering

LaTeX is supported via the `LiveMarkdown.Avalonia.Math` package. You can install it via NuGet:

```bash
dotnet add package LiveMarkdown.Avalonia.Math
```

Then register both the `MathInlineNode` and `MathBlockNode` before using LaTeX in your Markdown content:

```csharp
using LiveMarkdown.Avalonia;

MarkdownNode.Register<MathInlineNode>();
MarkdownNode.Register<MathBlockNode>();
```

## 🪄 Style Customization

Markdown elements can be styled using Avalonia's powerful styling system. You can override the [default styles](https://github.com/DearVa/LiveMarkdown.Avalonia/blob/main/src/LiveMarkdown.Avalonia/Styles.axaml) by defining your own styles in your application styles.

Avalonia Styling Docs: 
- [Avalonia Styles](https://docs.avaloniaui.net/docs/styling)
- [Style selector syntax](https://docs.avaloniaui.net/docs/reference/styles/style-selector-syntax)

### Customizing Resources

The `<ResourceInclude Source="avares://LiveMarkdown.Avalonia/Defaults.axaml"/>` line in your `App.axaml` imports the default resources used by the renderer. You can override these resources in your application to customize the look and feel.

Here are the available resource keys:

| Key | Type | Description |
| --- | --- | --- |
| `BorderColor` | `Color` | Color of borders (e.g., code blocks, tables) |
| `ForegroundColor` | `Color` | Default text color |
| `CardBackgroundColor` | `Color` | Background color for tables |
| `SecondaryCardBackgroundColor` | `Color` | Background color for code blocks and quotes |
| `CodeInlineColor` | `Color` | Text color for inline code |
| `QuoteBorderColor` | `Color` | Border color for blockquotes |
| `FontSizeS` | `Double` | Small font size (not used yet) |
| `FontSizeM` | `Double` | Medium font size (default text size) |
| `FontSizeL` | `Double` | Large font size for Heading4, Heading5 and Heading6 |
| `FontSizeXl` | `Double` | Extra large font size for Heading3 |
| `FontSize2Xl` | `Double` | 2XL font size for Heading2 |
| `FontSize3Xl` | `Double` | 3XL font size for Heading1 |

### Code Block Theme

You can customize the syntax highlighting theme for code blocks. The default theme is `DarkPlus`.

#### Global Setting

To set the theme globally for a `MarkdownRenderer` instance, use the `CodeBlockColorTheme` property:

```xml
<md:MarkdownRenderer CodeBlockColorTheme="LightPlus" />
```

#### Per-CodeBlock Setting via Styles

You can also use Avalonia styles to set the theme for specific code blocks or based on conditions:

```xml
<Style Selector="md|CodeBlock">
    <Setter Property="ColorTheme" Value="SolarizedDark"/>
</Style>
```

Supported themes are defined in `TextMateSharp.Grammars.ThemeName`.

## 🤔 FAQ

- Q: Why some emojis not rendered correctly (rendered in single color)?
- A: This is a known issue caused by Skia (the render backend of Avalonia). You can upgrade SkiaSharp version (e.g. >= 3.117.0) to fix this. [Related issue](https://github.com/AvaloniaUI/Avalonia/issues/18677)

## 🤝 Contributing

We welcome issues, feature ideas, and PRs! See [CONTRIBUTING.md](CONTRIBUTING.md) for guidelines.

## 📄 License

Distributed under the Apache 2.0 License. See [LICENSE](LICENSE) for more information.


[![FOSSA Status](https://app.fossa.com/api/projects/git%2Bgithub.com%2FDearVa%2FLiveMarkdown.Avalonia.svg?type=large)](https://app.fossa.com/projects/git%2Bgithub.com%2FDearVa%2FLiveMarkdown.Avalonia?ref=badge_large)

### Third-Party Licenses

- **markdig** - [BSD-2-Clause License](https://github.com/xoofx/markdig/blob/master/license.txt)
    - Markdown parser for Everywhere.Markdown rendering
    - Source repo: https://github.com/xoofx/markdig
- **Svg.Skia** - [MIT License](https://github.com/wieslawsoltes/Svg.Skia/blob/master/LICENSE.TXT)
    - Svg rendering for images
    - Source repo: https://github.com/wieslawsoltes/Svg.Skia
- **TextMateSharp** - [MIT License](https://github.com/danipen/TextMateSharp/blob/master/LICENSE.md)
    - Syntax highlighting for code blocks
    - Source repo: https://github.com/danipen/TextMateSharp
- **CSharpMath** - [MIT License](https://github.com/verybadcat/CSharpMath/blob/master/License)
    - LaTeX rendering support
    - Source repo: https://github.com/verybadcat/CSharpMath
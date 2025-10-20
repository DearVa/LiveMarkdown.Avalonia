# Markdown Render Test

---

Playwright有两层含义：

1. 剧作家：在传统意义上，playwright指“剧作家”，即编写戏剧、话剧剧本的人。比如莎士比亚就是著名的英国剧作家（playwright）。

2. Web 测试框架：在技术领域，Playwright 是一个流行的开源端到端测试（E2E）和自动化框架。它可以在所有主流浏览器（如 Chromium、Firefox 和 WebKit）上运行自动化测试，主要用于现代 Web 应用的功能测试和界面测试。Playwright 支持多种语言（JavaScript、TypeScript、Python、C# 等），能够进行高效、可靠的自动化操作，比如页面点击、表单填写、截图、网络拦截等。

---

Playwright 框架的主要特点有：
- 跨浏览器支持（Chromium、Firefox、WebKit）
- 支持移动设备模拟
- 极强的测试隔离和并行执行能力
- 自动等待、自动重试，提升测试的稳定性
- 提供丰富的调试工具和报告功能

总结：Playwright可以是“剧作家”，在 IT 领域内更多指的是现代 Web 自动化测试的专业工具。

---

## 1. List

- Item One
- Item Two
  - Subitem **2.1**
  - Test inline [Subitem](https://example.com) link 2.2
- Item Three
- Test long long long long long long long long long long long long long long long long long long text wrapping

1. Item One
2. Item Two
   1. Subitem **2.1**
   2. Test inline [Subitem](https://example.com) link 2.2
3. Item Three
4. Test long long long long long long long long long long long long long long long long long long text wrapping

Test **bold** and *italic* text, then `inline code` example and [`inline code with backticks`](https://example.com) are here.

---

## 2. Code Example

```csharp
public class HelloWorld
{
    public static void Main()
    {
        Console.WriteLine("Hello, Markdown!");
        Console.WriteLine("Test long long long long long long long long long long long long long long long long long long text wrapping");
    }
}
```

```yaml
name: Build and Release

on:
  push:
    tags:
      - 'v*.*.*'

jobs:
  build-windows:
    runs-on: windows-latest

    steps:
    - name: Enable long paths in git
      run: git config --global core.longpaths true

    - name: Checkout code
      uses: actions/checkout@v4
      with:
        fetch-depth: 0
        submodules: true

    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: 9.0.x

    - name: Setup MSBuild
      uses: microsoft/setup-msbuild@v2

    - name: Install NSIS
      uses: repolevedavaj/install-nsis@v1.0.1
      with:
        nsis-version: '3.11'

    - name: Get version from tag
      id: get_version
      shell: pwsh
      run: |
        $version = $env:GITHUB_REF -replace 'refs/tags/v', ''
        echo "VERSION=$version" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append

    - name: Get tag description
      id: get_tag_description
      shell: pwsh
      env:
        GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
      run: |
        $tag = $env:GITHUB_REF -replace 'refs/tags/', ''
        $url = "https://api.github.com/repos/${{ github.repository }}/git/ref/tags/$tag"
        $header = @{ Authorization = "token $env:GITHUB_TOKEN" }
        $tagInfo = Invoke-RestMethod -Uri $url -Headers $header
        $objectUrl = $tagInfo.object.url
        $tagObject = Invoke-RestMethod -Uri $objectUrl -Headers $header
        $message = $tagObject.message
        echo "TAG_MESSAGE=$message" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append

    - name: Publish Everywhere.Windows
      shell: pwsh
      run: |
        dotnet publish src/Everywhere.Windows/Everywhere.Windows.csproj -c Release --configfile nuget.config --self-contained true -p:AssemblyVersion=$env:VERSION -p:FileVersion=$env:VERSION -o ./publish

    - name: Build NSIS installer
      shell: pwsh
      run: |
        makensis "/DVERSION=${{env.VERSION}}" "tools/installer.nsi"
        $exePath = "Everywhere-Windows-x64-Setup-v${{env.VERSION}}.exe"
        echo "EXE_PATH=./${exePath}" | Out-File -FilePath $env:GITHUB_ENV -Encoding utf8 -Append

    - name: Create release zip
      shell: pwsh
      run: |
        Compress-Archive -Path ./publish/* -DestinationPath ./Everywhere-Windows-x64-v$env:VERSION.zip

    - name: Create GitHub Release
      id: create_release
      uses: softprops/action-gh-release@v2
      with:
        name: Everywhere v${{env.VERSION}}
        draft: false
        prerelease: false
        body: ${{env.TAG_MESSAGE}}
        files: |
          ./Everywhere-Windows-x64-v${{env.VERSION}}.zip
          ./Everywhere-Windows-x64-Setup-v${{env.VERSION}}.exe
        generate_release_notes: true
```

---

## 3. Table

| Name  | Age | City     |
|-------|-----|----------|
| Alice | 24  | London   |
| Bob   | 29  | B`erli`n |
| Carol | 31  | Madrid   |

---

## 4. Image

Online image example:

![Sample Image](https://raw.githubusercontent.com/DearVa/Everywhere/refs/heads/main/img/banner.webp)

Local image example:

![Local Image](./Avatar.png)

Avalonia image example:

![Avalonia Image](avares://LiveMarkdown.Avalonia.Demo/Assets/Antelcat.png)
# MarkdownTextBlock 高亮与稳定文本布局规范

状态：最终收束已实现；LiveMarkdown 与 Demo 已完成，ChatWindow 仍待接入发布后的 LiveMarkdown API

## 1. 范围与目标

本规范只约束 `D:\Source\CSharp\LiveMarkdown.Avalonia`。`D:\Source\CSharp\Avalonia` 仅作为只读参考，不能为了本功能修改或维护 Avalonia fork。

目标是让 `MarkdownTextBlock` 拥有一套稳定的文本坐标和绘制层，使以下功能可以共享同一份布局：

- 用户 selection；
- 当前块内的搜索结果和当前搜索结果；
- Markdown inline（尤其是 `CodeInline`）的背景；
- 将来需要的其他 paint-only 文本标注。

高亮只允许影响绘制，不得改变字体选择、glyph shaping、字号、基线、文化、字体特性、换行或测量结果。

## 2. 已确定的设计

### 2.1 布局与 selection

`SelectableTextBlock` 继续负责 selection 的鼠标、键盘、复制和范围状态，但 `MarkdownTextBlock` 不再调用其 selection 专用的 `CreateTextLayout` 实现，也不把 selection 前景样式作为完整 `TextRunProperties` 替换值。

`MarkdownTextBlock` 自己创建 selection-independent 的 `TextLayout`，并在 `RenderTextLayout` 中先绘制背景，再使用同一份布局绘制 glyph。没有 selection 前景时直接调用 `TextLayout.Draw`；存在 `SelectionForegroundBrush` 时，按选区视觉矩形将每个 `ShapedTextRun` 分成互斥的普通/选中裁剪区，复用原始 `GlyphRun` 单次绘制，避免半透明前景的二次混合。这样 selection 改变时不会重新用控件级默认属性覆盖 inline 的字号、字重、装饰、背景、基线或文化。

selection 背景和 `SelectionForegroundBrush` 都在同一份稳定布局上以 paint-only overlay 绘制；前景覆盖会按视觉区间裁剪并复用原始 `GlyphRun`，不会重新布局或二次覆盖半透明前景。

### 2.2 文本坐标

所有范围均使用 UTF-16 索引，并采用半开区间 `[Start, Start + Length)`。`GetTextRangeBounds(start, length)` 返回相对于文本布局原点的一个或多个视觉矩形；`GetTextRangeBoundsInControl(start, length)` 在其基础上加入实际 Padding、布局舍入和垂直布局原点，返回可直接转换到祖先视口的控件坐标，供搜索跳转和滚动定位复用。

矩形由 `TextLine.GetTextBounds` 产生，并按实际视觉行的 Y 坐标合并相邻片段；不手工估算 glyph 宽度，也不拆分 emoji 或组合字符的实际布局。

### 2.3 CodeInline

LiveMarkdown 的 `CodeInline` 直接继承 Avalonia `Run`，不再通过 `InlineUIContainer` 嵌套 `Border` 和子 `MarkdownTextBlock`。因此它与父文本共享字符索引、selection、搜索和换行。

`CodeInline.Background` 使用 `Run`/`TextElement` 的背景属性；`CornerRadius`、`Padding` 与 `Margin` 由父 `MarkdownTextBlock` 统一绘制。`Padding` 与 `Margin` 的水平分量会进入同一份文本布局；当前只承诺水平 Margin，垂直 Margin 保留为 API 对称性但暂不参与布局或绘制，避免一个 inline 改变段落行高。这样相邻普通文字不会与 code inline 的视觉盒子发生重叠，且 UTF-16 索引仍只对应实际 code 文本。

为避免重新实现字体 fallback，只有存在非零水平间距的 `CodeInline` 才会走额外 shaping：LiveMarkdown 建立一次不可变的 fallback glyph catalog，文本源按 formatter 的切分位置创建可释放的 `ShapedTextRun` slice；无间距的普通 inline 保持原有 `TextCharacters` 快路径。catalog 不缓存可变 `ShapedBuffer`，因此 Avalonia 的换行 split 不会污染后续布局。

普通 `Run.Background` 和祖先 `Span.Background` 也在创建文本源时从 `TextRunProperties` 中复制出来，统一转换为 paint-only span 后绘制。布局中的实际 `TextRunProperties` 不再携带背景，因此 selection、CodeInline 和命名 highlight 不会被 Avalonia 的原生背景绘制顺序遮挡。

### 2.4 自定义文本源

由于 Avalonia 的 `LineBreak` 可能以 `TextCharacters`（CRLF）进入受保护的 `InlinesTextSource`，直接转发该 run 会使 formatter 停在同一 source index，产生重复的零长度视觉行。LiveMarkdown 使用自己的 `ITextSource`，把 CRLF、LF、CR、U+2028 和 U+2029 转换为带正确长度的 `TextEndOfLine`，并在 code inline 背景范围内复制完整 run 属性、只清除 native background，避免重复绘制。

这是一层 LiveMarkdown 兼容性 workaround，不修改 Avalonia 源码。

## 3. 高亮 API

### 3.1 `TextHighlightRange`

- `Start` 和 `Length` 必须非负；
- 范围属于单个 `MarkdownTextBlock`，不跨子 `InlineUIContainer` 或其他文本块；
- 同一名称内重叠或相邻范围在注册时合并；
- 超出当前文本长度的端点由布局命中阶段自然裁剪。

通用 `TextHighlightRegistry` 保留上述裁剪语义；`TextSearchMatcher` 作为搜索 API 的输入则必须返回完全位于块本地文本内的范围，提交前会验证并合并它们。

### 3.2 `TextHighlightRegistry`

每个 `MarkdownTextBlock` 持有一个稳定的 `Highlights` 实例。注册项以名称索引，保存范围、优先级和注册顺序；同一优先级下后注册者后绘制。注册表不继承，也不在内部修改调用方传入的集合。

### 3.3 `TextHighlightStyles`

`MarkdownTextBlock.HighlightStyles` 是可继承的 Avalonia attached styled property。样式表可以设置在 Markdown 容器或其他 `StyledElement` 祖先上；本地设置的样式表整体替代继承值，不自动合并多级表。

`MarkdownRenderer` 为搜索消费方提供两层 API：

- 字符串重载支持 `TextSearchOptions.MatchCase` 和 `TextSearchOptions.WholeWord`；
- `TextSearchMatcher(MarkdownTextBlock, string)` 允许调用方自行生成本地范围。

matcher 会被 renderer 保留，并在 MarkdownBuilder 更新后自动重放。范围先在完整计算后统一提交，重叠/相邻范围按注册表规则合并；`ClearTextSearch` 会移除当前搜索产生的范围。`TextSearchMatchesChanged` 用于通知结果刷新。

搜索文本使用每个块自己的布局文本：普通 Run 和换行保持原有 UTF-16 长度，`InlineUIContainer` 在父块中只占一个 U+FFFC；嵌套文本块独立搜索，不把子块文本递归拼入父块坐标。renderer 对文本块列表采用延迟 DFS 缓存，并在文档更新或视觉树 attach/detach 时失效。调用方可用返回的 block/range 设置 `search-current`，再通过 `GetTextRangeBounds` 做滚动定位。

当前已实现的样式字段是：

- `Background`；
- `Foreground`（命名 highlight 与 selection 共用 paint-only glyph 路径）；
- `CornerRadius`；
- `Padding`。

它们都只参与绘制，不参与 shaping。TextDecorations、stroke 和 shadow 等 paint-only 属性保留为后续扩展，不能通过加入字体或布局属性来实现。

## 4. 绘制顺序

相对于同一个文本布局，绘制顺序为：

1. 普通文本背景与 `CodeInline` 背景，按文本坐标绘制；
2. 命名 highlights，按 priority 和注册顺序；
3. selection 背景；
4. glyph 和文本装饰：无任何前景覆盖时由原始 `TextLayout` 绘制；有 selection 或命名 highlight 前景时由 LiveMarkdown 的分区 glyph 绘制路径解析最高优先级的前景，并使用原始属性绘制文本装饰。

多层背景自然按 Avalonia `DrawingContext` 顺序合成。未找到同名样式时保留范围数据但不绘制。

## 5. 非目标与边界

- LiveMarkdown 提供字符串搜索、大小写和全词匹配，以及 delegate 搜索入口；正则语义、结果序号和跨消息聚合仍属于 ChatWindow/ChatContext 应用层。
- 工具调用、控件内容和嵌套子文本块不自动进入父文本块的搜索字符串。
- 不修改 Avalonia 仓库，不依赖未发布的 Avalonia fork。
- CodeInline 的水平 `Padding`/`Margin` 会影响换行宽度；普通 highlight 的 `Padding` 仍然是 paint-only，不影响文本布局。

## 6. 执行计划

### 阶段一：LiveMarkdown 稳定布局（已完成）

完成 selection-independent `TextLayout`、自定义换行文本源、`CodeInline : Run`、paint-only 背景绘制、命中矩形 API，并补充混排、换行和继承测试。

### 阶段二：完整 paint overlay（已完成）

已完成 selection foreground 与命名 highlight foreground 的统一、单次 glyph 绘制，不再依赖 Avalonia 的 selection 属性替换；重叠前景按 `Priority` 再按注册顺序选择最终画刷。`CodeInline.Margin` 与水平 `Padding` 也已通过 fallback-aware 的可切分 shaped run 进入布局。后续继续增加可组合的 TextDecorations 等命名 highlight 字段。

### 阶段三：搜索消费方

`MarkdownRenderer.ApplyTextSearch` 已提供字符串/全词/大小写和 delegate 搜索 API，并使用本地布局文本和延迟块缓存；Demo 已使用 `search-results` 与 `search-current` 两个命名 highlight，提供 Ctrl+F、悬浮搜索框、当前序号/总数和循环前后跳转。跳转使用 `GetTextRangeBoundsInControl` 获取匹配矩形，再转换到 ScrollViewer 视口并居中。

后续由 ChatWindow 为每条文本消息维护同样的两个命名 highlight；HistoryDockPanel 的筛选作为独立的标题/内容查询实现。工具调用等非文本内容不参与消息搜索。

## 7. 验收标准

- selection 前后，普通文本、粗体/斜体、不同字号、emoji 和多行文本的 `TextLayout` 行索引、行高和命中矩形保持稳定；
- 半透明 `SelectionForegroundBrush` 不与原始 glyph 前景二次混合，文本装饰在选区内使用相同的选区前景规则；
- `CodeInline` 与普通 Run 共享 `ActualText`、selection 和换行坐标；
- LineBreak 不产生重复零长度视觉行；
- 祖先上的 `HighlightStyles` 可被后代文本块读取，旧样式表替换后不再收到事件；
- ranges 的重叠、相邻、优先级和注册顺序结果确定；
- 全量 LiveMarkdown 测试通过，且 Avalonia 工作树保持未修改。

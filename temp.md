# 临时实施记录

说明：本文件前半部分保留了此前已撤销的 Avalonia 试验记录；从“重新实施后的 LiveMarkdown-only 约束”起，记录当前有效方案。当前 Avalonia 工作树保持干净。

## 2026-08-07：Avalonia 定向测试的环境权限问题

- 首次运行 `Avalonia.Controls.UnitTests` 的 `SelectableTextBlockTests` 时，测试尚未进入编译阶段。
- `D:\Source\CSharp\Avalonia` 不在当前执行沙箱的可写根目录内，`dotnet` 无法在 Avalonia 各项目的 `obj/bin` 目录创建或更新临时构建文件。
- 这不是本次代码补丁或测试代码导致的编译错误；需要使用一次经批准的提升权限测试命令继续验证。
- 当前没有引入代码 workaround，也没有修改或删除 Avalonia 的构建产物。

## 2026-08-07：测试筛选参数的工具链差异

- 提升权限重跑后，Avalonia 项目已成功编译，但当前 Microsoft.Testing.Platform runner 不接受旧的 `--filter`/`--nologo` 应用参数组合，因此报告运行了零个测试并返回退出码 5。
- 这不是测试失败；后续使用该 runner 支持的 `--filter-class` 参数重新执行。

## 2026-08-07：`CanShapeTogether` 的独立 shaping 检查

- 检查发现 `TextFormatterImpl.CanShapeTogether` 原本只比较字号、Typeface 和基线，没有比较会传入 `TextShaperOptions` 的 `CultureInfo` 与 `FontFeatures`。
- 这与阶段一 spec 的 shaping 不变量不符；已在 Avalonia 中补上两项比较，并增加录制 shaper 选项的回归测试。该修改保持为独立的小范围修复，没有改动 LiveMarkdown 的渲染流程。

## 2026-08-07：混合字形回归测试的容差与分段差异

- 混合 emoji、emoji ZWJ、字体 fallback 和多字号 Run 的回归测试首次使用精确宽度比较时，发现选区前后仅有约 `3e-14` 的浮点舍入差异；测试改为 `1e-9` 容差，这不是生产代码 workaround。
- 选区覆盖会改变 `ShapedTextRun` 的文本分段边界，但 glyph index、cluster、advance、fallback Typeface 和字号均保持一致。因此测试快照忽略纯分段文本值，只比较实际渲染相关数据。

## 2026-08-07：定向验证结果与宿主输出噪声

- 使用当前 runner 支持的 `--filter-class` 直接执行编译后的测试程序：`SelectableTextBlockTests` 共 10/10 通过，`TextFormatterTests` 共 59/59 通过。
- 经提升权限的 PowerShell 命令会额外输出用户 profile 中的 PSReadLine 虚拟终端警告，但命令退出码为 0，和代码或测试结果无关。

## 2026-08-07：重新实施后的 LiveMarkdown-only 约束

- 用户撤销了 Avalonia 仓库的试验性修改；本次实现严格只修改 `D:\Source\CSharp\LiveMarkdown.Avalonia`。Avalonia 工作树在实施前已确认干净，后续也没有写入。
- 生产项目第一次使用 `--no-restore` 编译时，`ResolvePackageAssets` 因旧的 project.assets.json 在进入 C# 编译前报空引用；执行一次经批准的 `dotnet restore` 后恢复正常。这是构建环境 workaround，不是代码依赖变更。
- 新增 LiveMarkdown `CodeInline : Run` 后，Mermaid 解析器中的 `CodeInline` 模式发生命名冲突；通过显式使用 Markdig 类型别名解决，没有引入新包。
- 定位到 Avalonia `LineBreak` 作为 `TextCharacters`（CRLF）经过受保护 `InlinesTextSource` 时，formatter 可能在同一 source index 重复返回零长度视觉行。LiveMarkdown 新增私有 `ITextSource`，将换行转换为 `TextEndOfLine` 并保留 UTF-16 长度；这是不修改 Avalonia 的本地兼容 workaround。
- 当前第一阶段的 `TextHighlightStyle` 只绘制 Background/CornerRadius/Padding；Foreground、TextDecorations 和 selection foreground overlay 尚未实现，已在 `spec.md` 标注为后续阶段，不能把该限制误认为已完成的 CSS 高亮能力。
- `CodeInline.Padding` 与高亮 Padding 当前是视觉矩形扩展，不会为文本布局额外占宽；改变这一语义需要另立布局方案。

## 2026-08-07：统一背景绘制管线实施结果

- `MarkdownTextBlock` 现在为普通文本背景、`CodeInline`、命名 highlight 和 selection 生成统一的 paint span，并在同一个 `TextLayout` 坐标系中绘制；没有任何装饰时保留直接 `TextLayout.Draw` 的 early return。
- 含有 `Background` 的普通 `TextRunProperties` 会在自定义文本源中复制为不带背景的属性，背景改由父控件绘制。这样 selection 不会被 Avalonia 原生文本背景覆盖，也不会通过属性替换改变混排布局。
- 为普通文本背景补充了回归测试：即使设置 selection，最终 `TextLayout` 的 shaped runs 也不再携带 native background；LiveMarkdown 全量测试目前为 102 通过、3 跳过。
- Demo 的旧 `InlineUIContainer.Code` 样式选择器已改为 `md|CodeInline`，以匹配 direct `Run` 实现。
- 本阶段仍有意保留两个限制：`SelectionForegroundBrush` 尚未转换为 paint-only 前景 overlay；`Padding` 只扩展绘制矩形，不影响布局宽度。这些属于 spec 阶段二/独立布局设计，不是临时 workaround。
- 为继续使用 Avalonia 未公开的 `TextParagraphProperties.LineSpacing` setter，LiveMarkdown 将原来的每次反射调用替换为 .NET `UnsafeAccessor`。这仍是 LiveMarkdown 侧的兼容 workaround，未修改 Avalonia 源码；若后续 Avalonia 暴露公开 setter，应删除该访问层。
- 最后一次完整测试曾偶发在既有链接交互测试中报 `TextDecoration` 的 dispatcher 跨线程异常，堆栈位于 Avalonia `TextDecoration.Draw`，随后原命令无改动重跑即恢复为 102 通过、3 跳过。当前判断为 headless/compositor 测试环境噪声，未添加规避代码；若后续稳定复现再单独定位。
- Demo 项目在当前依赖资产状态下最终使用 `dotnet build --no-restore` 成功（0 警告、0 错误），新的 `CodeInline` XAML selector 已通过编译。

## 2026-08-07：SelectionForegroundBrush 单次 glyph 绘制

- 直接在 `TextLayout.Draw` 之后覆盖选区 glyph 被确认不可接受：半透明前景会形成 `SelectionForeground over OriginalForeground over Background` 的二次混合；即使画刷本身不透明，抗锯齿边缘也会保留原前景的污染。
- `MarkdownTextBlock` 现在在存在选区前景时接管 `TextLine.TextRuns` 绘制：普通区间和选中区间使用互斥的水平裁剪，复用同一个已 shaping 的 `GlyphRun`，避免重新布局和重复覆盖；没有选区前景时仍走 `TextLayout.Draw` 快路径。
- Avalonia 的 `TextDecoration.Draw` 是 `internal`，LiveMarkdown 无法直接调用。为保持选中前后的下划线、删除线和上划线行为，LiveMarkdown 侧复制了 Avalonia 当前公开几何数据上的装饰绘制算法；这是 LiveMarkdown-only workaround，若 Avalonia 将来公开该 API，应删除重复实现并改为调用官方方法。
- 选区裁剪只限制水平方向，垂直范围按整个 layout 扩展；这是为了保留 glyph overhang 和 decoration 的垂直延伸，同时依赖控件自身的 render clip 限制可见区域。
- 当前测试运行在默认 headless drawing 模式，不能读取最终像素帧，因此新增回归使用半透明 `SelectionForegroundBrush`、混合字号/emoji 和下划线，验证路径可执行、布局不变；逐像素验证仍需要后续启用 Skia headless framebuffer 的专门测试。
- LiveMarkdown 库构建 net8/net10 均为 0 警告、0 错误；测试 102 通过、3 跳过。
- 最后尝试构建嵌套 Demo 项目时再次遇到已有 `project.assets.json` 导致的 `ResolvePackageAssets` `NullReferenceException`；项目尚未进入 C# 编译阶段。该问题与本次渲染代码无关，按既有记录执行定向 restore 后重试成功，Demo 最终 0 警告、0 错误。

## 2026-08-07：命名 highlight 前景统一

- 用户决定跳过像素测试，直接进入后续实现。`TextHighlightStyle` 新增 paint-only `Foreground`；命名 highlight 前景与 `SelectionForegroundBrush` 现在共用区间解析和单次 `GlyphRun` 绘制路径。
- 重叠前景先按视觉区间拆分，再按 `Priority` 和注册顺序选择胜出的画刷；selection 使用 `int.MaxValue/long.MaxValue`，因此始终覆盖命名 highlight 前景。这样半透明前景不会进行叠加重绘。
- 为了保持热点路径，命名 highlight 前景范围在注册表/样式变更时缓存；没有任何背景或前景绘制层时仍然直接调用 `TextLayout.Draw`。
- 初次编译发现缓存 API 返回 `IReadOnlyList`，错误地使用了 `.Length`；已修正为 `.Count`，没有生产 workaround。

## 2026-08-07：CodeInline 横向 Padding/Margin 进入布局

- 用户选择跳过像素测试，继续实施后续布局能力。`CodeInline` 新增 `Margin` 属性；水平 `Padding` 与 `Margin` 现在会真实增加文本布局宽度，垂直分量仍只用于背景矩形，避免一个 inline 改变整段行高。
- 直接用 `TextShaper.ShapeText` 会绕过 Avalonia 的字体 fallback，因此没有采用单字体直 shaping。LiveMarkdown 在创建 paint snapshot 时，对存在非零水平间距的 CodeInline 建立一次临时 `TextLayout`，复制其已经完成 fallback 的 glyph 数据，再按 formatter 请求的 source index 生成新的 `ShapedTextRun` slice。
- catalog 不缓存可变 `ShapedBuffer`；每个 source 请求创建独立 buffer，允许 Avalonia 正常 split/dispose，避免换行过程中修改共享 glyph 数据。没有水平间距时仍走原来的 `TextCharacters` 快路径，以控制常规文本热点开销。
- CodeInline 含换行时暂不启用额外间距 shaping，回退到普通文本源；这是当前公开 API 下的保守边界，不影响普通 CodeInline 的文本索引和换行。若以后需要支持多行 code inline 间距，需要把 `TextEndOfLine` 纳入 catalog 的 source-run 模型。
- 背景绘制会把 CodeInline 已进入布局的水平 `Margin + Padding` 从 `TextLine.GetTextBounds` 的内容矩形中扣除，再应用一次 visual Padding；普通 highlight 的 Padding 是纯绘制扩展、不会被扣除。这样 margin 是布局间隔而不是背景颜色的一部分，同时避免水平 Padding 被计算两次。
- 新增实现通过 LiveMarkdown 自己的 `ShapedBuffer` 公共构造函数和索引器复制 glyph；没有修改 Avalonia 源码。当前库编译 net8/net10 均为 0 警告、0 错误，定向 Markdown selection/pointer 测试 24/24 通过。

## 2026-08-07：HistoryDockPanel 筛选消费方起步

- Everywhere 的 HistoryDockPanel 已加入查询框和“包括内容”开关。主题匹配立即完成；勾选内容后，当前已加载的 metadata 对应 ChatContext 会在后台检查当前分支中的 `UserChatMessage.Content` 与 `AssistantChatMessageTextSpan`，不会检查 FunctionCall、工具参数/结果或 plugin display block。
- 当前 manager 的历史元数据仍按原有分页加载，内容筛选只覆盖已经加载的 metadata；没有新增数据库全文索引，也没有在输入每个字符时同步阻塞 UI。这是第一步的明确边界，后续若要“全部历史”搜索需要独立的 storage-level query 方案。
- Everywhere Core 编译通过；NuGet 现有的 `SQLitePCLRaw.lib.e_sqlite3` 安全警告仍为仓库既有警告，与本次改动无关。

## 2026-08-07：MarkdownRenderer 搜索消费 helper

- 为减少 ChatWindow 直接遍历内部文档树的耦合，`MarkdownRenderer.ApplyTextSearch` 现在按每个 `MarkdownTextBlock` 的 `ActualText` 查找 literal matches，并返回 `TextHighlightMatch(Block, Range)`；结果范围写入指定 highlight 名称，renderer 的 MarkdownBuilder 更新后会自动重新应用查询。
- 该 helper 只处理 renderer 内的 Markdown 文本块，不会把外部 tool/plugin 控件的字符串自动纳入搜索。包含复杂嵌套 `InlineUIContainer` 的文档仍应由消费方按具体消息边界选择 renderer；它不是跨文本块拼接器。
- 额外检查了 CodeInline shaping catalog 的生命周期：字体、字号、字重、前景、TextDecorations、FontFeatures、LetterSpacing 或 FlowDirection 改变时会使 paint snapshot 失效，避免缓存的 fallback glyph 使用旧的字体属性。普通无间距路径仍保留原有 early return。

## 2026-08-07：Everywhere 当前聊天搜索的包边界

- HistoryDockPanel 的筛选已接入 Everywhere；当前聊天 Ctrl+F 若要直接调用 `MarkdownRenderer.ApplyTextSearch`，Everywhere 必须引用包含该 API 的 LiveMarkdown 构建。现有 Everywhere 仍引用 NuGet `LiveMarkdown.Avalonia` 2.2.2，而新 API 尚未发布到该包，因此暂未把外部 sibling 仓库作为生产 `ProjectReference`，也没有修改 NuGet 依赖。
- 在不改变依赖边界的情况下，强行通过反射或 Avalonia 原生 Selection 拼接当前搜索会重新引入本次要规避的布局/选择副作用；当前将此项保留为包更新后的下一步集成点，而不是提交一个不稳定的 workaround。

## 2026-08-08：Demo 搜索跳转的 Avalonia 版本差异

- 本地 Avalonia 12 源码已经公开 `ScrollViewer.BringDescendantIntoView(Visual, Rect)`，但 LiveMarkdown Demo 当前依赖 Avalonia 11.3.2，该方法还不是可用的公共 API，因此 Demo 不能直接采用这一入口。
- `MarkdownTextBlock.GetTextRangeBoundsInControl` 负责把 UTF-16 匹配范围转换为控件坐标，并复用 TextBlock 实际的 Padding、布局舍入和垂直原点算法。Demo 再用 `TranslatePoint` 把命中矩形中心转换到 ScrollViewer 当前视口，叠加当前 Offset 后将结果居中；这避免了复制 Markdown 布局细节，也不依赖会被 MarkdownRenderer 拦截的 routed `BringIntoView`。

## 2026-08-08：MarkdownRenderer 搜索 API 与缓存实施

- 原搜索 helper 使用 `ActualText`。只读定位发现 `ActualText` 会递归拼入 `InlineUIContainer` 子 `MarkdownTextBlock`，但父 `TextLayout` 只为该控件保留一个 U+FFFC 占位符，导致父块范围可能错位并与子块重复。实现改为缓存每个块的本地布局文本（`Inlines.Text`/`Text`）；子块独立搜索。这是坐标正确性修复，不是 Avalonia workaround。
- 没有恢复历史上由 `DocumentNode.Update` 传递收集 List 的方案。renderer 现在按真实 visual DFS 延迟缓存文本块，在文档更新和 `MarkdownTextBlock` attach/detach 时失效，兼容模板内部和自定义节点；selection 在当前 renderer scope 使用独立的 selectable-block 缓存（保留 renderer 外部自定义视觉子树），外部共享 scope 仍保留视觉遍历。
- `ApplyTextSearch` 新增 `TextSearchMatcher` delegate、`TextSearchOptions.MatchCase/WholeWord`、`ClearTextSearch` 和 `TextSearchMatchesChanged`。delegate 结果先完整验证、排序和合并，再写入 highlight，避免异常时留下部分结果；字符串便利重载使用 ordinal 比较以保证 UTF-16 区间长度稳定。
- 为避免热点路径重复构造 `SelectedText`，selection 拖拽期间 `UpdateCanCopy` 直接检查缓存块的 selection 范围。无活动搜索时 `ArrangeCore` 不再枚举 MarkdownTextBlock；无结果块也不再写入空 highlight，避免无意义的视觉失效。
- 首次普通沙箱构建无法写入 LiveMarkdown 仓库现有 obj/bin，按权限要求使用提升权限完成验证；这是构建环境限制，没有引入代码 workaround。构建和全量测试最终通过：库 net8/net10 无警告错误，测试 107 通过、3 跳过。
- 构建还暴露了当前工作区已有的数组类型 `activeScopeBlocks` 与 `IndexOf` 扩展不匹配；已改用 `Array.IndexOf`。Avalonia `VisualTreeAttachmentEventArgs.Parent` 已过时，缓存失效逻辑改用 `AttachmentPoint`。

## 2026-08-08：Demo 搜索界面的 MVVM 重构

- Demo 的搜索开关、查询文本、全词匹配、区分大小写、结果计数和当前索引已迁移到 `MainViewModel`；Find 使用 `ToggleButton.IsChecked` 双向绑定，关闭/前后跳转及 Ctrl+F、Escape、Enter 快捷键均通过命令绑定。
- MarkdownRenderer 的搜索结果仍需要由 `MainView` 应用高亮并计算滚动坐标，因为这些操作直接依赖视觉树、`MarkdownTextBlock` 和 `ScrollViewer`。ViewModel 通过搜索参数变化/导航请求事件通知这一 UI bridge；这不是按钮事件 workaround，而是避免让 ViewModel 持有 Avalonia 控件引用的边界。
- Demo 项目 `dotnet build --no-restore` 验证通过，0 警告、0 错误。
- 普通沙箱复核时 `AvaloniaStatsTask` 无法写入用户目录下的 `buildtasks.log`；使用一次提升权限的同一构建命令后验证通过。这是宿主权限问题，不是 Demo 编译错误。

## 2026-08-08：CodeInline Margin 首次布局修复

- 只读定位确认，截图中的问题不是全局 Style 应用太晚：`Margin=12,0` 和 `Padding=2,0` 在首次布局前已经生效。真正的首要原因是 `CodeInlineLayout` 建立嵌套 `TextLayout` 后，把 Avalonia 为段落结束附加的 `TextEndOfParagraph` 哨兵也计入了 source index，导致 `currentIndex` 比实际文本长度多 1，布局创建始终返回失败。
- LiveMarkdown 侧现在忽略该格式化哨兵，只让实际 `ShapedTextRun` 推进 CodeInline 的 glyph/source index；没有修改 Avalonia 源码。这个修复使全局 Style 首次布局和运行时样式变化都能进入自定义 CodeInline layout。
- 发现的第二个问题是仅增加首 glyph 的 `GlyphAdvance` 会把所谓左侧间距塞到首、次 glyph 之间。现在在对应视觉边缘同时增加 `GlyphOffset.X`，让左侧 Margin/Padding 真正位于文本外侧；右侧仍只保留 advance 作为尾部布局空间。
- `CodeInline.Padding` 和 `Margin` 改变时现在会使父 `MarkdownTextBlock` 的文本布局失效；`Background`/`CornerRadius` 仅使视觉失效，保留绘制热点路径。新增回归测试验证 `12+2` 的两侧间距贡献 `28` 个布局单位，并验证首 glyph 偏移为 `14`。
- 当前仍保留一个边界：混合 bidi 的 CodeInline 需要进一步用逻辑/视觉 run 映射验证；本次修复覆盖普通 LTR/纯 RTL 的视觉边缘路径，没有用 workaround 修改 Avalonia。
- 复核 spec 时发现此前“垂直 Margin 只扩展背景矩形”的描述超出了当前实现；本次已将 API 文档/spec 校正为“垂直 Margin 暂不参与布局或绘制”。用户当前需求主要是左右间距，因此没有为了补齐未要求的垂直语义扩大改动面。
- 验证结果：LiveMarkdown 库 net8/net10 构建 0 警告、0 错误；定向回归测试通过；全量测试 `110` 通过、`3` 跳过。

## 2026-08-08：最终收束中的 bidi source 映射兼容

- 将 CodeInline 嵌套布局的 catalog 改为直接依赖 `TextLine.GetTextBounds().TextRunBounds` 时，发现 Avalonia 对纯 RTL buffer 返回的 `TextSourceCharacterIndex` 可能受 `GlyphRun` 的反向 cluster 偏移影响。例如 `אבג` 的 glyph clusters 为 `2,1,0`，但 bounds 返回的 source index 为 `1`、length 为 `3`，无法直接覆盖从 `0` 开始的 UTF-16 文本。
- 未修改 Avalonia。LiveMarkdown 改用 `ShapedTextRun.Text` 的 `ReadOnlyMemory<char>`，通过 `MemoryMarshal.TryGetString` 取得原始 CodeInline 字符串中的逻辑 offset；该内存切片来自本次嵌套 `CodeInlineTextSource`，因此可同时处理字体 fallback、混合 bidi 和视觉 run 重排。若未来 Avalonia 改变内存所有权，仍保留 `TextRunBounds` 作为保守降级路径。
- 新增混合 bidi CodeInline 回归测试，验证 `abc אבג` 的 shaped UTF-16 覆盖完整且左右布局间距仍为 `Margin + Padding` 的两侧总和。

## 2026-08-08：绘制算法最终收束

- `TextPaintSnapshot` 现在在一次按 source index 的扫描中同时建立 BackgroundSpan、清除原生背景的 PropertyOverride 和 CodeInlineLayout，删除了每个 CodeInline 从头重扫 TextRun/paint span 的路径。
- CodeInline 的背景创建与自定义 layout 创建绑定：layout 成功时只使用横向 BackgroundInset（Margin），水平 Padding 已由布局 advance 提供；layout 失败时回退为普通 visual padding，不扣除不存在的布局空间。
- 普通 Background、CodeInline、命名 Highlight 和 Selection 均通过同一个流式 `DrawPaintSpan` 绘制；首段 inset 使用实际已绘制 fragment 状态判断，修复了首个矩形被错误计数导致的左侧偏移。
- 命名 Highlight 的背景/前景统一缓存为一个快照；无重叠 Foreground 区间和单一 SelectionForeground 使用 early return，只有真实重叠才进入边界拆分和优先级解析。
- `CodeInline.CreateRun` 改为两次 glyph 计数/填充，不再为每次 formatter slice 分配临时 `List<GlyphInfo>`。
- 新增 DrawingGroup 几何回归测试，验证 `Margin=12, Padding=2` 时背景左/右边界分别等于布局范围加/减 Margin，避免只通过宽度和 glyph offset 间接验证。
- 最终验证：LiveMarkdown 库 net8/net10 构建均为 0 警告、0 错误；Demo 构建为 0 警告、0 错误；测试共 `112` 通过、`3` 跳过。

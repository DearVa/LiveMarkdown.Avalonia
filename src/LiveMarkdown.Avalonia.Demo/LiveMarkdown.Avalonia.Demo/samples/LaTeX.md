# RaTeX rendering samples

Switch **Toggle Mode** between light and dark. Default-colored formulas should follow the surrounding text; explicit LaTeX colors should stay unchanged.

Use **Reset** with a low **Render speed** to watch partially received formulas become complete. To test error recovery, correct an invalid formula in this sample file and select LaTeX again.

## Basic expressions and delimiters

Inline 1: $a,b,c$

Inline 2: $a+b\times c$

Inline 3: $C^a_b+\sqrt{c}$

Inline 4: $\sum_{i=1}^n a_i$

Inline 5: \( E=mc^2 \)

Inline 6: \[ \int_a^b f(x) \, dx \]

Error Inline: $\sum_{i=1$

Block 1:
$$
a,b,c
$$

Block 2:
$$
a+b\times c
$$

Block 3:
$$
C^a_b+\sqrt{c}
$$

Block 4:
$$
\sum_{i=1}^n a_i
$$

Block 5:
$$
E=mc^2
$$

Block 6:
$$
\int_a^b f(x) \, dx
$$

Error Block:
$$
\sum_{i=1
$$
## CJK and font fallback

Chinese: $\text{中文：你好，世界}$, with math $\text{面积} = \pi r^2$.

Japanese: $\text{日本語：こんにちは世界}$.

Korean: $\text{한국어: 안녕하세요 세계}$.

Mixed scripts: $\text{中文 English 日本語 한국어} + x_1$.

Single-character emoji fallback: $\text{结果 ★ 😀} = 42$.

Measured fallback in fractions and scripts: $\frac{\text{中文😀}}{\text{日本語}} + \text{한}_i^2$.

$$
\text{总成本} = \sum_{i=1}^{n} \text{单价}_i \times \text{数量}_i
$$

\[
\begin{aligned}
\text{平均值} &= \frac{1}{n}\sum_{i=1}^{n} x_i \\
\text{分散} &= \frac{1}{n}\sum_{i=1}^{n}(x_i-\bar{x})^2
\end{aligned}
\]

Check for missing-glyph boxes, overlapping characters, and clipped CJK punctuation. Emoji appearance depends on the installed fallback font.

## Explicit colors and theme inheritance

Default color: $x^2+y^2=z^2$. Switch light/dark mode without reloading this sample.

Named colors: $\textcolor{red}{a} + \textcolor{blue}{b} = \textcolor{green}{c}$.

Hex colors: $\textcolor{#E53935}{x^2} + \textcolor{#1E88E5}{y^2} = \textcolor{#43A047}{z^2}$.

Scoped color: ${\color{purple} a+b} + c$. The trailing $c$ in that formula should keep the default color.

Colored CJK: $\textcolor{#E67E22}{\text{橙色中文}} + \text{默认颜色}$.

$$
\textcolor{#D81B60}{\frac{-b \pm \sqrt{b^2-4ac}}{2a}}
\qquad
\textcolor{#00897B}{\int_0^1 x^2\,dx = \frac{1}{3}}
$$

## LaTeX font sizes

Tiny: ${\tiny x_1^2 + \frac{a}{b}}$ beside normal text.

Small: ${\small x_1^2 + \frac{a}{b}}$ beside normal text.

Normal: ${\normalsize x_1^2 + \frac{a}{b}}$ beside normal text.

Large: ${\Large x_1^2 + \frac{a}{b}}$ beside normal text.

Huge: ${\Huge x_1^2 + \frac{a}{b}}$ beside normal text.

$$
{\small \text{小字号} + x}
\quad {\normalsize \text{正常字号} + x}
\quad {\Large \text{大字号} + x}
$$

## Heading font-size inheritance: $E=mc^2$

### Smaller heading: $E=mc^2$

Normal paragraph: $E=mc^2$. Each formula should match its surrounding text size.

## Inline baseline and wrapping

中文基线 $x_i^2$ 与 English baseline $g_j$ 应与正文自然对齐，逗号和句号不应明显跳动。

Fractions $\frac{1}{2}$, radicals $\sqrt{x^2+y^2}$, sums $\sum_{k=1}^{n} k$, and integrals $\int_a^b f(x)\,dx$ share one paragraph.

Tall inline math: $\left(\frac{\frac{a}{b}}{\frac{c}{d}}\right)$ followed by normal text. The lines above and below should not overlap.

Resize the window to wrap this paragraph: before $a_1$ between $b_2^3$ after $\frac{x+y}{z}$ 中文与公式混排 $\text{长度} = 2\pi r$ continue to the next line.

## Matrices and multiline layout

$$
\begin{pmatrix}
a & b \\
c & d
\end{pmatrix}
\begin{pmatrix}
x \\ y
\end{pmatrix}
=
\begin{pmatrix}
ax+by \\ cx+dy
\end{pmatrix}
$$

$$
f(x) = \begin{cases}
x^2, & x \ge 0 \\
-x, & x < 0
\end{cases}
$$

## Error source and recovery

Unclosed group: $\frac{1}{2$.

Unknown command: $\unknowncommand{x}$.

Valid neighbor: $\frac{1}{2}$. An error must not hide or break the formulas around it.

$$
\unknowncommand{x}
$$

During **Reset**, a block still receiving content should show plain source until its closing delimiter arrives. The block below intentionally has no closing delimiter:

\[
\text{等待结束符} + \frac{a}{b}
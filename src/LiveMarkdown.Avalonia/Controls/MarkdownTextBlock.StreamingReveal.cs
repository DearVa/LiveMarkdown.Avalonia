using System.Diagnostics;
using Avalonia;
using Avalonia.Media.TextFormatting;
using Avalonia.Rendering.Composition;
using Avalonia.VisualTree;

namespace LiveMarkdown.Avalonia;

public partial class MarkdownTextBlock
{
    /// <summary>Defines the inherited <see cref="StreamingRevealMode"/> property.</summary>
    public static readonly StyledProperty<MarkdownStreamingRevealMode> StreamingRevealModeProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, MarkdownStreamingRevealMode>(nameof(StreamingRevealMode), inherits: true);

    /// <summary>Gets or sets how new text appears during an incremental document update.</summary>
    public MarkdownStreamingRevealMode StreamingRevealMode
    {
        get => GetValue(StreamingRevealModeProperty);
        set => SetValue(StreamingRevealModeProperty, value);
    }

    /// <summary>Defines the inherited <see cref="StreamingRevealDuration"/> property.</summary>
    public static readonly StyledProperty<TimeSpan> StreamingRevealDurationProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, TimeSpan>(nameof(StreamingRevealDuration),
            TimeSpan.FromMilliseconds(220), inherits: true, validate: value => value >= TimeSpan.Zero);

    /// <summary>Gets or sets how long each newly streamed text chunk or line takes to fade in. Zero reveals opacity immediately.</summary>
    public TimeSpan StreamingRevealDuration
    {
        get => GetValue(StreamingRevealDurationProperty);
        set => SetValue(StreamingRevealDurationProperty, value);
    }

    /// <summary>Defines the inherited <see cref="StreamingRevealRiseDuration"/> property.</summary>
    public static readonly StyledProperty<TimeSpan> StreamingRevealRiseDurationProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, TimeSpan>(nameof(StreamingRevealRiseDuration),
            TimeSpan.FromMilliseconds(380), inherits: true, validate: value => value >= TimeSpan.Zero);

    /// <summary>Gets or sets how long newly appearing lines take to rise into place. Zero snaps vertical position immediately.</summary>
    public TimeSpan StreamingRevealRiseDuration
    {
        get => GetValue(StreamingRevealRiseDurationProperty);
        set => SetValue(StreamingRevealRiseDurationProperty, value);
    }

    /// <summary>Defines the inherited <see cref="StreamingRevealOffset"/> property.</summary>
    public static readonly StyledProperty<double> StreamingRevealOffsetProperty =
        AvaloniaProperty.Register<MarkdownTextBlock, double>(
            nameof(StreamingRevealOffset),
            4d,
            inherits: true,
            validate: static value => double.IsFinite(value) && value >= 0);

    /// <summary>Gets or sets the vertical rise distance in DIPs for newly appearing lines. Zero disables rising.</summary>
    public double StreamingRevealOffset
    {
        get => GetValue(StreamingRevealOffsetProperty);
        set => SetValue(StreamingRevealOffsetProperty, value);
    }

    internal bool IsStreamingRevealEnabled { get; private set; }

    internal long StreamingAttachTimestamp { get; set; }

    internal IReadOnlyList<StreamingRevealSpan> ActiveRevealSpans => _reveal?.Spans ?? (IReadOnlyList<StreamingRevealSpan>)[];

    internal StreamingRevealSnapshot? ActiveRevealSnapshot => _reveal?.Snapshot;

    private RevealState? _reveal;

    // Exists only between an eligible append and its handback to static drawing.
    private sealed class RevealState
    {
        public readonly List<StreamingRevealSpan> Spans = [];
        public CompositionCustomVisual? Visual;
        public TextLayout? Layout;
        public Point Origin;
        public Size Size;
        public StreamingRevealSnapshot? Snapshot;
        public int? PreviousLineCount;
        public int UnmeasuredPrefix;
        public long LineTimestamp;
    }

    internal (string Text, int LineCount) CaptureRevealBaseline()
    {
        // Several updates can arrive before a measure pass invalidates the cached layout text again.
        _layoutText = null;
        return (LayoutText, StreamingRevealMode == MarkdownStreamingRevealMode.Line && IsMeasureValid ? TextLayout.TextLines.Count : -1);
    }

    internal TimeSpan EffectiveRevealDuration
    {
        get
        {
            var fade = StreamingRevealDuration;
            if (StreamingRevealOffset <= 0) return fade;
            var rise = StreamingRevealRiseDuration;
            return rise > fade ? rise : fade;
        }
    }

    internal void ApplyRevealUpdate((string Text, int LineCount) baseline)
    {
        var duration = EffectiveRevealDuration;
        if (!IsStreamingRevealEnabled || duration == TimeSpan.Zero ||
            SelectionStart != SelectionEnd || !this.IsAttachedToVisualTree())
        {
            return;
        }

        _layoutText = null;
        var text = LayoutText;
        var prefix = text.AsSpan().CommonPrefixLength(baseline.Text.AsSpan());
        if (prefix == text.Length && prefix == baseline.Text.Length) return;

        // Closing Markdown delimiters can remove *previously visible* punctuation. Keep the
        // letters that follow those delimiters settled (e.g. "**hel" -> "hello" reveals "lo").
        var visiblePrefix = prefix;
        for (var old = prefix; old < baseline.Text.Length && visiblePrefix < text.Length; old++)
        {
            if (baseline.Text[old] == text[visiblePrefix]) visiblePrefix++;
            else if (baseline.Text[old] is not ('*' or '_' or '~' or '`' or '[' or ']' or '\\')) break;
        }

        if (visiblePrefix == text.Length && _reveal is null) return;
        if (_reveal is null)
        {
            _reveal = new RevealState();
            LayoutUpdated += OnRevealLayoutUpdated;
        }

        var state = _reveal;
        var now = Stopwatch.GetTimestamp();
        if (StreamingRevealMode == MarkdownStreamingRevealMode.Character)
        {
            for (var i = state.Spans.Count - 1; i >= 0; i--)
            {
                var span = state.Spans[i];
                var length = Math.Min(span.Length, prefix - span.Start);
                if (length <= 0 || Stopwatch.GetElapsedTime(span.Timestamp, now) >= duration) state.Spans.RemoveAt(i);
                else if (length < span.Length) state.Spans[i] = span with { Length = length };
            }

            if (visiblePrefix < text.Length)
            {
                state.Spans.Add(new StreamingRevealSpan(visiblePrefix, text.Length - visiblePrefix, now));
            }
        }
        else if (state.PreviousLineCount is null && visiblePrefix < text.Length)
        {
            state.PreviousLineCount = baseline.Text.Length == 0 ? 0 : baseline.LineCount;
            state.UnmeasuredPrefix = visiblePrefix;
            state.LineTimestamp = now;
        }

        InvalidateRevealSnapshot();
    }

    private void InvalidateRevealSnapshot()
    {
        // A completion notification for the previous snapshot may already be in the UI queue.
        _reveal!.Layout = null;
        InvalidateArrange();
    }

    private void OnRevealLayoutUpdated(object? sender, EventArgs e)
    {
        if (_reveal is not { } state || !IsArrangeValid) return;
        var origin = GetTextLayoutOrigin();
        if (ReferenceEquals(state.Layout, TextLayout) && state.Origin == origin && state.Size == Bounds.Size) return;

        var lines = GetLineGeometry();
        var mode = StreamingRevealMode;
        var fadeDuration = StreamingRevealDuration;
        var riseOffset = StreamingRevealOffset;
        var riseDuration = riseOffset > 0 ? StreamingRevealRiseDuration : TimeSpan.Zero;
        var spanDuration = riseDuration > fadeDuration ? riseDuration : fadeDuration;
        var now = Stopwatch.GetTimestamp();
        for (var i = state.Spans.Count - 1; i >= 0; i--)
        {
            var span = state.Spans[i];
            if (Stopwatch.GetElapsedTime(span.Timestamp, now) >= spanDuration ||
                (mode == MarkdownStreamingRevealMode.Line && span.Start >= lines.Length))
            {
                state.Spans.RemoveAt(i);
            }
        }

        if (state.PreviousLineCount is { } previous)
        {
            if (previous == -1)
            {
                previous = state.UnmeasuredPrefix == 0 ? 0 : FindFirstLine(lines, state.UnmeasuredPrefix - 1) + 1;
            }

            if (Stopwatch.GetElapsedTime(state.LineTimestamp, now) < spanDuration)
            {
                for (var i = previous; i < lines.Length; i++)
                {
                    state.Spans.Add(new StreamingRevealSpan(i, 1, state.LineTimestamp));
                }
            }

            state.PreviousLineCount = null;
        }

        var first = lines.Length;
        foreach (var span in state.Spans)
        {
            if (mode == MarkdownStreamingRevealMode.Line)
            {
                first = span.Start;
                break;
            }

            // A newly appended newline belongs to the preceding, already-painted line.
            // It has no ink; keep that line on the static path.
            var start = span.Start;
            var end = Math.Min(start + span.Length, LayoutText.Length);
            while (start < end && LayoutText[start] is '\r' or '\n' or '\u2028' or '\u2029')
            {
                start++;
            }

            if (start < end)
            {
                first = FindFirstLine(lines, start);
                break;
            }
        }
        if (first >= lines.Length || ElementComposition.GetElementVisual(this) is not { } host ||
            ElementComposition.GetElementChildVisual(this) != state.Visual)
        {
            ClearStreamingReveal();
            return;
        }

        var snapshot = CreateRevealSnapshot(first, origin, state.Spans, mode, fadeDuration, riseDuration, riseOffset);
        if (snapshot is null)
        {
            ClearStreamingReveal();
            return;
        }

        if (state.Visual is null)
        {
            state.Visual = host.Compositor.CreateCustomVisual(new StreamingTextRevealHandler(OnRevealCompleted));
            ElementComposition.SetElementChildVisual(this, state.Visual);
        }

        state.Layout = TextLayout;
        state.Origin = origin;
        state.Size = Bounds.Size;
        state.Visual.Size = new Vector(Bounds.Width, Bounds.Height);
        state.Visual.SendHandlerMessage(snapshot);
        state.Snapshot = snapshot;
        InvalidateVisual();
    }

    private void OnRevealCompleted(StreamingRevealSnapshot snapshot)
    {
        if (_reveal is { Layout: not null } state && ReferenceEquals(state.Snapshot, snapshot)) ClearStreamingReveal();
    }

    internal void ClearStreamingReveal()
    {
        if (_reveal is not { } state) return;

        _reveal = null;
        LayoutUpdated -= OnRevealLayoutUpdated;
        if (state.Visual is not { } visual) return;
        visual.SendHandlerMessage(StreamingTextRevealHandler.Clear);
        if (ElementComposition.GetElementChildVisual(this) == visual)
        {
            ElementComposition.SetElementChildVisual(this, null);
        }

        InvalidateVisual();
    }
}

using System.Reflection;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.VisualTree;
using LiveMarkdown.Avalonia;

namespace LiveMarkdown.Avalonia.Tests;

/// <summary>
/// Captures the state that participates in Avalonia's pointer/cursor pipeline.
/// This is deliberately test-only: it observes the current implementation without
/// introducing another production event or capture abstraction.
/// </summary>
internal sealed class PointerInteractionTrace
{
    private readonly List<string> entries = [];
    private Window? window;
    private MarkdownRenderer? renderer;
    private MarkdownTextBlock? block;
    private IPointer? pointer;
    private int sequence;

    public void Attach(Window attachedWindow, MarkdownRenderer attachedRenderer, MarkdownTextBlock attachedBlock)
    {
        window = attachedWindow;
        renderer = attachedRenderer;
        block = attachedBlock;

        AddInputHandlers(window, "Window");
        AddInputHandlers(renderer, "Renderer");
        AddInputHandlers(block, "Block");

        renderer.PropertyChanged += OnPropertyChanged;
        block.PropertyChanged += OnPropertyChanged;
    }

    public void InstallPlatformCursorTrace(Window topLevel)
    {
        if (topLevel.PlatformImpl is not IWindowImpl originalImplementation)
        {
            throw new InvalidOperationException("The diagnostic window does not expose an IWindowImpl.");
        }

        var recordingImplementation = DispatchProxy.Create<IWindowImpl, CursorRecordingWindowImpl>();
        ((CursorRecordingWindowImpl)(object)recordingImplementation).Initialize(originalImplementation, this);

        var presentationSource = GetFieldValue(topLevel, "_source") ??
                                 throw new InvalidOperationException("Could not locate TopLevel._source.");
        SetBackingField(presentationSource, "PlatformImpl", recordingImplementation);
    }

    public void Mark(string label)
    {
        entries.Add($"{++sequence:000} MARK {label}");
    }

    public void Record(string phase, PointerEventArgs e)
    {
        pointer = e.Pointer;
        var position = TryGetPosition(e);
        var hit = position is { } point ? window?.InputHitTest(point) : null;
        var captured = e.Pointer.Captured;
        var pointerOver = GetInputRootValue("PointerOverElement");
        var cursorElement = GetInputRootValue("CursorElement");
        var presentationCursor = GetPresentationCursor();
        var clickCount = e is PointerPressedEventArgs pressed ? pressed.ClickCount.ToString() : "-";

        entries.Add(
            $"{++sequence:000} {phase} " +
            $"handled={e.Handled} click={clickCount} " +
            $"pos={FormatPoint(position)} hit={DescribeElement(hit)} " +
            $"captured={DescribeElement(captured)} " +
            $"captureSource={DescribeCaptureSource(e.Pointer)} " +
            $"pointerOver={DescribeElement(pointerOver)} " +
            $"cursorElement={DescribeElement(cursorElement)} " +
            $"presentationCursor={DescribeCursor(presentationCursor)} " +
            $"rendererCursor={DescribeCursor(renderer?.Cursor)} " +
            $"blockCursor={DescribeCursor(block?.Cursor)} " +
            $"pseudo={DescribePseudoClasses()}");
    }

    public void RecordCaptureLost(string phase, PointerCaptureLostEventArgs e)
    {
        pointer = e.Pointer;
        var captured = e.Pointer.Captured;
        var pointerOver = GetInputRootValue("PointerOverElement");
        var cursorElement = GetInputRootValue("CursorElement");
        var presentationCursor = GetPresentationCursor();

        entries.Add(
            $"{++sequence:000} {phase} " +
            $"captured={DescribeElement(captured)} " +
            $"captureSource={DescribeCaptureSource(e.Pointer)} " +
            $"pointerOver={DescribeElement(pointerOver)} " +
            $"cursorElement={DescribeElement(cursorElement)} " +
            $"presentationCursor={DescribeCursor(presentationCursor)} " +
            $"rendererCursor={DescribeCursor(renderer?.Cursor)} " +
            $"blockCursor={DescribeCursor(block?.Cursor)} " +
            $"pseudo={DescribePseudoClasses()}");
    }

    public void RecordPropertyChanged(string ownerName, AvaloniaObject owner, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != InputElement.CursorProperty)
        {
            return;
        }

        entries.Add(
            $"{++sequence:000} {ownerName}.CursorChanged " +
            $"old={DescribeCursor(e.OldValue)} new={DescribeCursor(e.NewValue)} " +
            $"captured={DescribeElement(GetCapturedPointer()?.Captured)} " +
            $"captureSource={DescribeCaptureSource(GetCapturedPointer())} " +
            $"cursorElement={DescribeElement(GetInputRootValue("CursorElement"))} " +
            $"presentationCursor={DescribeCursor(GetPresentationCursor())} " +
            $"owner={owner.GetType().Name}");
    }

    public void RecordPlatformCursor(object? platformCursor)
    {
        var stack = string.Join(
            "<-",
            new StackTrace(1)
                .GetFrames()?
                .Select(frame => frame.GetMethod())
                .Where(method => method is not null)
                .Select(method => $"{method!.DeclaringType?.Name}.{method.Name}")
                .Where(name => !name.Contains(nameof(CursorRecordingWindowImpl), StringComparison.Ordinal))
                .Take(6) ?? []);

        entries.Add(
            $"{++sequence:000} Platform.SetCursor " +
            $"cursor={DescribeCursor(GetPresentationCursor())} " +
            $"impl={platformCursor?.GetType().Name ?? "null"} " +
            $"stack={stack} " +
            $"captured={DescribeElement(pointer?.Captured)} " +
            $"captureSource={DescribeCaptureSource(pointer)} " +
            $"rendererCursor={DescribeCursor(renderer?.Cursor)} " +
            $"blockCursor={DescribeCursor(block?.Cursor)}");
    }

    public override string ToString() => string.Join(Environment.NewLine, entries);

    private void AddInputHandlers(InputElement element, string name)
    {
        element.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => Record($"{name}.Tunnel.Pressed", e),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerPressedEvent,
            (_, e) => Record($"{name}.Bubble.Pressed", e),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, e) => Record($"{name}.Tunnel.Released", e),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerReleasedEvent,
            (_, e) => Record($"{name}.Bubble.Released", e),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerMovedEvent,
            (_, e) => Record($"{name}.Tunnel.Moved", e),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerMovedEvent,
            (_, e) => Record($"{name}.Bubble.Moved", e),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerCaptureLostEvent,
            (_, e) => RecordCaptureLost($"{name}.Tunnel.CaptureLost", e),
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
        element.AddHandler(
            InputElement.PointerCaptureLostEvent,
            (_, e) => RecordCaptureLost($"{name}.Bubble.CaptureLost", e),
            RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    private void OnPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is MarkdownRenderer)
        {
            RecordPropertyChanged("Renderer", (AvaloniaObject)sender, e);
        }
        else if (sender is MarkdownTextBlock)
        {
            RecordPropertyChanged("Block", (AvaloniaObject)sender, e);
        }
    }

    private Point? TryGetPosition(PointerEventArgs e)
    {
        if (window is null)
        {
            return null;
        }

        try
        {
            return e.GetPosition(window);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private IPointer? GetCapturedPointer()
    {
        return pointer;
    }

    private object? GetInputRootValue(string name)
    {
        try
        {
            var source = window?.GetPresentationSource();
            var inputRoot = GetNamedProperty(source, "InputRoot") ??
                            GetInterfaceProperty(source, typeof(IPresentationSource), "InputRoot") ??
                            GetNamedProperty(window, "InputRoot");

            return GetNamedProperty(inputRoot, name) ??
                   GetInterfaceProperty(inputRoot, typeof(IInputRoot), name) ??
                   GetBackingField(inputRoot, name) ??
                   GetInterfaceProperty(source, typeof(IInputRoot), name) ??
                   GetNamedProperty(source, name) ??
                   GetBackingField(source, name) ??
                   GetNamedProperty(window, name) ??
                   GetBackingField(window, name);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private object? GetPresentationCursor()
    {
        try
        {
            var source = window?.GetPresentationSource();
            if (source is null)
            {
                return null;
            }

            var field = source.GetType().GetField(
                "_cursor",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return field?.GetValue(source);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? GetNamedProperty(object? target, string name)
    {
        if (target is null)
        {
            return null;
        }

        var property = target.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(property =>
                property.Name.Equals(name, StringComparison.Ordinal) ||
                property.Name.EndsWith($".{name}", StringComparison.Ordinal));

        try
        {
            return property?.GetValue(target);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? GetInterfaceProperty(object? target, Type interfaceType, string name)
    {
        if (target is null || !interfaceType.IsInstanceOfType(target))
        {
            return null;
        }

        var property = interfaceType.GetProperty(
            name,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        try
        {
            return property?.GetValue(target);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? GetBackingField(object? target, string name)
    {
        if (target is null)
        {
            return null;
        }

        var field = target.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(field =>
                field.Name.Contains($"<{name}>", StringComparison.Ordinal) ||
                field.Name.Contains($".{name}>", StringComparison.Ordinal));

        try
        {
            return field?.GetValue(target);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static void SetBackingField(object target, string name, object? value)
    {
        var field = FindField(target.GetType(), name);
        if (field is null)
        {
            throw new InvalidOperationException($"Could not locate backing field for '{name}'.");
        }

        field.SetValue(target, value);
    }

    private static object? GetFieldValue(object target, string name)
    {
        var field = FindField(target.GetType(), name);
        return field?.GetValue(target);
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field is not null)
            {
                return field;
            }

            field = current.GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                .FirstOrDefault(candidate =>
                    candidate.Name.Contains($"<{name}>", StringComparison.Ordinal) &&
                    candidate.Name.Contains("BackingField", StringComparison.Ordinal));
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private class CursorRecordingWindowImpl : DispatchProxy
    {
        private IWindowImpl? target;
        private PointerInteractionTrace? trace;

        public void Initialize(IWindowImpl targetImplementation, PointerInteractionTrace owner)
        {
            target = targetImplementation;
            trace = owner;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod is null || target is null)
            {
                throw new InvalidOperationException("The cursor recording proxy was not initialized.");
            }

            if (targetMethod.Name.Equals("SetCursor", StringComparison.Ordinal))
            {
                trace?.RecordPlatformCursor(args is { Length: > 0 } ? args[0] : null);
            }

            return targetMethod.Invoke(target, args);
        }
    }

    private static string FormatPoint(Point? point)
    {
        return point is { } value ? $"({value.X:0.##},{value.Y:0.##})" : "-";
    }

    private static string DescribeElement(object? element)
    {
        if (element is null)
        {
            return "null";
        }

        var cursor = element is IInputElement inputElement ? DescribeCursor(inputElement.Cursor) : "-";
        return $"{element.GetType().Name}[{cursor}]";
    }

    private static string DescribeCursor(object? cursor)
    {
        return cursor?.ToString() ?? "null";
    }

    private static string DescribeCaptureSource(IPointer? currentPointer)
    {
        if (currentPointer is null)
        {
            return "null";
        }

        var source = GetNamedProperty(currentPointer, "CaptureSource") ??
                     GetBackingField(currentPointer, "CaptureSource");
        return source?.ToString() ?? "unknown";
    }

    private string DescribePseudoClasses()
    {
        var rendererSelecting = renderer?.Classes.Contains(":selecting") == true;
        var rendererPending = renderer?.Classes.Contains(":link-pending") == true;
        var blockLink = block?.Classes.Contains(":pointerover-link") == true;
        return $"renderer(selecting={rendererSelecting},pending={rendererPending}),block(link={blockLink})";
    }
}

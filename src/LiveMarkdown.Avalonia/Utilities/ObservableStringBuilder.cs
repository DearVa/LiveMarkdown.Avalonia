using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Event arguments for changes in the ObservableStringBuilder.
/// </summary>
/// <param name="StartIndex">The starting index of the change.</param>
/// <param name="Length">The length of the change.</param>
/// <param name="NewLength">The length of the string builder after the change.</param>
/// <param name="Version">The version of the string builder after the change.</param>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly record struct ObservableStringBuilderChangedEventArgs(int StartIndex, int Length, int NewLength, long Version)
{
    private string DebuggerDisplay
    {
        get
        {
            var endIndex = StartIndex + Length;
            return $"[{StartIndex}..{endIndex}] length={NewLength} version={Version}";
        }
    }
}

/// <summary>
/// Represents an immutable text snapshot and the content version that produced it.
/// </summary>
/// <param name="Text">The complete committed text.</param>
/// <param name="Version">The content version associated with <paramref name="Text"/>.</param>
public readonly record struct ObservableStringBuilderSnapshot(string Text, long Version);

/// <summary>
/// A delegate for handling changes in the ObservableStringBuilder.
/// </summary>
public delegate void ObservableStringBuilderChangedEventHandler(in ObservableStringBuilderChangedEventArgs e);

/// <summary>
/// A string builder that raises events when its content changes.
/// </summary>
/// <remarks>
/// This is **not** thread-safe!
/// If used for <see cref="MarkdownRenderer"/>, you must ensure that all changes are made on the UI thread.
/// </remarks>
public class ObservableStringBuilder : INotifyPropertyChanged
{
    /// <summary>
    /// Raised when a property changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Gets the current length of the string builder.
    /// </summary>
    public int Length { get; private set; }

    /// <summary>
    /// Gets the content version. It increments once for each actual text change.
    /// </summary>
    public long Version { get; private set; }

    /// <summary>
    /// Raised when the content of the string builder changes.
    /// </summary>
    public event ObservableStringBuilderChangedEventHandler? Changed;

    private readonly StringBuilder stringBuilder;

    /// <summary>
    /// Initializes an empty string builder.
    /// </summary>
    public ObservableStringBuilder()
    {
        stringBuilder = new StringBuilder();
    }

    /// <summary>
    /// Initializes an empty string builder with the specified initial capacity.
    /// </summary>
    /// <param name="capacity">The initial capacity.</param>
    public ObservableStringBuilder(int capacity)
    {
        stringBuilder = new StringBuilder(capacity);
    }

    /// <summary>
    /// Initializes a string builder with optional initial content.
    /// </summary>
    /// <param name="initialValue">The initial content, or <see langword="null"/>.</param>
    public ObservableStringBuilder(string? initialValue) : this(initialValue?.Length ?? 0)
    {
        if (!string.IsNullOrEmpty(initialValue))
        {
            stringBuilder.Append(initialValue);
            Length = stringBuilder.Length;
        }
    }

    /// <summary>
    /// Appends a string to the string builder and raises an event.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public ObservableStringBuilder Append(string? value)
    {
        if (string.IsNullOrEmpty(value)) return this;
        var startIndex = stringBuilder.Length;
        stringBuilder.Append(value);
        UpdateState();
        Changed?.Invoke(
            new ObservableStringBuilderChangedEventArgs(
                // ReSharper disable once RedundantSuppressNullableWarningExpression
                startIndex,
                value!.Length,
                Length,
                Version));
        return this;
    }

    /// <summary>
    /// Appends a string followed by a newline to the string builder and raises an event.
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    public ObservableStringBuilder AppendLine(string? value = null)
    {
        if (string.IsNullOrEmpty(value)) return this;
        var startIndex = stringBuilder.Length;
        stringBuilder.AppendLine(value);
        UpdateState();
        Changed?.Invoke(
            new ObservableStringBuilderChangedEventArgs(
                // ReSharper disable once RedundantSuppressNullableWarningExpression
                startIndex,
                value!.Length + Environment.NewLine.Length,
                Length,
                Version));
        return this;
    }

    /// <summary>
    /// Clears the string builder and raises an event with the previous content.
    /// </summary>
    /// <returns></returns>
    public ObservableStringBuilder Clear()
    {
        var length = stringBuilder.Length;
        if (length == 0) return this;
        stringBuilder.Clear();
        UpdateState();
        Changed?.Invoke(
            new ObservableStringBuilderChangedEventArgs(
                0,
                length,
                Length,
                Version));
        return this;
    }

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString()
    {
        return stringBuilder.ToString();
    }

    /// <summary>
    /// Captures the text committed through this builder together with its matching version.
    /// </summary>
    /// <remarks>
    /// This method intentionally reads the base builder directly instead of dispatching through
    /// <see cref="ToString"/>. Derived types may expose a producer-side view from that virtual
    /// method, while rendering and indexing need the text that has already raised
    /// <see cref="Changed"/> on the owning thread.
    /// </remarks>
    public ObservableStringBuilderSnapshot CaptureSnapshot() => new(stringBuilder.ToString(), Version);

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event for a property.
    /// </summary>
    /// <param name="propertyName">The changed property name.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void UpdateState()
    {
        Version++;
        OnPropertyChanged(nameof(Version));

        if (Length == stringBuilder.Length) return;

        Length = stringBuilder.Length;
        OnPropertyChanged(nameof(Length));
    }
}

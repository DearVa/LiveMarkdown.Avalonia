namespace LiveMarkdown.Avalonia;

/// <summary>
/// Provides helpers for appending observable and asynchronous text to an <see cref="ObservableStringBuilder"/>.
/// </summary>
public static class ObservableStringBuilderExtensions
{
    extension(ObservableStringBuilder builder)
    {
        /// <summary>
        /// Appends each value emitted by an observable source.
        /// </summary>
        /// <param name="source">The source whose values are appended.</param>
        /// <returns>A disposable subscription that stops the updates when disposed.</returns>
        public IDisposable SubscribeAppend(IObservable<string> source)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(source);

            var observer = new AnonymousObserver<string>(
                onNext: value => builder.Append(value),
                onError: ex => throw ex,
                onCompleted: () => { });

            return source.Subscribe(observer);
        }

        /// <summary>
        /// Appends each value emitted by an observable source followed by a line break.
        /// </summary>
        /// <param name="source">The source whose values are appended.</param>
        /// <returns>A disposable subscription that stops the updates when disposed.</returns>
        public IDisposable SubscribeAppendLine(IObservable<string> source)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(source);

            var observer = new AnonymousObserver<string>(
                onNext: value => builder.AppendLine(value),
                onError: ex => throw ex,
                onCompleted: () => { });

            return source.Subscribe(observer);
        }

        /// <summary>
        /// Enumerates an asynchronous source and appends each value as it arrives.
        /// </summary>
        /// <param name="asyncEnumerable">The asynchronous source to enumerate.</param>
        /// <param name="timeSpan">An optional delay after appending each value.</param>
        /// <param name="cancellationToken">A token that cancels enumeration.</param>
        /// <returns>A task that completes when enumeration finishes.</returns>
        public async Task EnumerateAppendAsync(
            IAsyncEnumerable<string> asyncEnumerable,
            TimeSpan? timeSpan = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(asyncEnumerable);

            await foreach (var line in asyncEnumerable.WithCancellation(cancellationToken))
            {
                builder.Append(line);
                if (timeSpan.HasValue)
                {
                    await Task.Delay(timeSpan.Value, cancellationToken);
                }
            }
        }

        /// <summary>
        /// Enumerates an asynchronous source and appends each value followed by a line break.
        /// </summary>
        /// <param name="asyncEnumerable">The asynchronous source to enumerate.</param>
        /// <param name="timeSpan">An optional delay after appending each value.</param>
        /// <param name="cancellationToken">A token that cancels enumeration.</param>
        /// <returns>A task that completes when enumeration finishes.</returns>
        public async Task EnumerateAppendLineAsync(
            IAsyncEnumerable<string> asyncEnumerable,
            TimeSpan? timeSpan = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(builder);
            ArgumentNullException.ThrowIfNull(asyncEnumerable);

            await foreach (var line in asyncEnumerable.WithCancellation(cancellationToken))
            {
                builder.AppendLine(line);
                if (timeSpan.HasValue)
                {
                    await Task.Delay(timeSpan.Value, cancellationToken);
                }
            }
        }
    }

    private sealed class AnonymousObserver<T>(Action<T>? onNext, Action<Exception>? onError, Action? onCompleted) : IObserver<T>
    {
        private bool _isCompleted;

        public void OnNext(T value)
        {
            if (!_isCompleted)
                onNext?.Invoke(value);
        }

        public void OnError(Exception error)
        {
            if (_isCompleted) return;
            _isCompleted = true;
            onError?.Invoke(error);
        }

        public void OnCompleted()
        {
            if (_isCompleted) return;
            _isCompleted = true;
            onCompleted?.Invoke();
        }
    }
}

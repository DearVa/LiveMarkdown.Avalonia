namespace LiveMarkdown.Avalonia.Demo;

internal static class TaskExtensions
{
    /// <summary>
    /// Observes UI-started background tasks whose callers cannot await them, such as a property setter.
    /// </summary>
    public static async void Detach(this Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            await Console.Error.WriteLineAsync(exception.ToString());
        }
    }
}

using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Markdig.Extensions.TaskLists;

namespace LiveMarkdown.Avalonia;

/// <summary>
/// Renders a Markdig task-list item as an inline checkbox.
/// </summary>
public class TaskListNode : InlineNode<TaskList>
{
    /// <summary>
    /// Gets the Avalonia inline container holding the task checkbox.
    /// </summary>
    public override Inline Inline { get; }

    private readonly CheckBox checkBox;

    /// <summary>
    /// Initializes a new task-list inline node.
    /// </summary>
    public TaskListNode()
    {
        Inline = new InlineUIContainer
        {
            Classes = { "TaskList" },
            Child = checkBox = new CheckBox
            {
                Classes = { "TaskList" }
            }
        };
    }

    /// <summary>
    /// Applies the Markdig checked state to the rendered checkbox.
    /// </summary>
    /// <param name="documentNode">The owning document node.</param>
    /// <param name="taskList">The Markdig task-list inline.</param>
    /// <param name="change">The source change being applied.</param>
    /// <param name="cancellationToken">The token used to cancel the update.</param>
    /// <returns><see langword="true"/> when the task-list item remains valid.</returns>
    protected override bool UpdateCore(
        DocumentNode documentNode,
        TaskList taskList,
        in ObservableStringBuilderChangedEventArgs change,
        CancellationToken cancellationToken)
    {
        checkBox.IsChecked = taskList.Checked;
        return true;
    }
}
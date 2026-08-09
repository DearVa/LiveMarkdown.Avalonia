using NUnit.Framework;

namespace LiveMarkdown.Avalonia.Tests;

[TestFixture]
public class ObservableStringBuilderTests
{
    [Test]
    public void CaptureSnapshot_DerivedToStringViewDoesNotChangeCommittedTextOrVersion()
    {
        var builder = new ProducerViewObservableStringBuilder("committed", "producer-ahead");

        var snapshot = builder.CaptureSnapshot();

        Assert.Multiple(() =>
        {
            Assert.That(builder.ToString(), Is.EqualTo("producer-ahead"));
            Assert.That(snapshot.Text, Is.EqualTo("committed"));
            Assert.That(snapshot.Version, Is.Zero);
        });
    }

    [Test]
    public void Append_RaisesRangeLengthAndVersionWithoutSnapshotText()
    {
        var builder = new ObservableStringBuilder("abc");
        ObservableStringBuilderChangedEventArgs? change = null;
        builder.Changed += (in ObservableStringBuilderChangedEventArgs e) => change = e;

        builder.Append("def");

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.Not.Null);
            Assert.That(change!.Value.StartIndex, Is.EqualTo(3));
            Assert.That(change.Value.Length, Is.EqualTo(3));
            Assert.That(change.Value.NewLength, Is.EqualTo(6));
            Assert.That(change.Value.Version, Is.EqualTo(1));
            Assert.That(builder.Version, Is.EqualTo(1));
        });
    }

    [Test]
    public void AppendLine_IncludesLineBreakInChangedLength()
    {
        var builder = new ObservableStringBuilder("abc");
        ObservableStringBuilderChangedEventArgs? change = null;
        builder.Changed += (in ObservableStringBuilderChangedEventArgs e) => change = e;

        builder.AppendLine("def");

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.Not.Null);
            Assert.That(change!.Value.StartIndex, Is.EqualTo(3));
            Assert.That(change.Value.Length, Is.EqualTo(3 + Environment.NewLine.Length));
            Assert.That(change.Value.NewLength, Is.EqualTo(builder.Length));
            Assert.That(change.Value.Version, Is.EqualTo(1));
        });
    }

    [Test]
    public void Clear_RaisesRangeForRemovedText()
    {
        var builder = new ObservableStringBuilder("abcdef");
        ObservableStringBuilderChangedEventArgs? change = null;
        builder.Changed += (in ObservableStringBuilderChangedEventArgs e) => change = e;

        builder.Clear();

        Assert.Multiple(() =>
        {
            Assert.That(change, Is.Not.Null);
            Assert.That(change!.Value.StartIndex, Is.EqualTo(0));
            Assert.That(change.Value.Length, Is.EqualTo(6));
            Assert.That(change.Value.NewLength, Is.EqualTo(0));
            Assert.That(change.Value.Version, Is.EqualTo(1));
            Assert.That(builder.Version, Is.EqualTo(1));
        });
    }

    private sealed class ProducerViewObservableStringBuilder(string committed, string producerView)
        : ObservableStringBuilder(committed)
    {
        public override string ToString() => producerView;
    }
}

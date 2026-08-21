using Avalonia.Headless;

namespace LiveMarkdown.Avalonia.Tests;

/// <summary>
/// ONE headless session for the whole test assembly.
///
/// <para>Avalonia's default isolation is <see cref="AvaloniaTestIsolationLevel.PerTest"/>, and its
/// <c>DispatchCore</c> documentation is explicit that a dispatch "creates a new application instance,
/// setting app avalonia services". Starting a session per test — <c>StartNew</c> in <c>[SetUp]</c>,
/// <c>Dispose</c> in <c>[TearDown]</c> — therefore runs Avalonia's whole application construction path
/// once per test, and that path (ServerCompositor hooking the render loop) intermittently dies with
/// <c>InvalidOperationException: The calling thread cannot access this object because a different thread
/// owns it</c>, thrown from inside Avalonia's own render, not from any test body.</para>
///
/// <para>Measured here: the full suite failed 4 runs in 6, always in
/// <c>CaptureTimeline_LinkClick_AndDragStartingOnLink</c>; that same test run ALONE passed 6 in 6, and its
/// whole fixture alone passed 4 in 4. So it was never that test — it was the number of times the
/// application had been rebuilt before it.</para>
///
/// <para>Building once removes those re-runs. The trade, per Avalonia's own remarks on
/// <see cref="AvaloniaTestIsolationLevel.PerAssembly"/>: state leaks between tests, so a fixture must undo
/// anything global it sets, and windows a test opens stay open unless it closes them.</para>
/// </summary>
internal static class HeadlessSession
{
    private static readonly Lazy<HeadlessUnitTestSession> Instance =
        new(() => HeadlessUnitTestSession.StartNew(
            typeof(MarkdownPointerSelectionTests.StyledTestApplication),
            AvaloniaTestIsolationLevel.PerAssembly));

    /// <summary>The shared session. Deliberately never disposed: disposing it tears down the application
    /// that every remaining test in the assembly is about to dispatch onto.</summary>
    public static HeadlessUnitTestSession Current => Instance.Value;
}

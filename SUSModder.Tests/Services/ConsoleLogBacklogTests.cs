using SUSModder.Services;
using SUSModder.Views;
using System.Diagnostics;
using Xunit;

namespace SUSModder.Tests.Services;

// Testy ścieżki bufora historii przy ZAMKRYTYM oknie konsoli.
// Nie tworzymy ConsoleWindow (konstruktor wymaga Avalonii) — WriteLog przy
// _instance == null nie dotyka Dispatchera ani UI. Stan statyczny sprzątany
// przed każdym testem (xunit tworzy nową instancję klasy per test).
public sealed class ConsoleLogBacklogTests
{
    public ConsoleLogBacklogTests() => ConsoleWindow.ClearBacklog();

    [Fact]
    public void ClosedWindowBuffersEntryWithLevelAndTimestamp()
    {
        ConsoleWindow.WriteLog("hello", LogLevel.Warning);

        var snapshot = ConsoleWindow.GetBacklogSnapshot();
        Assert.Single(snapshot);
        Assert.Equal(LogLevel.Warning, snapshot[0].Level);
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] hello$", snapshot[0].Text);
    }

    [Fact]
    public void DefaultLevelIsInfo()
    {
        ConsoleWindow.WriteLog("plain message");

        var snapshot = ConsoleWindow.GetBacklogSnapshot();
        Assert.Single(snapshot);
        Assert.Equal(LogLevel.Info, snapshot[0].Level);
    }

    [Fact]
    public void BacklogKeepsOnlyNewestThousandEntries()
    {
        for (var i = 0; i < 1050; i++)
        {
            ConsoleWindow.WriteLog($"msg-{i}");
        }

        var snapshot = ConsoleWindow.GetBacklogSnapshot();
        Assert.Equal(1000, snapshot.Length);
        Assert.EndsWith("msg-50", snapshot[0].Text);
        Assert.EndsWith("msg-1049", snapshot[^1].Text);
    }

    [Fact]
    public void ConcurrentWritesBelowLimitLoseNoEntries()
    {
        const int threads = 4;
        const int perThread = 100;

        Parallel.For(0, threads, t =>
        {
            for (var i = 0; i < perThread; i++)
            {
                ConsoleWindow.WriteLog($"t{t}-{i}");
            }
        });

        var snapshot = ConsoleWindow.GetBacklogSnapshot();
        Assert.Equal(threads * perThread, snapshot.Length);
        Assert.Equal(threads * perThread, snapshot.Select(e => e.Text).Distinct().Count());
    }

    [Fact]
    public void ConcurrentWritesAboveLimitTrimToThousandWithoutDuplicates()
    {
        const int threads = 8;
        const int perThread = 200;

        Parallel.For(0, threads, t =>
        {
            for (var i = 0; i < perThread; i++)
            {
                ConsoleWindow.WriteLog($"t{t}-{i}");
            }
        });

        var snapshot = ConsoleWindow.GetBacklogSnapshot();
        Assert.Equal(1000, snapshot.Length);
        Assert.Equal(1000, snapshot.Select(e => e.Text).Distinct().Count());
        Assert.All(snapshot, e => Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] t\d-\d+$", e.Text));
    }

    [Fact]
    public void ClearBacklogRemovesAllEntries()
    {
        ConsoleWindow.WriteLog("a");
        ConsoleWindow.WriteLog("b");
        ConsoleWindow.WriteLog("c");

        ConsoleWindow.ClearBacklog();

        Assert.Empty(ConsoleWindow.GetBacklogSnapshot());
    }

#if DEBUG
    // Regresja routingu: Debug.WriteLine omija Trace.Listeners przy domyślnym
    // providerze, ale pierwsze użycie Trace.Listeners instaluje TraceProvider,
    // który kieruje Debug.WriteLine do listenerów (zweryfikowane na .NET 10).
    // Tylko Debug: Debug.WriteLine ma [Conditional("DEBUG")] i w Release znika.
    [Fact]
    public void DebugWriteLineReachesBacklogThroughTraceListener()
    {
        var listener = new DebugTraceListener();
        Trace.Listeners.Add(listener);
        try
        {
            Debug.WriteLine("routing-probe");

            Assert.Contains(ConsoleWindow.GetBacklogSnapshot(),
                e => e.Level == LogLevel.Debug && e.Text.EndsWith("routing-probe"));
        }
        finally
        {
            Trace.Listeners.Remove(listener);
        }
    }
#endif
}
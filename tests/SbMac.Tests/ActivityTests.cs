using SbMac.App.ViewModels;
using SbMac.App.ViewModels.Tree;

using Xunit;

namespace SbMac.Tests;

public class OperationViewModelTests
{
    [Fact]
    public void ProgressStartsIndeterminateAndFillsOnceATotalIsKnown()
    {
        var operation = new OperationViewModel(OperationKind.Purge, "Purging orders…");

        Assert.True(operation.IsIndeterminate);

        operation.Report("5,000 deleted", 5_000, 10_000);

        Assert.False(operation.IsIndeterminate);
        Assert.Equal(50, operation.Progress);
        Assert.Equal("5,000 deleted", operation.Detail);
    }

    /// <summary>
    /// A live entity keeps taking messages while it drains, so the count can pass the
    /// estimate the bar was sized from. Overshooting the bar would look broken.
    /// </summary>
    [Fact]
    public void ProgressIsCappedWhenTheWorkOverrunsTheEstimate()
    {
        var operation = new OperationViewModel(OperationKind.Purge, "Purging orders…");

        operation.Report("14,000 deleted", 14_000, 10_000);

        Assert.Equal(100, operation.Progress);
    }

    [Fact]
    public void AnUnknownTotalLeavesTheBarSweeping()
    {
        var operation = new OperationViewModel(OperationKind.Purge, "Purging orders…");

        operation.Report("300 deleted", 300, null);

        Assert.True(operation.IsIndeterminate);
        Assert.Equal("300 deleted", operation.Detail);
    }

    [Fact]
    public void FinishingSettlesTheStateAndFillsTheBar()
    {
        var operation = new OperationViewModel(OperationKind.Read, "Peeking 100 message(s)…");

        operation.Finish(OperationState.Completed);

        Assert.False(operation.IsRunning);
        Assert.True(operation.IsFinished);
        Assert.Equal(100, operation.Progress);
        Assert.False(operation.IsIndeterminate);
        Assert.Equal("Done", operation.StateLabel);
    }

    [Fact]
    public void EachKindKeepsItsOwnColourWhileRunning()
    {
        var peek = new OperationViewModel(OperationKind.Read, "Peeking…");
        var purge = new OperationViewModel(OperationKind.Purge, "Purging…");

        Assert.NotEqual(peek.AccentKey, purge.AccentKey);
    }

    /// <summary>The outcome matters more than the action, so a failure takes over the colour.</summary>
    [Fact]
    public void AFailureOverridesTheKindColour()
    {
        var operation = new OperationViewModel(OperationKind.Read, "Peeking…");

        operation.Finish(OperationState.Failed, "The messaging entity could not be found.");

        Assert.Equal("DangerBrush", operation.AccentKey);
        Assert.Equal("Failed", operation.StateLabel);
        Assert.Equal("The messaging entity could not be found.", operation.Detail);
    }

    [Fact]
    public void CancellingSignalsTheOperationsOwnToken()
    {
        var operation = new OperationViewModel(OperationKind.Purge, "Purging orders…");

        operation.Cancel();

        Assert.True(operation.Token.IsCancellationRequested);
    }

    /// <summary>
    /// The token source is released once the operation settles, so a stale Cancel — a click
    /// landing as the work finishes — must not reach a disposed source.
    /// </summary>
    [Fact]
    public void CancellingAfterTheOperationHasFinishedIsIgnored()
    {
        var operation = new OperationViewModel(OperationKind.Send, "Sending…");
        operation.Finish(OperationState.Completed);

        operation.Cancel();

        Assert.False(operation.CanCancel);
        Assert.Equal(OperationState.Completed, operation.State);
    }

    [Fact]
    public void CancellingClearsTheInProgressTextRatherThanKeepingIt()
    {
        var operation = new OperationViewModel(OperationKind.Purge, "Purging orders…");
        operation.Report("900 deleted", 900, 5_000);

        operation.Cancel();
        operation.Finish(OperationState.Cancelled);

        Assert.Null(operation.Detail);
        Assert.Equal("Cancelled", operation.StateLabel);
    }
}

public class ActivityTrayTests
{
    [Fact]
    public void TheWindowIsBusyWhileAnyOperationIsRunning()
    {
        var viewModel = new MainWindowViewModel();
        var running = new OperationViewModel(OperationKind.Refresh, "Refreshing contoso…");

        viewModel.Operations.Add(running);
        Assert.True(viewModel.IsBusy);

        running.Finish(OperationState.Completed);
        Assert.False(viewModel.IsBusy);
    }

    /// <summary>
    /// One operation names itself; several are counted, because the names won't fit and the
    /// coloured bars beside them already say which is which.
    /// </summary>
    [Fact]
    public void TheSummaryNamesASingleOperationAndCountsSeveral()
    {
        var viewModel = new MainWindowViewModel();

        viewModel.Operations.Add(new OperationViewModel(OperationKind.Read, "Peeking 100 message(s)…"));
        Assert.Equal("Peeking 100 message(s)…", viewModel.ActivitySummary);

        viewModel.Operations.Add(new OperationViewModel(OperationKind.Purge, "Purging orders…"));
        Assert.Equal("2 operations running", viewModel.ActivitySummary);
    }

    [Fact]
    public void ClearingFinishedLeavesWhatIsStillRunning()
    {
        var viewModel = new MainWindowViewModel();

        var done = new OperationViewModel(OperationKind.Send, "Sending 1 message(s)…");
        done.Finish(OperationState.Completed);

        var running = new OperationViewModel(OperationKind.Purge, "Purging orders…");

        viewModel.Operations.Add(done);
        viewModel.Operations.Add(running);

        Assert.True(viewModel.HasFinishedOperations);

        viewModel.ClearFinishedOperationsCommand.Execute(null);

        Assert.Same(running, Assert.Single(viewModel.Operations));
        Assert.False(viewModel.HasFinishedOperations);
    }

    [Fact]
    public void CancelAllStopsEveryRunningOperation()
    {
        var viewModel = new MainWindowViewModel();

        var first = new OperationViewModel(OperationKind.Purge, "Purging orders…");
        var second = new OperationViewModel(OperationKind.Read, "Peeking 100 message(s)…");

        viewModel.Operations.Add(first);
        viewModel.Operations.Add(second);

        viewModel.CancelAllOperationsCommand.Execute(null);

        Assert.True(first.Token.IsCancellationRequested);
        Assert.True(second.Token.IsCancellationRequested);
    }
}

/// <summary>
/// Loading a namespace's children one after another is what made a tree with many topics
/// take so long to appear, and it is why nothing else could be done in the meantime.
/// </summary>
public class TreeRefreshConcurrencyTests
{
    [Fact]
    public async Task ChildrenAreRefreshedTogetherRatherThanOneAtATime()
    {
        var meter = new ConcurrencyMeter();
        var nodes = Enumerable.Range(0, 6).Select(_ => new SlowNode(meter)).ToList();

        await ConcurrencyProbe.RefreshAll(nodes, CancellationToken.None, maxConcurrency: 6);

        Assert.All(nodes, node => Assert.True(node.WasRefreshed));
        Assert.True(meter.Peak > 1, $"expected overlapping refreshes, saw a peak of {meter.Peak}");
    }

    [Fact]
    public async Task ConcurrencyIsBoundedSoALargeNamespaceDoesNotFloodTheService()
    {
        var meter = new ConcurrencyMeter();
        var nodes = Enumerable.Range(0, 12).Select(_ => new SlowNode(meter)).ToList();

        await ConcurrencyProbe.RefreshAll(nodes, CancellationToken.None, maxConcurrency: 3);

        Assert.Equal(12, nodes.Count(node => node.WasRefreshed));
        Assert.True(meter.Peak <= 3, $"expected at most 3 at once, saw {meter.Peak}");
    }

    [Fact]
    public async Task CancellingStopsTheRemainingChildren()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var meter = new ConcurrencyMeter();
        var nodes = Enumerable.Range(0, 4).Select(_ => new SlowNode(meter)).ToList();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ConcurrencyProbe.RefreshAll(nodes, cancellation.Token, maxConcurrency: 1));

        Assert.DoesNotContain(nodes, node => node.WasRefreshed);
    }

    /// <summary>Exists only to reach the protected helper under test.</summary>
    sealed class ConcurrencyProbe : TreeNodeViewModel
    {
        public static Task RefreshAll(
            IReadOnlyList<TreeNodeViewModel> nodes,
            CancellationToken cancellationToken,
            int maxConcurrency) =>
            RefreshAllAsync(nodes, cancellationToken, maxConcurrency);
    }

    /// <summary>Records how many refreshes were in flight at the same moment.</summary>
    sealed class ConcurrencyMeter
    {
        int inFlight;
        int peak;

        public int Peak => Volatile.Read(ref peak);

        public void Enter()
        {
            var now = Interlocked.Increment(ref inFlight);

            // Continuations can land on the pool, so the high-water mark is raised under a
            // compare-exchange rather than a read-then-write.
            var seen = Volatile.Read(ref peak);
            while (now > seen && Interlocked.CompareExchange(ref peak, now, seen) != seen)
            {
                seen = Volatile.Read(ref peak);
            }
        }

        public void Exit() => Interlocked.Decrement(ref inFlight);
    }

    sealed class SlowNode(ConcurrencyMeter meter) : TreeNodeViewModel
    {
        public bool WasRefreshed { get; private set; }

        public override async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            meter.Enter();
            try
            {
                await Task.Delay(15, cancellationToken).ConfigureAwait(true);
                WasRefreshed = true;
            }
            finally
            {
                meter.Exit();
            }
        }
    }
}

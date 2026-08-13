using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SbMac.App.ViewModels;

/// <summary>
/// What an operation is doing. Each kind keeps its own colour in the activity tray, so a
/// glance at the collapsed bars is enough to tell a purge from a peek.
/// </summary>
public enum OperationKind
{
    Connect,
    Refresh,
    Read,
    Receive,
    Send,
    Purge,
    Delete,
    Manage,
    Transfer
}

public enum OperationState
{
    Running,
    Completed,
    Cancelled,
    Failed
}

/// <summary>
/// One unit of work the window is running. Several can be live at once — loading a
/// namespace's topics no longer blocks peeking a queue that has already loaded — so each
/// carries its own cancellation, progress and outcome rather than sharing a single
/// window-wide busy flag.
/// </summary>
public sealed partial class OperationViewModel : ViewModelBase
{
    readonly CancellationTokenSource cancellation = new();

    public OperationViewModel(OperationKind kind, string title)
    {
        Kind = kind;
        Title = title;
    }

    public OperationKind Kind { get; }

    /// <summary>Stable label for the work — "Purging orders", "Peeking 100 message(s)".</summary>
    public string Title { get; }

    public CancellationToken Token => cancellation.Token;

    /// <summary>Live progress text under the title, e.g. "12,400 deleted". Null while unknown.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    string? detail;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(AccentKey))]
    [NotifyPropertyChangedFor(nameof(StateLabel))]
    [NotifyPropertyChangedFor(nameof(StatusLine))]
    [NotifyPropertyChangedFor(nameof(TrayOpacity))]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    OperationState state;

    /// <summary>Percentage complete. Only meaningful when <see cref="IsIndeterminate"/> is false.</summary>
    [ObservableProperty]
    double progress;

    /// <summary>
    /// True until the operation learns how much work there is. Most operations never do,
    /// which is why the bar starts out sweeping rather than sitting at zero.
    /// </summary>
    [ObservableProperty]
    bool isIndeterminate = true;

    public bool IsRunning => State == OperationState.Running;

    public bool IsFinished => State != OperationState.Running;

    /// <summary>
    /// Theme resource key for this operation's bar. Kind picks the colour while it runs;
    /// a failure overrides it, because the outcome matters more than the action.
    /// </summary>
    public string AccentKey => State switch
    {
        OperationState.Failed => "DangerBrush",
        OperationState.Cancelled => "TextTertiaryBrush",
        _ => Kind switch
        {
            OperationKind.Connect => "OpConnectBrush",
            OperationKind.Refresh => "OpRefreshBrush",
            OperationKind.Read => "OpReadBrush",
            OperationKind.Receive => "OpReceiveBrush",
            OperationKind.Send => "OpSendBrush",
            OperationKind.Purge => "OpPurgeBrush",
            OperationKind.Delete => "OpDeleteBrush",
            OperationKind.Manage => "OpManageBrush",
            _ => "OpTransferBrush"
        }
    };

    public string StateLabel => State switch
    {
        OperationState.Completed => "Done",
        OperationState.Cancelled => "Cancelled",
        OperationState.Failed => "Failed",
        _ => "Running"
    };

    /// <summary>One line for tooltips: what it is, and how it's going.</summary>
    public string StatusLine => Detail is { Length: > 0 } detail
        ? $"{Title} — {detail}"
        : $"{Title} — {StateLabel.ToLowerInvariant()}";

    /// <summary>Finished bars stay in the tray but recede, so the running ones read first.</summary>
    public double TrayOpacity => IsFinished ? 0.45 : 1;

    public bool CanCancel => IsRunning;

    /// <summary>Replaces the progress text without claiming to know how much is left.</summary>
    public void Report(string? text) => Detail = text;

    /// <summary>Reports countable progress, switching the bar from sweeping to filling.</summary>
    public void Report(string text, long completed, long? total)
    {
        Detail = text;

        if (total is > 0)
        {
            IsIndeterminate = false;
            Progress = Math.Clamp(completed * 100d / total.Value, 0, 100);
        }
    }

    /// <summary>Settles the operation on its outcome and releases its cancellation source.</summary>
    public void Finish(OperationState outcome, string? detail = null)
    {
        if (IsFinished)
        {
            return;
        }

        // "Cancelling…" is left over from the click that stopped it; the row's own state
        // label says "Cancelled" from here on.
        Detail = detail ?? (outcome == OperationState.Cancelled ? null : Detail);

        // A finished bar reads as full whatever it was doing; a half-filled bar next to
        // "Done" looks like the operation stopped short.
        IsIndeterminate = false;
        Progress = 100;
        State = outcome;

        cancellation.Dispose();
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    public void Cancel()
    {
        if (!IsRunning)
        {
            return;
        }

        Detail = "Cancelling…";
        cancellation.Cancel();
    }
}

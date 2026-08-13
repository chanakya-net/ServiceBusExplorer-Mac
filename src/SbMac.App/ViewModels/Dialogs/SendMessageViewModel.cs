using System.Collections.ObjectModel;
using System.Text;

using Azure.Messaging.ServiceBus;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using SbMac.Core.Messaging;

namespace SbMac.App.ViewModels.Dialogs;

/// <summary>What the caller wants composed: the destination and an optional message to start from.</summary>
public sealed record SendMessageRequest(string TargetName, MessageRecord? Seed = null, string? SeedDescription = null);

/// <summary>The composed messages, ready to hand to <see cref="MessageService"/>.</summary>
public sealed record SendMessageResult(
    string TargetName,
    IReadOnlyList<ServiceBusMessage> Messages,
    DateTimeOffset? ScheduledEnqueueTime);

/// <summary>One editable application property row.</summary>
public sealed partial class PropertyRowViewModel : ViewModelBase
{
    [ObservableProperty]
    string key = string.Empty;

    [ObservableProperty]
    string value = string.Empty;
}

/// <summary>Backs the compose/send dialog, also used for resubmitting a dead-lettered message.</summary>
public sealed partial class SendMessageViewModel : ViewModelBase
{
    public SendMessageViewModel(SendMessageRequest request)
    {
        TargetName = request.TargetName;
        SeedDescription = request.SeedDescription;

        if (request.Seed is { } seed)
        {
            // Start from the decoded text so an operator can fix the payload that caused
            // the dead-letter before resubmitting it.
            body = seed.BodyView.Text;
            contentType = seed.ContentType ?? string.Empty;
            subject = seed.Subject ?? string.Empty;
            correlationId = seed.CorrelationId ?? string.Empty;
            sessionId = seed.SessionId ?? string.Empty;
            partitionKey = seed.PartitionKey ?? string.Empty;
            replyTo = seed.ReplyTo ?? string.Empty;
            to = seed.To ?? string.Empty;

            foreach (var pair in seed.ApplicationProperties)
            {
                Properties.Add(new PropertyRowViewModel { Key = pair.Key, Value = pair.Value?.ToString() ?? string.Empty });
            }
        }
    }

    public string TargetName { get; }

    /// <summary>Shown at the top of the dialog when composing from an existing message.</summary>
    public string? SeedDescription { get; }

    public string DialogTitle => $"Send to {TargetName}";

    public ObservableCollection<PropertyRowViewModel> Properties { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    string body = string.Empty;

    [ObservableProperty]
    string contentType = string.Empty;

    [ObservableProperty]
    string subject = string.Empty;

    [ObservableProperty]
    string correlationId = string.Empty;

    [ObservableProperty]
    string sessionId = string.Empty;

    [ObservableProperty]
    string partitionKey = string.Empty;

    [ObservableProperty]
    string replyTo = string.Empty;

    [ObservableProperty]
    string to = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    int copies = 1;

    /// <summary>
    /// When sending more than one copy, replaces the token <c>{n}</c> in the body with
    /// the copy number — handy for generating distinguishable test traffic.
    /// </summary>
    [ObservableProperty]
    bool numberCopies = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowScheduleFields))]
    bool schedule;

    [ObservableProperty]
    DateTimeOffset scheduledDate = DateTimeOffset.Now;

    [ObservableProperty]
    TimeSpan scheduledTime = DateTimeOffset.Now.TimeOfDay + TimeSpan.FromMinutes(5);

    [ObservableProperty]
    string? validationError;

    public bool ShowScheduleFields => Schedule;

    public bool CanSend => Copies >= 1 && Body.Length > 0;

    [RelayCommand]
    void AddProperty() => Properties.Add(new PropertyRowViewModel());

    [RelayCommand]
    void RemoveProperty(PropertyRowViewModel? row)
    {
        if (row is not null)
        {
            Properties.Remove(row);
        }
    }

    /// <summary>Builds the messages to send. Returns null and sets <see cref="ValidationError"/> on bad input.</summary>
    public SendMessageResult? Build()
    {
        ValidationError = null;

        if (Copies < 1)
        {
            ValidationError = "Number of copies must be at least 1.";
            return null;
        }

        DateTimeOffset? scheduledEnqueueTime = null;
        if (Schedule)
        {
            var when = new DateTimeOffset(ScheduledDate.Date + ScheduledTime, ScheduledDate.Offset);
            if (when <= DateTimeOffset.Now)
            {
                ValidationError = "The scheduled time must be in the future.";
                return null;
            }

            scheduledEnqueueTime = when;
        }

        var messages = new List<ServiceBusMessage>(Copies);

        for (var index = 1; index <= Copies; index++)
        {
            var text = Copies > 1 && NumberCopies
                ? Body.Replace("{n}", index.ToString(), StringComparison.Ordinal)
                : Body;

            var message = new ServiceBusMessage(new BinaryData(Encoding.UTF8.GetBytes(text)))
            {
                ContentType = NullIfBlank(ContentType),
                Subject = NullIfBlank(Subject),
                CorrelationId = NullIfBlank(CorrelationId),
                SessionId = NullIfBlank(SessionId),
                ReplyTo = NullIfBlank(ReplyTo),
                To = NullIfBlank(To)
            };

            // Service Bus derives the partition key from the session id when sessions are
            // in use, and rejects a conflicting explicit value.
            if (message.SessionId is null)
            {
                message.PartitionKey = NullIfBlank(PartitionKey);
            }

            foreach (var property in Properties)
            {
                if (!string.IsNullOrWhiteSpace(property.Key))
                {
                    message.ApplicationProperties[property.Key.Trim()] = property.Value;
                }
            }

            messages.Add(message);
        }

        return new SendMessageResult(TargetName, messages, scheduledEnqueueTime);
    }

    static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

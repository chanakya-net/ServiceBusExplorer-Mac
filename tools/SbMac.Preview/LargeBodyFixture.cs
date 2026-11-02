using System.Text;
using SbMac.App.ViewModels;
using SbMac.Core.Messaging;

namespace SbMac.Preview;

/// <summary>
/// Deterministic large-payload rows: 10,000 rows cycle through a few shared bodies, so no
/// distinct 1 MiB payload is allocated per row. Synthetic data only; no broker access.
/// </summary>
static class LargeBodyFixture
{
    public const int RowCount = 10_000;
    const int SharedBodies = 4;
    const int OverLimitBytes = 1024 * 1024 + 4096;

    /// <summary>Row 0 is a small JSON body, rows 1.. cycle over multi-line over-1-MiB bodies.</summary>
    public static IEnumerable<MessageRowViewModel> Rows(int count = RowCount)
    {
        var bodies = Enumerable.Range(0, SharedBodies).Select(MultiLineJson).ToArray();
        for (var i = 0; i < count; i++)
            yield return Row(i, i == 0 ? BinaryData.FromString("{\"small\": true}") : bodies[i % SharedBodies]);
    }

    /// <summary>One over-1-MiB row whose body is a single very long line (the wrap/find stress case).</summary>
    public static MessageRowViewModel LongLineRow() =>
        Row(1, BinaryData.FromString("{\"k\": \"" + new string('x', OverLimitBytes) + "\"}"));

    static BinaryData MultiLineJson(int variant)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; builder.Length < OverLimitBytes; i++)
            builder.Append(i == 0 ? "" : ",").Append($"\"v{variant}-row{i:D6}-alpha beta gamma {new string('x', 24)}\"");
        return BinaryData.FromString(builder.Append(']').ToString());
    }

    static MessageRowViewModel Row(int index, BinaryData body) => new(new MessageRecord
    {
        SequenceNumber = index + 1, MessageId = $"large-{index + 1}", Subject = "large fixture",
        EnqueuedTime = DateTimeOffset.UnixEpoch, Body = body, SourceEntityPath = "orders-fixture"
    });
}

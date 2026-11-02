using System.Text.Json.Nodes;
using SbMac.App.Models;

namespace SbMac.App.Services;

public sealed partial class AppPreferencesService
{
    static Dictionary<string, IReadOnlyList<MessageColumnState>> ParseMessageTables(JsonNode? node)
    {
        var result = new Dictionary<string, IReadOnlyList<MessageColumnState>>();
        if (node is not JsonObject tables) return AppPreferences.Default.MessageTables;
        foreach (var key in new[] { "serviceBus", "deadLetter", "eventHubs" })
        {
            if (tables[key] is not JsonObject table || table["columns"] is not JsonArray columns) continue;
            var states = new List<MessageColumnState>();
            foreach (var item in columns.OfType<JsonObject>())
                if (item["id"] is JsonValue id && id.TryGetValue<string>(out var name) &&
                    item["visible"] is JsonValue visible && visible.TryGetValue<bool>(out var value))
                    states.Add(new MessageColumnState(name, value,
                        item["displayIndex"] is JsonValue index && index.TryGetValue<int>(out var order) ? order : null,
                        ReadNumber(item, "width")).Normalize());
            result[key] = states;
        }
        return result.Count == 0 ? AppPreferences.Default.MessageTables : result;
    }

    static void WriteMessageTables(JsonObject root, Dictionary<string, IReadOnlyList<MessageColumnState>> values)
    {
        WriteSchema(root);
        var tables = root["messageTables"] as JsonObject;
        foreach (var (key, columns) in values)
        {
            if (columns.Count == 0)
            {
                (tables?[key] as JsonObject)?.Remove("columns");
                continue;
            }
            if (tables is null) root["messageTables"] = tables = [];
            if (tables[key] is not JsonObject table) tables[key] = table = [];
            table["columns"] = MergeColumns(table["columns"] as JsonArray, columns);
        }
    }

    static JsonArray MergeColumns(JsonArray? previous, IReadOnlyList<MessageColumnState> columns)
    {
        var merged = new JsonArray();
        var written = new HashSet<string>();
        foreach (var raw in columns)
        {
            var state = raw.Normalize();
            if (!written.Add(state.Id)) continue;
            var old = previous?.OfType<JsonObject>().FirstOrDefault(c => ReadColumnId(c) == state.Id);
            var item = old?.DeepClone() as JsonObject ?? new JsonObject();
            item["id"] = state.Id;
            item["visible"] = state.Visible;
            if (state.DisplayIndex is { } index) item["displayIndex"] = index;
            else item.Remove("displayIndex");
            if (state.Width is { } width) item["width"] = width;
            else item.Remove("width");
            merged.Add(item);
        }
        foreach (var item in previous ?? [])
            if (item is not JsonObject obj || ReadColumnId(obj) is not { } id || !written.Contains(id))
                merged.Add(item?.DeepClone());
        return merged;
    }

    static string? ReadColumnId(JsonObject column) =>
        column["id"] is JsonValue value && value.TryGetValue<string>(out var id) ? id : null;
}

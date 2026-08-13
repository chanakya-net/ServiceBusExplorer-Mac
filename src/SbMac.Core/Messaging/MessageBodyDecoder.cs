using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace SbMac.Core.Messaging;

/// <summary>How a message body was interpreted for display.</summary>
public enum MessageBodyFormat
{
    Text,
    Json,
    Xml,
    /// <summary>A string written by the old WCF <c>DataContractSerializer</c> with a binary XML writer.</summary>
    WcfDataContract,
    /// <summary>Not decodable as text; shown as a hex dump.</summary>
    Binary,
    Empty
}

/// <summary>The decoded form of a message body, ready to put in a text box.</summary>
public sealed record MessageBodyView(MessageBodyFormat Format, string Text)
{
    /// <summary>A short label for the UI, e.g. "JSON" or "Binary (hex)".</summary>
    public string FormatLabel => Format switch
    {
        MessageBodyFormat.Json => "JSON",
        MessageBodyFormat.Xml => "XML",
        MessageBodyFormat.Text => "Text",
        MessageBodyFormat.WcfDataContract => "Text (WCF DataContract)",
        MessageBodyFormat.Binary => "Binary (hex)",
        MessageBodyFormat.Empty => "Empty",
        _ => "Unknown"
    };
}

/// <summary>
/// Renders a message body as something readable.
/// </summary>
/// <remarks>
/// Bodies written by the old <c>WindowsAzure.ServiceBus</c> SDK — which the Windows
/// Service Bus Explorer used — are wrapped in binary-XML DataContract framing rather
/// than being raw UTF-8. Messages in long-lived queues are often still in that shape,
/// so we unwrap it instead of showing the user a hex dump of their own JSON.
/// </remarks>
public static class MessageBodyDecoder
{
    /// <summary>Bodies larger than this are truncated for display; the full bytes are still on the record.</summary>
    public const int MaxDisplayBytes = 1024 * 1024;

    public static MessageBodyView Decode(BinaryData body)
    {
        var bytes = body.ToMemory().Span;
        if (bytes.Length == 0)
        {
            return new MessageBodyView(MessageBodyFormat.Empty, string.Empty);
        }

        if (TryDecodeWcfDataContractString(bytes, out var unwrapped))
        {
            // The unwrapped payload is usually JSON or XML in its own right.
            var inner = DecodeText(unwrapped);
            return inner.Format == MessageBodyFormat.Text
                ? new MessageBodyView(MessageBodyFormat.WcfDataContract, inner.Text)
                : inner;
        }

        if (!TryDecodeUtf8(bytes, out var text))
        {
            return new MessageBodyView(MessageBodyFormat.Binary, ToHexDump(bytes));
        }

        return DecodeText(text);
    }

    static MessageBodyView DecodeText(string text)
    {
        var trimmed = text.TrimStart();

        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            if (TryPrettyPrintJson(text, out var json))
            {
                return new MessageBodyView(MessageBodyFormat.Json, json);
            }
        }

        if (trimmed.StartsWith('<'))
        {
            if (TryPrettyPrintXml(text, out var xml))
            {
                return new MessageBodyView(MessageBodyFormat.Xml, xml);
            }
        }

        return new MessageBodyView(MessageBodyFormat.Text, text);
    }

    /// <summary>
    /// Decodes strictly, so invalid UTF-8 is reported rather than silently turned into
    /// replacement characters — that's the signal that a body is genuinely binary.
    /// </summary>
    static bool TryDecodeUtf8(ReadOnlySpan<byte> bytes, out string text)
    {
        try
        {
            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            text = encoding.GetString(bytes);

            // Control characters other than tab/newline/carriage return mean this decoded
            // "successfully" but isn't really text.
            foreach (var character in text)
            {
                if (char.IsControl(character) && character is not ('\t' or '\n' or '\r'))
                {
                    text = string.Empty;
                    return false;
                }
            }

            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Unwraps the framing that <c>DataContractSerializer</c> produces for a plain string
    /// when writing with <c>XmlDictionaryWriter.CreateBinaryWriter</c>:
    /// <c>0x40 0x06 "string" &lt;text-node&gt; &lt;length&gt; &lt;utf8&gt;</c>.
    /// </summary>
    static bool TryDecodeWcfDataContractString(ReadOnlySpan<byte> bytes, out string text)
    {
        text = string.Empty;

        // 0x40 = short element node, 0x06 = name length, then the literal name "string".
        ReadOnlySpan<byte> prefix = [0x40, 0x06, (byte)'s', (byte)'t', (byte)'r', (byte)'i', (byte)'n', (byte)'g'];
        if (bytes.Length < prefix.Length + 2 || !bytes[..prefix.Length].SequenceEqual(prefix))
        {
            return false;
        }

        var cursor = prefix.Length;
        var nodeType = bytes[cursor++];

        // 0x98/0x99 = Chars8Text, 0x9A/0x9B = Chars16Text, 0x9C/0x9D = Chars32Text.
        // The odd value of each pair also closes the element; both carry the same payload.
        int length;
        switch (nodeType)
        {
            case 0x98 or 0x99:
                length = bytes[cursor];
                cursor += 1;
                break;

            case 0x9A or 0x9B:
                if (bytes.Length < cursor + 2) return false;
                length = bytes[cursor] | (bytes[cursor + 1] << 8);
                cursor += 2;
                break;

            case 0x9C or 0x9D:
                if (bytes.Length < cursor + 4) return false;
                length = bytes[cursor]
                         | (bytes[cursor + 1] << 8)
                         | (bytes[cursor + 2] << 16)
                         | (bytes[cursor + 3] << 24);
                cursor += 4;
                break;

            default:
                return false;
        }

        if (length < 0 || cursor + length > bytes.Length)
        {
            return false;
        }

        return TryDecodeUtf8(bytes.Slice(cursor, length), out text);
    }

    static bool TryPrettyPrintJson(string text, out string formatted)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
            return true;
        }
        catch (JsonException)
        {
            formatted = string.Empty;
            return false;
        }
    }

    static bool TryPrettyPrintXml(string text, out string formatted)
    {
        try
        {
            formatted = XDocument.Parse(text).ToString();
            return true;
        }
        catch (System.Xml.XmlException)
        {
            formatted = string.Empty;
            return false;
        }
    }

    /// <summary>Classic offset / hex / ASCII dump, 16 bytes to a line.</summary>
    public static string ToHexDump(ReadOnlySpan<byte> bytes)
    {
        const int bytesPerLine = 16;
        var truncated = bytes.Length > MaxDisplayBytes;
        var length = truncated ? MaxDisplayBytes : bytes.Length;

        var builder = new StringBuilder(length * 4);

        for (var offset = 0; offset < length; offset += bytesPerLine)
        {
            var lineLength = Math.Min(bytesPerLine, length - offset);
            builder.Append(offset.ToString("x8")).Append("  ");

            for (var index = 0; index < bytesPerLine; index++)
            {
                builder.Append(index < lineLength ? bytes[offset + index].ToString("x2") : "  ").Append(' ');
                if (index == 7)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(" |");
            for (var index = 0; index < lineLength; index++)
            {
                var value = bytes[offset + index];
                builder.Append(value is >= 0x20 and < 0x7F ? (char)value : '.');
            }

            builder.Append('|').Append('\n');
        }

        if (truncated)
        {
            builder.Append($"\n… truncated at {MaxDisplayBytes:N0} of {bytes.Length:N0} bytes.");
        }

        return builder.ToString();
    }
}

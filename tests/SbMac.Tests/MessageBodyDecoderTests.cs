using System.Text;

using SbMac.Core.Messaging;

using Xunit;

namespace SbMac.Tests;

public class MessageBodyDecoderTests
{
    [Fact]
    public void PlainTextIsReturnedAsText()
    {
        var result = MessageBodyDecoder.Decode(BinaryData.FromString("hello world"));

        Assert.Equal(MessageBodyFormat.Text, result.Format);
        Assert.Equal("hello world", result.Text);
    }

    [Fact]
    public void EmptyBodyIsReportedAsEmpty()
    {
        var result = MessageBodyDecoder.Decode(new BinaryData(Array.Empty<byte>()));

        Assert.Equal(MessageBodyFormat.Empty, result.Format);
    }

    [Fact]
    public void JsonIsDetectedAndIndented()
    {
        var result = MessageBodyDecoder.Decode(BinaryData.FromString("""{"orderId":7,"region":"emea"}"""));

        Assert.Equal(MessageBodyFormat.Json, result.Format);
        Assert.Contains("\n", result.Text);
        Assert.Contains("\"orderId\"", result.Text);
    }

    [Fact]
    public void XmlIsDetectedAndIndented()
    {
        var result = MessageBodyDecoder.Decode(BinaryData.FromString("<order><id>7</id></order>"));

        Assert.Equal(MessageBodyFormat.Xml, result.Format);
        Assert.Contains("\n", result.Text);
    }

    [Fact]
    public void MalformedJsonFallsBackToText()
    {
        var result = MessageBodyDecoder.Decode(BinaryData.FromString("{not really json"));

        Assert.Equal(MessageBodyFormat.Text, result.Format);
        Assert.Equal("{not really json", result.Text);
    }

    [Fact]
    public void NonTextBytesBecomeAHexDump()
    {
        var result = MessageBodyDecoder.Decode(new BinaryData(new byte[] { 0xFF, 0xFE, 0x00, 0x01, 0x80 }));

        Assert.Equal(MessageBodyFormat.Binary, result.Format);
        Assert.Contains("00000000", result.Text);
        Assert.Contains("ff fe", result.Text);
    }

    /// <summary>
    /// Bodies written by the old WindowsAzure.ServiceBus SDK — what the Windows Service
    /// Bus Explorer produced — carry binary-XML DataContract framing. Messages sitting in
    /// long-lived queues are still in this shape, so unwrapping it is the difference
    /// between showing the payload and showing a hex dump.
    /// </summary>
    [Theory]
    [InlineData("hello from the old SDK")]
    [InlineData("short")]
    public void WcfDataContractStringIsUnwrapped(string payload)
    {
        var body = BuildWcfDataContractString(payload);

        var result = MessageBodyDecoder.Decode(new BinaryData(body));

        Assert.Equal(MessageBodyFormat.WcfDataContract, result.Format);
        Assert.Equal(payload, result.Text);
    }

    [Fact]
    public void WcfWrappedJsonIsReportedAsJson()
    {
        var body = BuildWcfDataContractString("""{"id":1}""");

        var result = MessageBodyDecoder.Decode(new BinaryData(body));

        // The framing is stripped, then the payload is classified on its own merits.
        Assert.Equal(MessageBodyFormat.Json, result.Format);
        Assert.Contains("\"id\"", result.Text);
    }

    [Fact]
    public void WcfDataContractWithTwoByteLengthIsUnwrapped()
    {
        // Longer than 255 bytes, so the writer uses the Chars16Text node instead of Chars8Text.
        var payload = new string('a', 700);
        var body = BuildWcfDataContractString(payload);

        var result = MessageBodyDecoder.Decode(new BinaryData(body));

        Assert.Equal(MessageBodyFormat.WcfDataContract, result.Format);
        Assert.Equal(payload, result.Text);
    }

    [Fact]
    public void TextThatMerelyStartsLikeTheWcfPrefixIsNotMisread()
    {
        var result = MessageBodyDecoder.Decode(BinaryData.FromString("string theory"));

        Assert.Equal(MessageBodyFormat.Text, result.Format);
        Assert.Equal("string theory", result.Text);
    }

    /// <summary>
    /// Reproduces what <c>DataContractSerializer</c> emits for a string through
    /// <c>XmlDictionaryWriter.CreateBinaryWriter</c>: a short element node named
    /// "string", then a length-prefixed UTF-8 text node.
    /// </summary>
    static byte[] BuildWcfDataContractString(string payload)
    {
        var text = Encoding.UTF8.GetBytes(payload);
        var buffer = new List<byte> { 0x40, 0x06 };
        buffer.AddRange("string"u8.ToArray());

        if (text.Length <= byte.MaxValue)
        {
            buffer.Add(0x99);                       // Chars8TextWithEndElement
            buffer.Add((byte)text.Length);
        }
        else
        {
            buffer.Add(0x9B);                       // Chars16TextWithEndElement
            buffer.Add((byte)(text.Length & 0xFF));
            buffer.Add((byte)(text.Length >> 8));
        }

        buffer.AddRange(text);
        return buffer.ToArray();
    }
}

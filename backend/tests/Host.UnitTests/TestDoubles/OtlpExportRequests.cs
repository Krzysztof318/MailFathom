// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;

namespace MailFathom.Host.UnitTests.TestDoubles;

/// <summary>Builds the OTLP export requests a client would post, as the octets that would arrive.</summary>
/// <remarks>
/// <para>
/// The proxy reads the protocol buffers wire format rather than a schema, so a test arranges the same thing: an encoder
/// of its own, written from the format rather than from the production reader. That is what makes the assertions
/// independent — a fault in the reader's own varint handling would otherwise be arranged into the input and read back
/// out as agreement.
/// </para>
/// <para>
/// The three signals are shaped identically for everything the proxy does, so one builder covers all three. A record
/// here is a message with one field in it: nothing under a scope is read, and giving a span its real fields would be
/// arranging a schema the subject never consults. A metric is the exception, and only as far as the subject goes — it
/// is given a payload holding the points it measured, because that is the one nesting the record bound counts through.
/// </para>
/// </remarks>
internal static class OtlpExportRequests
{
    private const int FirstField = 1;
    private const int SecondField = 2;

    /// <summary>The one-of arm a sum reports its data points in, which is one of the five a metric may use.</summary>
    private const int SumField = 7;

    /// <summary>Builds one export request carrying one resource and one scope.</summary>
    /// <param name="resourceAttributes">The attributes the client claims on the resource.</param>
    /// <param name="records">How many records the scope carries.</param>
    /// <returns>The request as it would arrive.</returns>
    internal static byte[] Batch(IReadOnlyList<KeyValuePair<string, string>> resourceAttributes, int records) =>
        Envelope(Resource(resourceAttributes), Enumerable.Range(0, records).Select(_ => Record()));

    /// <summary>Builds one metrics export request whose scope carries metrics reporting points of their own.</summary>
    /// <param name="metrics">How many metric definitions the scope carries.</param>
    /// <param name="dataPoints">How many data points each of them reports.</param>
    /// <param name="payloadField">The one-of arm the points are reported in, a sum by default and an unknown number for a shape this repository has no name for.</param>
    /// <returns>The request as it would arrive.</returns>
    internal static byte[] MetricsBatch(int metrics, int dataPoints, int payloadField = SumField) =>
        Envelope(
            Resource([]),
            Enumerable.Range(0, metrics).Select(_ => Metric(dataPoints, payloadField)));

    /// <summary>Builds one export request whose envelope carries no resource at all, which a client is free to send.</summary>
    /// <param name="records">How many records the scope carries.</param>
    /// <returns>The request as it would arrive.</returns>
    internal static byte[] BatchWithoutResource(int records) =>
        Envelope(resource: null, Enumerable.Range(0, records).Select(_ => Record()));

    /// <summary>Encodes one resource attribute exactly as it appears inside a request, tag included.</summary>
    private static byte[] Attribute(string key, string value) => LengthDelimited(
        FirstField,
        [.. LengthDelimited(FirstField, Encoding.UTF8.GetBytes(key)),
         .. LengthDelimited(SecondField, LengthDelimited(FirstField, Encoding.UTF8.GetBytes(value)))]);

    /// <summary>Reads back every resource attribute a request carries, decoded from the wire rather than searched for.</summary>
    /// <param name="request">The request to read.</param>
    /// <returns>The attributes of every resource in the request, in the order they were encoded.</returns>
    /// <remarks>
    /// Structural rather than a byte search, and that is the whole point of it: the encoded form of one attribute is
    /// the encoded form of a resource carrying exactly that attribute and nothing else, so a substring search reports
    /// a rewritten batch as correct whether the entry was written as an attribute or as the resource around it. Only a
    /// reader that parses the nesting tells the two apart, which is what a collector does with the batch.
    /// </remarks>
    internal static IReadOnlyList<KeyValuePair<string, string>> ResourceAttributes(byte[] request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return
        [
            .. Fields(request)
                .Where(envelope => envelope.Field == FirstField)
                .SelectMany(envelope => Fields(envelope.Payload))
                .Where(resource => resource.Field == FirstField)
                .SelectMany(resource => Fields(resource.Payload))
                .Where(entry => entry.Field == FirstField)
                .Select(entry => AttributeOf(entry.Payload)),
        ];
    }

    private static byte[] Envelope(byte[]? resource, IEnumerable<byte[]> records)
    {
        byte[] scope = [.. records.SelectMany(record => LengthDelimited(SecondField, record))];

        byte[] envelope =
        [
            .. resource is null ? [] : LengthDelimited(FirstField, resource),
            .. LengthDelimited(SecondField, scope),
        ];

        return LengthDelimited(FirstField, envelope);
    }

    private static byte[] Resource(IReadOnlyList<KeyValuePair<string, string>> attributes) =>
        [.. attributes.SelectMany(attribute => Attribute(attribute.Key, attribute.Value))];

    /// <summary>One record, whose only field is opaque to everything the proxy does with it.</summary>
    private static byte[] Record() => LengthDelimited(FirstField, Encoding.UTF8.GetBytes("r"));

    /// <summary>One metric: a name it is reported under, and one payload holding the points it measured.</summary>
    private static byte[] Metric(int dataPoints, int payloadField) =>
    [
        .. LengthDelimited(FirstField, Encoding.UTF8.GetBytes("m")),
        .. LengthDelimited(
            payloadField,
            [.. Enumerable.Range(0, dataPoints)
                .SelectMany(_ => LengthDelimited(FirstField, Encoding.UTF8.GetBytes("p")))]),
    ];

    private static byte[] LengthDelimited(int field, ReadOnlySpan<byte> payload) =>
        [.. Varint(((ulong)field << 3) | 2), .. Varint((ulong)payload.Length), .. payload];

    /// <summary>Decodes one key-value entry, whose value this builder only ever writes as a string.</summary>
    private static KeyValuePair<string, string> AttributeOf(byte[] entry)
    {
        var fields = Fields(entry);
        var key = fields.Single(field => field.Field == FirstField).Payload;
        var anyValue = fields.Single(field => field.Field == SecondField).Payload;
        var value = Fields(anyValue).Single(field => field.Field == FirstField).Payload;

        return new KeyValuePair<string, string>(Encoding.UTF8.GetString(key), Encoding.UTF8.GetString(value));
    }

    /// <summary>Reads the length-delimited fields of one message, which is every field this builder writes.</summary>
    /// <remarks>
    /// A field of any other wire type stops the reading rather than being skipped: nothing here encodes one, so meeting
    /// one means the octets are not what this reader was pointed at, and a reader that walked past it would report
    /// whatever came after as the message's own content.
    /// </remarks>
    private static List<(int Field, byte[] Payload)> Fields(ReadOnlySpan<byte> message)
    {
        var fields = new List<(int, byte[])>();
        var at = 0;

        while (at < message.Length)
        {
            var tag = ReadVarint(message, ref at);

            if ((tag & 0x7) != 2)
            {
                throw new InvalidOperationException($"Field {tag >> 3} is not length-delimited.");
            }

            var length = (int)ReadVarint(message, ref at);
            fields.Add(((int)(tag >> 3), message.Slice(at, length).ToArray()));
            at += length;
        }

        return fields;
    }

    private static ulong ReadVarint(ReadOnlySpan<byte> message, ref int at)
    {
        ulong value = 0;

        for (var shift = 0; ; shift += 7)
        {
            var octet = message[at++];
            value |= (ulong)(octet & 0x7F) << shift;

            if ((octet & 0x80) == 0)
            {
                return value;
            }
        }
    }

    private static byte[] Varint(ulong value)
    {
        var encoded = new List<byte>(10);

        do
        {
            var octet = (byte)(value & 0x7F);
            value >>= 7;
            encoded.Add(value == 0 ? octet : (byte)(octet | 0x80));
        }
        while (value != 0);

        return [.. encoded];
    }
}

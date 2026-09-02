// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Buffers;
using System.Text;

namespace MailFathom.Host.Observability.ClientTelemetry;

/// <summary>Reads an OTLP export request far enough to count what it carries and to write who it belongs to.</summary>
/// <remarks>
/// <para>
/// The proxy is not a processor, so this is deliberately not a decoder of the OTLP schema. It walks the protocol
/// buffers wire format, which needs no schema at all: every field announces its own number and its own length, so a
/// field this repository has no opinion about is copied octet for octet without ever being understood. What is
/// understood is the one path to the resource attributes and the two levels of nesting a record sits at, and those are
/// the same three field numbers in all three signals — a repeated resource envelope at field 1, the resource itself at
/// field 1 within it, repeated scopes at field 2, and repeated records at field 2 within those.
/// </para>
/// <para>
/// Copying rather than re-encoding is what makes that safe as the schema grows. A field OpenTelemetry adds after this
/// was written travels through untouched, in the order it arrived and with its own bytes, so a collector reads exactly
/// what the client sent apart from the one attribute this deployment overwrites. Nothing here allocates per record: the
/// walk is over spans, and the only bytes written are the envelope's own length prefixes and the attribute added.
/// </para>
/// <para>
/// A payload that does not parse is refused rather than forwarded. That is a bound as much as a correctness rule — a
/// receiver that forwards whatever arrives is a receiver an unauthenticated shape can push arbitrary octets through,
/// and the specification answers a request it cannot decode rather than passing it on.
/// </para>
/// </remarks>
internal static class OtlpExportPayload
{
    private const int VarintWireType = 0;
    private const int Fixed64WireType = 1;
    private const int LengthDelimitedWireType = 2;
    private const int Fixed32WireType = 5;

    /// <summary>The field number the resource envelope, the resource, and a key-value entry all sit at.</summary>
    private const int FirstField = 1;

    /// <summary>The field number the scopes within an envelope and the records within a scope both sit at.</summary>
    private const int SecondField = 2;

    /// <summary>Rewrites one export request so it names the owner this deployment authenticated, and counts what it carries.</summary>
    /// <param name="request">The export request exactly as the client sent it.</param>
    /// <param name="attributeKey">The resource attribute naming whose telemetry this is.</param>
    /// <param name="attributeValue">What this deployment resolved that owner to.</param>
    /// <param name="maxRecords">The most records one batch may carry before it is refused whole.</param>
    /// <returns>The rewritten request and what it carries, or the refusal that stopped it.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="attributeKey" /> or <paramref name="attributeValue" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The owner attribute is written onto every resource in the batch rather than onto the first, because a batch may
    /// carry several and a resource left unwritten would be the one path by which a client's own claim survived at the
    /// level attribution is read from. It reaches the resource and nothing below it: an attribute a client writes on a
    /// scope, a span, a log record, or a metric data point is copied through like every other field it sends, since
    /// reaching those means walking each signal's own schema, which is the decoding this file exists not to do.
    /// </remarks>
    internal static OtlpExportRewrite Rewrite(
        ReadOnlySpan<byte> request,
        string attributeKey,
        string attributeValue,
        int maxRecords)
    {
        ArgumentNullException.ThrowIfNull(attributeKey);
        ArgumentNullException.ThrowIfNull(attributeValue);

        var owner = OwnerAttribute(attributeKey, attributeValue);
        var output = new ArrayBufferWriter<byte>(request.Length + owner.Length + 16);
        var records = 0;
        var at = 0;

        while (at < request.Length)
        {
            if (!TryReadField(request, ref at, out var field, out var wireType, out var start, out var length))
            {
                return OtlpExportRewrite.Malformed;
            }

            if (field != FirstField || wireType != LengthDelimitedWireType)
            {
                CopyField(output, request, field, wireType, start, length);

                continue;
            }

            var envelope = RewriteEnvelope(request.Slice(start, length), owner, maxRecords, ref records);

            if (envelope.Refusal != OtlpPayloadRefusal.None)
            {
                return envelope;
            }

            WriteLengthDelimited(output, FirstField, envelope.Body);
        }

        return new OtlpExportRewrite(output.WrittenSpan.ToArray(), records, OtlpPayloadRefusal.None);
    }

    /// <summary>Rewrites one resource envelope, replacing its resource and counting the records beneath it.</summary>
    /// <remarks>
    /// An envelope carrying no resource at all is given one rather than left alone. The client is not obliged to send a
    /// resource, and telemetry arriving under none would be the one batch this deployment forwarded without saying
    /// whose it was.
    /// </remarks>
    private static OtlpExportRewrite RewriteEnvelope(
        ReadOnlySpan<byte> envelope,
        ReadOnlySpan<byte> owner,
        int maxRecords,
        ref int records)
    {
        var output = new ArrayBufferWriter<byte>(envelope.Length + owner.Length + 8);
        var carriedResource = false;
        var at = 0;

        while (at < envelope.Length)
        {
            if (!TryReadField(envelope, ref at, out var field, out var wireType, out var start, out var length))
            {
                return OtlpExportRewrite.Malformed;
            }

            if (wireType != LengthDelimitedWireType)
            {
                CopyField(output, envelope, field, wireType, start, length);

                continue;
            }

            switch (field)
            {
                case FirstField:
                    carriedResource = true;

                    if (!TryWriteResource(output, envelope.Slice(start, length), owner))
                    {
                        return OtlpExportRewrite.Malformed;
                    }

                    break;

                case SecondField:
                    if (!TryCountRecords(envelope.Slice(start, length), maxRecords, ref records))
                    {
                        return records > maxRecords ? OtlpExportRewrite.TooManyRecords : OtlpExportRewrite.Malformed;
                    }

                    CopyField(output, envelope, field, wireType, start, length);

                    break;

                default:
                    CopyField(output, envelope, field, wireType, start, length);

                    break;
            }
        }

        if (!carriedResource)
        {
            var resource = new ArrayBufferWriter<byte>(owner.Length + 8);
            WriteLengthDelimited(resource, FirstField, owner);
            WriteLengthDelimited(output, FirstField, resource.WrittenSpan);
        }

        return new OtlpExportRewrite(output.WrittenSpan.ToArray(), records, OtlpPayloadRefusal.None);
    }

    /// <summary>Writes the resource with the owner attribute replacing whatever the client claimed under that name.</summary>
    /// <returns><see langword="true" /> when the resource parsed, otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// Replaced rather than merged: an entry the client sent under this key is dropped as it is read, and this
    /// deployment's own is appended once. Every other attribute survives, because what the resource says about the
    /// browser, the release, or the screen is the client's to report.
    /// </remarks>
    private static bool TryWriteResource(
        ArrayBufferWriter<byte> output,
        ReadOnlySpan<byte> resource,
        ReadOnlySpan<byte> owner)
    {
        var attributes = new ArrayBufferWriter<byte>(resource.Length + owner.Length);
        var claimedKey = OwnerAttributeKey(owner);
        var at = 0;

        while (at < resource.Length)
        {
            if (!TryReadField(resource, ref at, out var field, out var wireType, out var start, out var length))
            {
                return false;
            }

            if (field == FirstField
                && wireType == LengthDelimitedWireType
                && NamesTheOwner(resource.Slice(start, length), claimedKey))
            {
                continue;
            }

            CopyField(attributes, resource, field, wireType, start, length);
        }

        WriteLengthDelimited(attributes, FirstField, owner);
        WriteLengthDelimited(output, FirstField, attributes.WrittenSpan);

        return true;
    }

    /// <summary>Counts the records one scope carries, refusing once the batch is past what this endpoint accepts.</summary>
    /// <returns><see langword="true" /> when the scope parsed and the batch is still within its bound.</returns>
    private static bool TryCountRecords(ReadOnlySpan<byte> scope, int maxRecords, ref int records)
    {
        var at = 0;

        while (at < scope.Length)
        {
            if (!TryReadField(scope, ref at, out var field, out var wireType, out _, out _))
            {
                return false;
            }

            if (field != SecondField || wireType != LengthDelimitedWireType)
            {
                continue;
            }

            records++;

            if (records > maxRecords)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Reports whether one key-value entry is written under the key this deployment owns.</summary>
    private static bool NamesTheOwner(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> key)
    {
        var at = 0;

        while (at < entry.Length)
        {
            if (!TryReadField(entry, ref at, out var field, out var wireType, out var start, out var length))
            {
                return false;
            }

            if (field == FirstField && wireType == LengthDelimitedWireType)
            {
                return entry.Slice(start, length).SequenceEqual(key);
            }
        }

        return false;
    }

    /// <summary>Encodes the status document the OTLP specification requires a refusal to carry.</summary>
    /// <param name="code">The canonical status code naming what kind of refusal this is.</param>
    /// <param name="message">What an operator or a client developer reads, which never carries any part of the payload.</param>
    /// <returns>The encoded status.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="message" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// The specification is explicit that a refused export answers with a status document rather than with an empty
    /// body or a shape of this repository's own, because the client reading it is an exporter rather than a person.
    /// Two fields is the whole of what one needs, so it is written here beside the wire primitives rather than pulling
    /// a schema and a code generator in for a message with a code and a sentence in it.
    /// </remarks>
    internal static byte[] Status(int code, string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var output = new ArrayBufferWriter<byte>(Encoding.UTF8.GetByteCount(message) + 8);
        WriteVarint(output, ((ulong)FirstField << 3) | VarintWireType);
        WriteVarint(output, (ulong)code);
        WriteLengthDelimited(output, SecondField, Encoding.UTF8.GetBytes(message));

        return output.WrittenSpan.ToArray();
    }

    /// <summary>Encodes the one key-value entry this deployment writes onto every resource it forwards.</summary>
    private static byte[] OwnerAttribute(string key, string value)
    {
        var anyValue = new ArrayBufferWriter<byte>(Encoding.UTF8.GetByteCount(value) + 8);
        WriteLengthDelimited(anyValue, FirstField, Encoding.UTF8.GetBytes(value));

        var attribute = new ArrayBufferWriter<byte>(anyValue.WrittenCount + Encoding.UTF8.GetByteCount(key) + 8);
        WriteLengthDelimited(attribute, FirstField, Encoding.UTF8.GetBytes(key));
        WriteLengthDelimited(attribute, SecondField, anyValue.WrittenSpan);

        return attribute.WrittenSpan.ToArray();
    }

    /// <summary>Reads back the key out of the encoded attribute, so the name exists once rather than twice.</summary>
    private static ReadOnlySpan<byte> OwnerAttributeKey(ReadOnlySpan<byte> owner)
    {
        var at = 0;

        return TryReadField(owner, ref at, out _, out _, out var start, out var length)
            ? owner.Slice(start, length)
            : [];
    }

    /// <summary>Reads one field, leaving the cursor past it and reporting where its payload sits.</summary>
    /// <returns><see langword="false" /> for a field that is truncated, carries an unknown wire type, or is numbered zero.</returns>
    /// <remarks>
    /// The two group wire types are refused rather than skipped. They were removed from the language in proto3, no
    /// OpenTelemetry message uses one, and skipping a group correctly means matching its end marker — so a payload
    /// carrying one is refused as the thing it is, which is not an OTLP request.
    /// </remarks>
    private static bool TryReadField(
        ReadOnlySpan<byte> body,
        ref int at,
        out int field,
        out int wireType,
        out int payloadStart,
        out int payloadLength)
    {
        field = 0;
        wireType = 0;
        payloadStart = 0;
        payloadLength = 0;

        if (!TryReadVarint(body, ref at, out var tag))
        {
            return false;
        }

        field = (int)(tag >> 3);
        wireType = (int)(tag & 0x7);

        if (field == 0)
        {
            return false;
        }

        switch (wireType)
        {
            case VarintWireType:
                payloadStart = at;

                if (!TryReadVarint(body, ref at, out _))
                {
                    return false;
                }

                payloadLength = at - payloadStart;

                return true;

            case Fixed64WireType:
            case Fixed32WireType:
                payloadLength = wireType == Fixed64WireType ? 8 : 4;

                if (body.Length - at < payloadLength)
                {
                    return false;
                }

                payloadStart = at;
                at += payloadLength;

                return true;

            case LengthDelimitedWireType:
                if (!TryReadVarint(body, ref at, out var length) || length > (ulong)(body.Length - at))
                {
                    return false;
                }

                payloadStart = at;
                payloadLength = (int)length;
                at += payloadLength;

                return true;

            default:
                return false;
        }
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> body, ref int at, out ulong value)
    {
        value = 0;

        for (var shift = 0; shift < 64; shift += 7)
        {
            if (at >= body.Length)
            {
                return false;
            }

            var octet = body[at++];
            value |= (ulong)(octet & 0x7F) << shift;

            if ((octet & 0x80) == 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Copies one field back out exactly as it arrived, tag included.</summary>
    private static void CopyField(
        ArrayBufferWriter<byte> output,
        ReadOnlySpan<byte> body,
        int field,
        int wireType,
        int payloadStart,
        int payloadLength)
    {
        WriteVarint(output, ((ulong)field << 3) | (uint)wireType);

        if (wireType == LengthDelimitedWireType)
        {
            WriteVarint(output, (ulong)payloadLength);
        }

        output.Write(body.Slice(payloadStart, payloadLength));
    }

    private static void WriteLengthDelimited(ArrayBufferWriter<byte> output, int field, ReadOnlySpan<byte> payload)
    {
        WriteVarint(output, ((ulong)field << 3) | LengthDelimitedWireType);
        WriteVarint(output, (ulong)payload.Length);
        output.Write(payload);
    }

    private static void WriteVarint(ArrayBufferWriter<byte> output, ulong value)
    {
        Span<byte> encoded = stackalloc byte[10];
        var written = 0;

        do
        {
            var octet = (byte)(value & 0x7F);
            value >>= 7;
            encoded[written++] = value == 0 ? octet : (byte)(octet | 0x80);
        }
        while (value != 0);

        output.Write(encoded[..written]);
    }
}

/// <summary>Why an export request was refused before anything was forwarded.</summary>
internal enum OtlpPayloadRefusal
{
    /// <summary>The request parsed and is forwardable.</summary>
    None = 0,

    /// <summary>The octets are not a protocol buffers message this endpoint can read.</summary>
    Malformed = 1,

    /// <summary>The batch carries more records than one request may.</summary>
    TooManyRecords = 2,
}

/// <summary>What reading one export request produced.</summary>
/// <param name="Body">The request as it will be forwarded, empty where it was refused.</param>
/// <param name="RecordCount">How many records the batch carries, as far as reading it got.</param>
/// <param name="Refusal">Why the request was refused, or <see cref="OtlpPayloadRefusal.None" /> where it was not.</param>
internal readonly record struct OtlpExportRewrite(byte[] Body, int RecordCount, OtlpPayloadRefusal Refusal)
{
    /// <summary>Gets the reading of a request that is not a protocol buffers message.</summary>
    internal static OtlpExportRewrite Malformed { get; } = new([], 0, OtlpPayloadRefusal.Malformed);

    /// <summary>Gets the reading of a batch carrying more records than one request may.</summary>
    internal static OtlpExportRewrite TooManyRecords { get; } = new([], 0, OtlpPayloadRefusal.TooManyRecords);
}

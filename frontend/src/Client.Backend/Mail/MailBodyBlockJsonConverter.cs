// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace MailFathom.Client.Backend.Mail;

/// <summary>Reads one block by the identity and revision the deployment stamped on it.</summary>
/// <remarks>
/// <para>
/// Written by hand rather than declared with the polymorphic attributes, and for the property those attributes do not
/// have: an identity nothing here declares makes the serializer refuse the whole document, which would cost a reader
/// the entire message because a deployment ahead of this build added one block. Here it costs them that block.
/// </para>
/// <para>
/// The revision is checked the same way and for the same reason. A newer revision of a block this build knows would
/// otherwise deserialize into this build's shape with the members that revision added silently dropped, and present as
/// though nothing were missing — which is worse than saying that a part of the message cannot be shown.
/// </para>
/// <para>
/// Every read below goes through a generated type info rather than through reflection, because the browser head
/// publishes trimmed and a reflection-based read is removed by the trimmer rather than reported.
/// </para>
/// </remarks>
public sealed class MailBodyBlockJsonConverter : JsonConverter<MailBodyBlock>
{
    /// <summary>The revision of each block this build implements, keyed by the identity the deployment publishes.</summary>
    /// <remarks>
    /// The client's own catalogue rather than a number taken from the document. Reading the revision out of the block
    /// and trusting it would make the check say only that the deployment agrees with itself.
    /// </remarks>
    private static readonly Dictionary<string, int> Implemented = new(StringComparer.Ordinal)
    {
        ["paragraph"] = 1,
        ["heading"] = 1,
        ["list"] = 1,
        ["table"] = 1,
        ["quote"] = 1,
        ["image"] = 1,
        ["separator"] = 1,
        ["preformatted"] = 1,
    };

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not an object, which is a document this deployment did not write.</exception>
    public override MailBodyBlock Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var block = JsonDocument.ParseValue(ref reader);

        var root = block.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"A block must be a JSON object, but the value was {root.ValueKind}.");
        }

        var identity = root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

        var version = root.TryGetProperty("version", out var revision)
            && revision.ValueKind == JsonValueKind.Number
            && revision.TryGetInt32(out var stated)
                ? stated
                : 0;

        return identity is not null && Implemented.TryGetValue(identity, out var implemented) && implemented == version
            ? Known(identity, root)
            : new MailBodyUnsupportedBlock(identity, version);
    }

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always, because a body is read from the deployment and never sent to one.</exception>
    public override void Write(Utf8JsonWriter writer, MailBodyBlock value, JsonSerializerOptions options) =>
        throw new NotSupportedException("A message body is read from the deployment rather than written to it.");

    /// <summary>Reads a block whose identity and revision this build implements.</summary>
    /// <remarks>
    /// A block whose shape does not read at all becomes the unsupported one rather than failing the document, because
    /// the reader's position is the same either way: this part of the message cannot be drawn, and the rest can.
    /// </remarks>
    private static MailBodyBlock Known(string identity, JsonElement block)
    {
        try
        {
            return identity switch
            {
                "paragraph" => Read(block, DeploymentJsonContext.Default.MailBodyParagraphBlock),
                "heading" => Read(block, DeploymentJsonContext.Default.MailBodyHeadingBlock),
                "list" => Read(block, DeploymentJsonContext.Default.MailBodyListBlock),
                "table" => Read(block, DeploymentJsonContext.Default.MailBodyTableBlock),
                "quote" => Read(block, DeploymentJsonContext.Default.MailBodyQuoteBlock),
                "image" => Read(block, DeploymentJsonContext.Default.MailBodyImageBlock),
                "separator" => Read(block, DeploymentJsonContext.Default.MailBodySeparatorBlock),
                "preformatted" => Read(block, DeploymentJsonContext.Default.MailBodyPreformattedBlock),
                _ => new MailBodyUnsupportedBlock(identity, Version: 0),
            };
        }
        catch (JsonException)
        {
            return new MailBodyUnsupportedBlock(identity, Implemented[identity]);
        }
    }

    private static MailBodyBlock Read<TBlock>(JsonElement block, JsonTypeInfo<TBlock> typeInfo)
        where TBlock : MailBodyBlock =>
        block.Deserialize(typeInfo) as MailBodyBlock ?? new MailBodyUnsupportedBlock(Identity: null, Version: 0);
}

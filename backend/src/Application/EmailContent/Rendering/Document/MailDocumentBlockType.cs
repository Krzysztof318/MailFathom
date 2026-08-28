// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.EmailContent.Rendering.Document;

/// <summary>The closed catalogue of blocks a reduced mail body may hold.</summary>
/// <remarks>
/// <para>
/// Eight members and no ninth. The tree is built from markup a stranger wrote, so what a client draws has to be a set
/// somebody reviewed rather than whatever the sender's document happened to contain: adding a member is a source change
/// that reaches a review, and a client switches over the set exhaustively.
/// </para>
/// <para>
/// It is a closed enumeration rather than a C# <see langword="enum" /> for the reason
/// <c>PresentationBlockType</c> is one — a member carries an identity the wire uses, which survives a rename of the C#
/// member, and the revision of that block's own contract, which is what lets a client meeting a document from a newer
/// service refuse the one block it does not know and render the rest of the message. A version is raised when that
/// block's shape changes and never for a change elsewhere.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no block; it reports itself through
/// <see cref="IsSpecified" /> and is refused by the converter.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailDocumentBlockTypeJsonConverter))]
public readonly record struct MailDocumentBlockType
{
    /// <summary>The identity of the block holding one run of body text.</summary>
    public const string ParagraphIdentity = "paragraph";

    /// <summary>The identity of the block holding a heading.</summary>
    public const string HeadingIdentity = "heading";

    /// <summary>The identity of the block holding a bulleted or numbered list.</summary>
    public const string ListIdentity = "list";

    /// <summary>The identity of the block holding a table, which is how mail layout is overwhelmingly built.</summary>
    public const string TableIdentity = "table";

    /// <summary>The identity of the block holding quoted history at its own depth.</summary>
    public const string QuoteIdentity = "quote";

    /// <summary>The identity of the block holding one image resolved from the message's own parts.</summary>
    public const string ImageIdentity = "image";

    /// <summary>The identity of the block holding a horizontal separator.</summary>
    public const string SeparatorIdentity = "separator";

    /// <summary>The identity of the block holding text whose own line breaks and spacing are the content.</summary>
    public const string PreformattedIdentity = "preformatted";

    private readonly string? identity;

    private MailDocumentBlockType(string identity, int version)
    {
        this.identity = identity;
        this.Version = version;
    }

    /// <summary>Gets the type of the block holding one run of body text.</summary>
    public static MailDocumentBlockType Paragraph { get; } = new(ParagraphIdentity, version: 1);

    /// <summary>Gets the type of the block holding a heading.</summary>
    public static MailDocumentBlockType Heading { get; } = new(HeadingIdentity, version: 1);

    /// <summary>Gets the type of the block holding a bulleted or numbered list.</summary>
    public static MailDocumentBlockType List { get; } = new(ListIdentity, version: 1);

    /// <summary>Gets the type of the block holding a table.</summary>
    public static MailDocumentBlockType Table { get; } = new(TableIdentity, version: 1);

    /// <summary>Gets the type of the block holding quoted history at its own depth.</summary>
    public static MailDocumentBlockType Quote { get; } = new(QuoteIdentity, version: 1);

    /// <summary>Gets the type of the block holding one image resolved from the message's own parts.</summary>
    public static MailDocumentBlockType Image { get; } = new(ImageIdentity, version: 1);

    /// <summary>Gets the type of the block holding a horizontal separator.</summary>
    public static MailDocumentBlockType Separator { get; } = new(SeparatorIdentity, version: 1);

    /// <summary>Gets the type of the block holding text whose own line breaks and spacing are the content.</summary>
    public static MailDocumentBlockType Preformatted { get; } = new(PreformattedIdentity, version: 1);

    /// <summary>Gets every block type the catalogue holds.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailDocumentBlockType> All { get; } =
    [
        Paragraph,
        Heading,
        List,
        Table,
        Quote,
        Image,
        Separator,
        Preformatted,
    ];

    /// <summary>Gets whether this value names a catalogued block rather than the unusable struct default.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Gets the revision of this block's own contract, which every block of the type carries.</summary>
    public int Version { get; }

    /// <summary>Gets the identity the wire uses, which survives a rename of the C# member.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a block type.</exception>
    public string Identity => this.identity
        ?? throw new InvalidOperationException("The value is the default of the struct and names no block type.");

    /// <summary>Parses an identity read off the wire.</summary>
    /// <param name="identity">The identity to parse.</param>
    /// <param name="blockType">The catalogued type when the identity names one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the identity names a catalogued block; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// An identity nothing declares is unknown rather than new, so nothing is reconstructed from it: a client meeting one
    /// is meeting a service ahead of it, which is what the block's version and the document's schema version are for.
    /// </remarks>
    public static bool TryParse(string? identity, out MailDocumentBlockType blockType)
    {
        blockType = default;

        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var normalized = identity.Trim();

        blockType = All.FirstOrDefault(candidate =>
            string.Equals(candidate.Identity, normalized, StringComparison.Ordinal));

        return blockType.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.identity is null
        ? "(unspecified)"
        : string.Create(CultureInfo.InvariantCulture, $"{this.identity} v{this.Version}");
}

/// <summary>Serializes <see cref="MailDocumentBlockType" /> as the identity the catalogue publishes.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value writes the identity rather than an ordinal — a position would silently change meaning the first time the
/// catalogue were reordered, while the identity is what a client's renderers are keyed by.
/// </remarks>
public sealed class MailDocumentBlockTypeJsonConverter : JsonConverter<MailDocumentBlockType>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a catalogued block.</exception>
    public override MailDocumentBlockType Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A block type must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailDocumentBlockType value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(SpecifiedOrThrow(value));
    }

    private static MailDocumentBlockType ParseOrThrow(string? identity) =>
        MailDocumentBlockType.TryParse(identity, out var parsed)
            ? parsed
            : throw new JsonException("The value does not name a block this catalogue holds.");

    private static string SpecifiedOrThrow(MailDocumentBlockType value) => value.IsSpecified
        ? value.Identity
        : throw new JsonException("A block type cannot be written from the unspecified default of the struct.");
}

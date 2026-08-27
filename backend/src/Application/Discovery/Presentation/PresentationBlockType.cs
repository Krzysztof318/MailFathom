// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>The closed catalogue of block types a presentation plan may hold.</summary>
/// <remarks>
/// <para>
/// Nine members and no tenth. A plan is composed by a model, and a catalogue that were an open string would let one
/// invent a presentation the client has no renderer for — so the set is declared here, adding to it is a source change
/// that reaches a review, and a client can switch over it exhaustively.
/// </para>
/// <para>
/// It is a closed enumeration rather than a C# <see langword="enum" /> because a member carries two things beside its
/// name. The identity is the value the wire uses, which must survive a rename of the C# member; and the version is the
/// revision of that block type's own contract, which is what lets a client meeting a plan from a newer service render
/// the blocks it knows and refuse one block rather than the whole run. A separate lookup table for either would be a
/// second place to keep in step with this one.
/// </para>
/// <para>
/// A version is raised when that block type's shape changes and never for a change elsewhere, which is the point of
/// putting it here rather than deriving it from the plan's schema version. An identity is allocated once and never
/// reused, exactly as an error code is. Being a struct, <see langword="default" /> is reachable and names no block; it
/// reports itself through <see cref="IsSpecified" /> and is refused by the converter.
/// </para>
/// </remarks>
[JsonConverter(typeof(PresentationBlockTypeJsonConverter))]
public readonly record struct PresentationBlockType
{
    /// <summary>The identity of the block presenting one synthesized answer.</summary>
    public const string AnswerIdentity = "answer";

    /// <summary>The identity of the block presenting the messages an answer rests on.</summary>
    public const string EvidenceListIdentity = "evidenceList";

    /// <summary>The identity of the block presenting change over time.</summary>
    public const string TimelineIdentity = "timeline";

    /// <summary>The identity of the block comparing values across a known set of columns.</summary>
    public const string FactTableIdentity = "factTable";

    /// <summary>The identity of the block presenting people and organizations.</summary>
    public const string PeopleIdentity = "people";

    /// <summary>The identity of the block presenting where a conversation stands.</summary>
    public const string ThreadStateIdentity = "threadState";

    /// <summary>The identity of the block presenting files found in mail.</summary>
    public const string AttachmentGalleryIdentity = "attachmentGallery";

    /// <summary>The identity of the block presenting text to be sent.</summary>
    public const string DraftIdentity = "draft";

    /// <summary>The identity of the block presenting a next step somebody may take.</summary>
    public const string SuggestedActionIdentity = "suggestedAction";

    private readonly string? identity;

    private PresentationBlockType(string identity, int version)
    {
        this.identity = identity;
        this.Version = version;
    }

    #region Reading — the blocks that present what the correspondence says

    /// <summary>Gets the type of the block presenting one synthesized answer.</summary>
    public static PresentationBlockType Answer { get; } = new(AnswerIdentity, version: 1);

    /// <summary>Gets the type of the block presenting the messages an answer rests on.</summary>
    public static PresentationBlockType EvidenceList { get; } = new(EvidenceListIdentity, version: 1);

    /// <summary>Gets the type of the block presenting change over time.</summary>
    public static PresentationBlockType Timeline { get; } = new(TimelineIdentity, version: 1);

    /// <summary>Gets the type of the block comparing values across a known set of columns.</summary>
    public static PresentationBlockType FactTable { get; } = new(FactTableIdentity, version: 1);

    /// <summary>Gets the type of the block presenting people and organizations.</summary>
    public static PresentationBlockType People { get; } = new(PeopleIdentity, version: 1);

    /// <summary>Gets the type of the block presenting where a conversation stands.</summary>
    public static PresentationBlockType ThreadState { get; } = new(ThreadStateIdentity, version: 1);

    /// <summary>Gets the type of the block presenting files found in mail.</summary>
    public static PresentationBlockType AttachmentGallery { get; } = new(AttachmentGalleryIdentity, version: 1);

    #endregion

    #region Acting — the blocks that present something a person may do next

    /// <summary>Gets the type of the block presenting text to be sent.</summary>
    public static PresentationBlockType Draft { get; } = new(DraftIdentity, version: 1);

    /// <summary>Gets the type of the block presenting a next step somebody may take.</summary>
    public static PresentationBlockType SuggestedAction { get; } = new(SuggestedActionIdentity, version: 1);

    #endregion

    /// <summary>Gets every block type the catalogue holds.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<PresentationBlockType> All { get; } =
    [
        Answer,
        EvidenceList,
        Timeline,
        FactTable,
        People,
        ThreadState,
        AttachmentGallery,
        Draft,
        SuggestedAction,
    ];

    /// <summary>Gets whether this value names a catalogued block type rather than the unusable struct default.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Gets the revision of this block type's own contract, which every block of the type carries.</summary>
    public int Version { get; }

    /// <summary>Gets the identity the wire uses, which survives a rename of the C# member.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a block type.</exception>
    public string Identity => this.identity
        ?? throw new InvalidOperationException("The value is the default of the struct and names no block type.");

    /// <summary>Parses an identity read off the wire.</summary>
    /// <param name="identity">The identity to parse.</param>
    /// <param name="blockType">The catalogued type when the identity names one; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the identity names a catalogued block type; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// An identity nothing declares is unknown rather than new, so nothing is reconstructed from it: a client meeting
    /// one is meeting a service ahead of it, which is what the block's version and the plan's schema version are for.
    /// </remarks>
    public static bool TryParse(string? identity, out PresentationBlockType blockType)
    {
        blockType = default;

        if (string.IsNullOrWhiteSpace(identity))
        {
            return false;
        }

        var normalized = identity.Trim();

        blockType = All.FirstOrDefault(candidate => string.Equals(candidate.Identity, normalized, StringComparison.Ordinal));

        return blockType.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.identity is null
        ? "(unspecified)"
        : string.Create(CultureInfo.InvariantCulture, $"{this.identity} v{this.Version}");
}

/// <summary>Serializes <see cref="PresentationBlockType" /> as the identity the catalogue publishes.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value writes the identity rather than an ordinal — a position would silently change meaning the first time the
/// catalogue were reordered, while the identity is what a client's renderers are keyed by.
/// </remarks>
public sealed class PresentationBlockTypeJsonConverter : JsonConverter<PresentationBlockType>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a catalogued block type.</exception>
    public override PresentationBlockType Read(
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
        PresentationBlockType value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(SpecifiedOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a catalogued block type.</exception>
    public override PresentationBlockType ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        PresentationBlockType value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(SpecifiedOrThrow(value));
    }

    private static PresentationBlockType ParseOrThrow(string? identity) =>
        PresentationBlockType.TryParse(identity, out var parsed)
            ? parsed
            : throw new JsonException("The value does not name a block type this catalogue holds.");

    private static string SpecifiedOrThrow(PresentationBlockType value) => value.IsSpecified
        ? value.Identity
        : throw new JsonException("A block type cannot be written from the unspecified default of the struct.");
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Infrastructure.DataEncryption;

/// <summary>Identifies what a sealed value is, so that one key ring can protect several kinds of value safely.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration rather than a C# <see langword="enum" /> because a purpose has a published identity
/// in the strongest sense this repository has: the identity is authenticated into every value sealed under it, so it is
/// written into the database and stays there for the life of the row. An enum member's ordinal means nothing outside
/// the assembly and its name changes with an ordinary rename — either would make every value sealed under the previous
/// spelling fail to open, and the failure would appear at the next read rather than at the rename.
/// </para>
/// <para>
/// Sharing one key ring across several kinds of value is only safe because this identity is bound in. Two values sealed
/// under the same key for different purposes do not open as one another, so a stored refresh token cannot be replayed
/// into a column that means something else, and a future sealed column needs no key of its own.
/// </para>
/// <para>
/// An identity is allocated once and never reused or respelled. Being a struct, <see langword="default" /> is reachable
/// and names no purpose; <see cref="DataEncryptionBinding" /> is where it is rejected, because that is the last point
/// before a value is bound to something meaningless. See
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0005-data-encryption-key-ring-and-provisioning.md">ADR 0005</see>.
/// </para>
/// </remarks>
[JsonConverter(typeof(DataEncryptionPurposeJsonConverter))]
public readonly record struct DataEncryptionPurpose
{
    private readonly string? identity;

    private DataEncryptionPurpose(string identity) => this.identity = identity;

    /// <summary>Gets the purpose of the OAuth refresh token MailFathom stores for a mailbox account.</summary>
    /// <remarks>The subject of a value sealed under this purpose is the account identifier, which is MailFathom's own configured name for the account and carries no personal data.</remarks>
    public static DataEncryptionPurpose MailboxRefreshToken { get; } = new("mailbox-refresh-token");

    /// <summary>Gets every supported purpose.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<DataEncryptionPurpose> All { get; } = [MailboxRefreshToken];

    /// <summary>Gets whether this value names a supported purpose rather than the unusable struct default.</summary>
    public bool IsSpecified => this.identity is not null;

    /// <summary>Gets the identity authenticated into every value sealed under this purpose.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a purpose.</exception>
    public string Identity => this.identity
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name an encryption purpose.");

    /// <summary>Parses a stored or configured identity.</summary>
    /// <param name="identity">The identity to match, compared exactly because it is authenticated material rather than operator input.</param>
    /// <param name="purpose">The parsed purpose when the identity is supported; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the identity names a supported purpose; otherwise <see langword="false" />.</returns>
    /// <remarks>An identity nothing declares is unknown rather than new, so no value is ever reconstructed from one.</remarks>
    public static bool TryParse(string? identity, out DataEncryptionPurpose purpose)
    {
        purpose = default;
        if (string.IsNullOrEmpty(identity))
        {
            return false;
        }

        purpose = All.FirstOrDefault(candidate => string.Equals(candidate.Identity, identity, StringComparison.Ordinal));

        return purpose.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.identity ?? "(unspecified)";
}

/// <summary>Serializes <see cref="DataEncryptionPurpose" /> as its published identity.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the identity rather than an ordinal for the same
/// reason the value object exists: the identity is what a sealed value already carries, and an ordinal would change
/// meaning the moment the supported set were reordered.
/// </remarks>
public sealed class DataEncryptionPurposeJsonConverter : JsonConverter<DataEncryptionPurpose>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a supported purpose.</exception>
    public override DataEncryptionPurpose Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"An encryption purpose must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, DataEncryptionPurpose value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(IdentityOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a supported purpose.</exception>
    public override DataEncryptionPurpose ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        DataEncryptionPurpose value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(IdentityOrThrow(value));
    }

    private static DataEncryptionPurpose ParseOrThrow(string? identity) =>
        DataEncryptionPurpose.TryParse(identity, out var purpose)
            ? purpose
            : throw new JsonException($"'{identity}' does not name a supported encryption purpose.");

    private static string IdentityOrThrow(DataEncryptionPurpose value) =>
        value.IsSpecified
            ? value.Identity
            : throw new JsonException("The unspecified default of the encryption purpose cannot be serialized.");
}

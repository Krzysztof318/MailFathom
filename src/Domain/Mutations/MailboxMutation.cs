// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Domain.Mutations;

/// <summary>Names one change MailFathom is permitted to make to a remote mailbox.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the name is the
/// identity: it is what a log line records, what a span is called, and what a counter is broken down by, so an operator
/// reading any of the three sees the same word. An enum member's ordinal would carry no meaning outside this assembly,
/// and a name derived from the member would change with every rename of the operation it belongs to.
/// </para>
/// <para>
/// The set is closed because it is the answer to a decision rather than a list that grows with call sites. Sending,
/// every flag other than <c>\Seen</c>, and renaming, deleting, or unsubscribing a folder are refused, and permitting
/// one of them is a decision to reopen rather than a member to append. Creating a folder the operator configured was
/// reopened and permitted, and is still not a member here: it changes the shape of a mailbox rather than a message in
/// one, so it is a capability of its own.
/// </para>
/// <para>
/// A mutation names what was asked for, never how the server was made to do it. A relocation carried by
/// <c>MOVE</c> and a relocation carried by copy, flag, and expunge are the same value here, which is what lets the
/// layer above be unable to tell them apart. Being a struct, <see langword="default" /> is reachable and names no
/// mutation; <see cref="IsSpecified" /> reports that.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailboxMutationJsonConverter))]
public readonly record struct MailboxMutation
{
    private readonly string? name;

    private MailboxMutation(string name) => this.name = name;

    /// <summary>Gets the mutation that moves one email out of its folder and into another.</summary>
    public static MailboxMutation Relocate { get; } = new("relocate");

    /// <summary>Gets the mutation that removes one email from the folder it is in.</summary>
    public static MailboxMutation Delete { get; } = new("delete");

    /// <summary>Gets the mutation that sets or clears the remote <c>\Seen</c> flag of one email.</summary>
    /// <remarks>
    /// It is the one mutation whose purpose is to write that flag, and the only path permitted to. Reading mail still
    /// never sets it, which is what keeps the stored value a snapshot of what the server reports.
    /// </remarks>
    public static MailboxMutation SetSeen { get; } = new("set-seen");

    /// <summary>Gets the mutation that puts a second live occurrence of one email into another folder.</summary>
    public static MailboxMutation Copy { get; } = new("copy");

    /// <summary>Gets every permitted mutation.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailboxMutation> All { get; } = [Relocate, Delete, SetSeen, Copy];

    /// <summary>Gets whether this value names a permitted mutation rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the name a log line, a span, and a counter dimension all use for the mutation.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a mutation.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no mailbox mutation.");

    /// <summary>Parses a recorded name back into the mutation it names.</summary>
    /// <param name="name">The name read from a log, a stored record, or a serialized document.</param>
    /// <param name="mutation">The parsed mutation when the name is permitted; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a permitted mutation; otherwise <see langword="false" />.</returns>
    /// <remarks>A name this set does not hold is not accepted, so a mutation that was never permitted is recognized as unknown rather than reconstructed as a value nothing performs.</remarks>
    public static bool TryParseName(string? name, out MailboxMutation mutation)
    {
        mutation = default;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name.Trim();

        // No permitted mutation is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        mutation = All.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, normalizedName));

        return mutation.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="MailboxMutation" /> as its name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the name for the same reason the value object exists:
/// it is the identity an operator already reads in a log and a metric, and an ordinal would change meaning silently if
/// the permitted set were ever reordered.
/// </remarks>
public sealed class MailboxMutationJsonConverter : JsonConverter<MailboxMutation>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a permitted mutation.</exception>
    public override MailboxMutation Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A mailbox mutation must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        MailboxMutation value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a permitted mutation.</exception>
    public override MailboxMutation ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailboxMutation value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static MailboxMutation ParseOrThrow(string? name)
    {
        if (!MailboxMutation.TryParseName(name, out var mutation))
        {
            throw new JsonException($"'{name}' does not name a permitted mailbox mutation.");
        }

        return mutation;
    }

    private static string NameOrThrow(MailboxMutation mutation) => mutation.IsSpecified
        ? mutation.Name
        : throw new JsonException("An unspecified mailbox mutation cannot be serialized.");
}

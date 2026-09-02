// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json;
using System.Text.Json.Serialization;
using MailFathom.Domain.Folders;

namespace MailFathom.Domain.Delivery.Filing;

/// <summary>Names one place an outgoing message belongs in the mailbox, and what its copy looks like once it is there.</summary>
/// <remarks>
/// <para>
/// A message MailFathom authors has a place in the mailbox at every stage of its life, and this is that stage read as
/// a filing: which role the destination folder plays and which flags the appended copy carries. Both travel together
/// because they are one answer — <c>\Draft</c> in the drafts folder and <c>\Seen</c> in the sent folder are not two
/// independent settings, they are what each of those folders means — and separating them would let a caller file a
/// draft as read.
/// </para>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the name is the
/// identity: it is what the durable filing row stores, what a log line records, and what a counter is broken down by.
/// The set is closed for the reason <see cref="Mutations.MailboxMutation" />'s is, and a fourth place an outgoing
/// message can be is a member to append rather than a second filing mechanism to build.
/// </para>
/// <para>
/// <see cref="Held" /> is the one member naming a role no mail server publishes. RFC 6154 defines the special-use
/// attributes a server may advertise and there is no <c>\Outbox</c> among them, because the outbox a mail client shows
/// is that client's own local queue of what it has not managed to send yet. MailFathom's outbox is the durable
/// outgoing record, which is the truth about what will be sent; this member is the optional mirror of it into a folder
/// an operator chose, for the message that will sit there long enough to be worth seeing in a mail client.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and names no filing. <see cref="IsSpecified" /> reports that.
/// </para>
/// </remarks>
[JsonConverter(typeof(OutgoingMailFilingJsonConverter))]
public readonly record struct OutgoingMailFiling
{
    private readonly string? name;

    private OutgoingMailFiling(string name, MailFolderSpecialUse role, AppendedMailFlags flags)
    {
        this.name = name;
        this.Role = role;
        this.Flags = flags;
    }

    /// <summary>Gets the filing of a message the owner is still composing, into the drafts folder.</summary>
    public static OutgoingMailFiling Draft { get; } =
        new("draft", MailFolderSpecialUse.Drafts, AppendedMailFlags.Draft);

    /// <summary>Gets the filing of a message that is waiting to go out, into the folder an operator mapped as the outbox.</summary>
    /// <remarks>
    /// It carries <c>\Draft</c> for the same reason a draft does — the message has not left — and the copy is withdrawn
    /// when it does leave. Deleting that copy in a mail client cancels nothing: the outgoing record is what will be
    /// sent, and withdrawing a send is its own command.
    /// </remarks>
    public static OutgoingMailFiling Held { get; } =
        new("held", MailFolderSpecialUse.Outbox, AppendedMailFlags.Draft);

    /// <summary>Gets the filing of a message a submission server has already accepted, into the sent folder.</summary>
    public static OutgoingMailFiling Sent { get; } =
        new("sent", MailFolderSpecialUse.Sent, AppendedMailFlags.Seen);

    /// <summary>Gets every place an outgoing message is filed.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<OutgoingMailFiling> All { get; } = [Draft, Held, Sent];

    /// <summary>Gets the role the destination folder plays, which is what identifies it on any account in any language.</summary>
    public MailFolderSpecialUse Role { get; }

    /// <summary>Gets the flags the appended copy carries.</summary>
    public AppendedMailFlags Flags { get; }

    /// <summary>Gets whether this value names a filing rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets whether the copy is withdrawn once the message leaves the stage this filing describes.</summary>
    /// <remarks>
    /// Only the outbox mirror is. A draft and a sent copy are what the owner keeps; the mirror exists to show a message
    /// that has not gone yet, so leaving it behind after the message went would show an outbox that never drains.
    /// </remarks>
    public bool IsWithdrawnWhenTheMessageLeaves => this == Held;

    /// <summary>Gets the name a durable row, a log line, and a counter dimension all use for the filing.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a filing.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and names no outgoing mail filing.");

    /// <summary>Parses a recorded name back into the filing it names.</summary>
    /// <param name="name">The name read from a stored row, a log, or a serialized document.</param>
    /// <param name="filing">The parsed filing when the name is one this set holds; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is a filing this set holds; otherwise <see langword="false" />.</returns>
    public static bool TryParseName(string? name, out OutgoingMailFiling filing)
    {
        filing = default;

        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name.Trim();

        // No filing this set holds is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        filing = All.FirstOrDefault(candidate => StringComparer.Ordinal.Equals(candidate.Name, normalizedName));

        return filing.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="OutgoingMailFiling" /> as its name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration, and the JSON form is the same name the durable row stores.
/// </remarks>
public sealed class OutgoingMailFilingJsonConverter : JsonConverter<OutgoingMailFiling>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a filing.</exception>
    public override OutgoingMailFiling Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"An outgoing mail filing must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(
        Utf8JsonWriter writer,
        OutgoingMailFiling value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a filing.</exception>
    public override OutgoingMailFiling ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        OutgoingMailFiling value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static OutgoingMailFiling ParseOrThrow(string? name)
    {
        if (!OutgoingMailFiling.TryParseName(name, out var filing))
        {
            throw new JsonException($"'{name}' does not name an outgoing mail filing.");
        }

        return filing;
    }

    private static string NameOrThrow(OutgoingMailFiling filing) => filing.IsSpecified
        ? filing.Name
        : throw new JsonException("An unspecified outgoing mail filing cannot be serialized.");
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MailFathom.Domain.Access;

/// <summary>One named capability MailFathom publishes, which a grant lists and a caller either holds or does not.</summary>
/// <remarks>
/// <para>
/// The type is a closed enumeration of values rather than a C# <see langword="enum" />, because the name is the
/// identity and it travels outside this process: an operator writes it in configuration, a deployment advertises it in
/// its protected resource metadata document, and an authorization server mints it as a scope so a token can carry it.
/// A member's ordinal would mean nothing to any of the three, and its C# name has to be free to change without moving
/// what an operator already wrote.
/// </para>
/// <para>
/// The set is closed so that every name a grant can carry corresponds to a check that exists. A name nothing publishes
/// is unknown rather than new, which is what lets startup refuse a misspelling instead of accepting a grant nobody
/// enforces. Adding a member is a configuration-schema change and is made when the capability it names exists, never
/// ahead of it.
/// </para>
/// <para>
/// A name is <c>mailfathom.&lt;surface&gt;[.&lt;subject&gt;].&lt;verb&gt;</c>, lowercase and dot-separated, and is
/// always a valid OAuth scope token so the same string can travel in a <c>scope</c> claim. The prefix after
/// <c>mailfathom.</c> names the <see cref="Surface" /> the permission belongs to, and the two halves are disjoint: no
/// permission implies another, and holding one says nothing about holding the next.
/// </para>
/// <para>
/// Being a struct, <see langword="default" /> is reachable and is not a permission. It reports itself through
/// <see cref="IsSpecified" />, refuses to answer for a name, and is rejected by the JSON converter below; a grant is
/// composed from <see cref="TryParse" /> or from the members themselves, so no undeclared value can reach one.
/// </para>
/// </remarks>
[JsonConverter(typeof(MailFathomPermissionJsonConverter))]
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix", Justification = "The suffix is reserved for code access security, which .NET removed; permission is the word ADR 0012 fixed for this unit and the word an operator writes in configuration.")]
public readonly record struct MailFathomPermission
{
    private readonly string? name;

    private MailFathomPermission(string name, ProtectedSurface surface)
    {
        this.name = name;
        this.Surface = surface;
    }

    #region Mail

    /// <summary>Gets the permission covering the tools that read the local mailbox copy.</summary>
    /// <remarks>
    /// It is not an egress-free grant. Where semantic retrieval is configured, searching places the caller's own query
    /// text with the embedding provider, which is why the pages describing the grant say so where an operator writes
    /// it. What it does not permit is sending mail content to a chat provider, which is <see cref="MailAsk" />.
    /// </remarks>
    public static MailFathomPermission MailRead { get; } = new("mailfathom.mail.read", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering the tool that answers from mail content by sending it to a model provider.</summary>
    /// <remarks>It does not imply <see cref="MailRead" /> and is not the weaker of the two: a cited answer returns mail content, so granting it is granting access to mail.</remarks>
    public static MailFathomPermission MailAsk { get; } = new("mailfathom.mail.ask", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering the tools that read the contact book.</summary>
    /// <remarks>
    /// The book is an assembled record about identified third parties rather than mail that arrived, which is why it is
    /// granted apart from <see cref="MailRead" /> instead of travelling with it: a deployment may want an agent able to
    /// name the people it reads about without that following from the grant that lets it read the mailbox.
    /// </remarks>
    public static MailFathomPermission MailContactsRead { get; } = new("mailfathom.mail.contacts.read", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering the tools that record, amend, and erase a contact.</summary>
    /// <remarks>
    /// It does not imply <see cref="MailContactsRead" />, as no permission implies another. It is the grant that lets a
    /// caller change what this deployment holds about a person, erasure included, so a deployment that wants the book
    /// looked up and not edited writes the reading half alone.
    /// </remarks>
    public static MailFathomPermission MailContactsWrite { get; } = new("mailfathom.mail.contacts.write", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering the tool that writes <c>\Seen</c>, <c>\Flagged</c>, and keywords onto mail this deployment holds.</summary>
    /// <remarks>
    /// <para>
    /// It is the first grant on this surface that reaches somebody's mail server rather than the local copy, which is
    /// why it is its own and does not follow from <see cref="MailRead" />: a deployment that lets an agent read the
    /// mailbox has not thereby let it change what the owner sees in their own client. Nothing about it widens what may
    /// be read, either, since no permission implies another.
    /// </para>
    /// <para>
    /// The three values are one grant because they are one act — a <c>STORE</c> against one message, in a direction the
    /// caller stated, undone in any mail client with the gesture that would have made it. Splitting them would offer a
    /// deployment a combination whose only effect is mail accumulating labels nothing may take off again, which is the
    /// reasoning the account's own switches already follow.
    /// </para>
    /// </remarks>
    public static MailFathomPermission MailFlagsWrite { get; } = new("mailfathom.mail.flags.write", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering writing, editing, and giving up a draft this deployment holds.</summary>
    /// <remarks>
    /// <para>
    /// It is the safe half of authoring mail, and it is its own name because that half is worth granting on its own. A
    /// draft is delivered to nobody, can be withdrawn by deleting it, and lands in a folder the owner already reads —
    /// so an agent holding this and nothing else can prepare mail whose worst failure is a message in Drafts, which is
    /// the arrangement <see cref="MailSend" /> is too strong to describe.
    /// </para>
    /// <para>
    /// It does not imply <see cref="MailSend" /> and is not implied by it, as no permission here implies another. A
    /// deployment that means an agent to draft and to send writes both, and one that means it to draft alone writes
    /// this one: promoting a draft is asking for mail to leave, so it is admitted under the sending grant wherever it
    /// is reached.
    /// </para>
    /// <para>
    /// Its effect does leave the deployment, unlike every other grant that is not <see cref="MailSend" />: a draft is
    /// appended to the owner's own drafts folder on their own mail server, which is <see cref="MailFlagsWrite" />'s
    /// reach rather than a send's.
    /// </para>
    /// </remarks>
    public static MailFathomPermission MailDraftsWrite { get; } = new("mailfathom.mail.drafts.write", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering asking this deployment to send mail from an account it holds.</summary>
    /// <remarks>
    /// <para>
    /// It is the one grant on this surface whose effect leaves the deployment and cannot be withdrawn: a message that
    /// reached somebody else's mailbox is not recallable by any act available here. That is why it is its own name and
    /// follows from nothing — reading a mailbox is not writing from it, and <see cref="MailFlagsWrite" /> reaches the
    /// owner's own mail server rather than a stranger's.
    /// </para>
    /// <para>
    /// It permits asking rather than sending: what it reaches writes a send down durably, and a delivery pass is what
    /// transmits it. It says nothing about which account a caller may send from either, since bounding a credential to
    /// particular accounts is outside this model.
    /// </para>
    /// </remarks>
    public static MailFathomPermission MailSend { get; } = new("mailfathom.mail.send", ProtectedSurface.Mail);

    /// <summary>Gets the permission covering an owner maintaining the mail accounts their own record declares.</summary>
    /// <remarks>
    /// <para>
    /// The one grant on this surface that changes what the deployment does rather than what it holds: withdrawing a
    /// mail account stops a mailbox being synchronized, and declaring one points this deployment at a mail server and
    /// gives it a credential reference to authenticate with. That is why it follows from nothing —
    /// <see cref="MailRead" /> is a person reading their own mail, and nothing about reading implies deciding which
    /// mailboxes are read at all.
    /// </para>
    /// <para>
    /// It reaches one owner's own record and cannot reach another's. Whose record it is comes from the principal
    /// rather than from the request, so the grant says what may be done and never to whom, exactly as every other name
    /// on this surface does.
    /// </para>
    /// <para>
    /// It is deliberately not the administrative <see cref="AdminConfigurationWrite" /> under another name. That one
    /// decides what the deployment is — the endpoints it opens, the grants it honours, the model it bills — and an
    /// owner holds none of it; this one decides which mailboxes are that person's, which is the only configuration
    /// that is theirs at all.
    /// </para>
    /// </remarks>
    public static MailFathomPermission MailAccountsWrite { get; } =
        new("mailfathom.mail.accounts.write", ProtectedSurface.Mail);

    #endregion

    #region Administration

    /// <summary>Gets the permission covering the administrative reads that report the deployment's own state and no mail.</summary>
    public static MailFathomPermission AdminRead { get; } = new("mailfathom.admin.read", ProtectedSurface.Administration);

    /// <summary>Gets the permission covering the per-account records derived from mail: the audits, the rules history, and the spam classifications.</summary>
    public static MailFathomPermission AdminAuditRead { get; } = new("mailfathom.admin.audit.read", ProtectedSurface.Administration);

    /// <summary>Gets the permission covering asking the deployment to do work it can already do.</summary>
    public static MailFathomPermission AdminOperate { get; } = new("mailfathom.admin.operate", ProtectedSurface.Administration);

    /// <summary>Gets the permission covering storing a mailbox refresh token.</summary>
    public static MailFathomPermission AdminCredentialsWrite { get; } = new("mailfathom.admin.credentials.write", ProtectedSurface.Administration);

    /// <summary>Gets the permission covering the one operation that starts a provider bill, which is activating the declared embedding model.</summary>
    public static MailFathomPermission AdminSpend { get; } = new("mailfathom.admin.spend", ProtectedSurface.Administration);

    /// <summary>Gets the permission covering erasing the mail stored for a folder an account no longer mirrors.</summary>
    public static MailFathomPermission AdminErase { get; } = new("mailfathom.admin.erase", ProtectedSurface.Administration);

    /// <summary>Gets the permission covering changing the deployment's own persisted configuration.</summary>
    /// <remarks>
    /// A name of its own rather than a route under the operating one, because a persisted setting decides what the
    /// deployment is rather than what it does next. The write that corrects a search bound is the write that widens a
    /// credential's grant or repoints a model provider, so a caller holding this holds everything this surface
    /// publishes — which is exactly what an operator has to be able to withhold from the credential they granted the
    /// ordinary operating work to.
    /// </remarks>
    public static MailFathomPermission AdminConfigurationWrite { get; } =
        new("mailfathom.admin.configuration.write", ProtectedSurface.Administration);

    #endregion

    /// <summary>Gets every published permission.</summary>
    /// <remarks>Declared last so the members it lists are already initialized when this initializer runs.</remarks>
    public static IReadOnlyList<MailFathomPermission> All { get; } =
    [
        MailRead,
        MailAsk,
        MailContactsRead,
        MailContactsWrite,
        MailFlagsWrite,
        MailDraftsWrite,
        MailSend,
        MailAccountsWrite,
        AdminRead,
        AdminAuditRead,
        AdminOperate,
        AdminCredentialsWrite,
        AdminSpend,
        AdminErase,
        AdminConfigurationWrite,
    ];

    /// <summary>Gets whether this value names a published permission rather than the unusable struct default.</summary>
    public bool IsSpecified => this.name is not null;

    /// <summary>Gets the surface this permission belongs to.</summary>
    /// <remarks>The struct default reports <see cref="ProtectedSurface.Mail" /> like any other unset enum field, so ask <see cref="IsSpecified" /> before reading it.</remarks>
    public ProtectedSurface Surface { get; }

    /// <summary>Gets the published name, which is what an operator writes and what a token carries as a scope.</summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is the struct default rather than a permission.</exception>
    public string Name => this.name
        ?? throw new InvalidOperationException("The value is the default of the struct and does not name a permission.");

    /// <summary>Reports every permission one surface publishes, which is what a grant nobody narrowed reaches.</summary>
    /// <param name="surface">The surface whose half is being asked for.</param>
    /// <returns>The permissions belonging to that surface, in declaration order.</returns>
    public static IReadOnlyList<MailFathomPermission> PublishedFor(ProtectedSurface surface) =>
        [.. All.Where(permission => permission.Surface == surface)];

    /// <summary>Parses an operator-supplied or token-supplied permission name.</summary>
    /// <param name="name">The written name.</param>
    /// <param name="permission">The parsed permission when the name is published; otherwise the unspecified default.</param>
    /// <returns><see langword="true" /> when the name is one this repository publishes; otherwise <see langword="false" />.</returns>
    /// <remarks>
    /// The comparison is exact and neither trimmed nor case-folded, because the same string has to travel as an OAuth
    /// scope token, where a scope is compared byte for byte. Accepting a spelling here that an authorization server
    /// would treat as a different scope is how a grant comes to mean one thing in configuration and another in a token.
    /// </remarks>
    public static bool TryParse(string? name, out MailFathomPermission permission)
    {
        // No published permission is the struct default, so an unmatched name yields the unspecified value the caller
        // already receives when parsing fails.
        permission = name is null
            ? default
            : All.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));

        return permission.IsSpecified;
    }

    /// <inheritdoc />
    public override string ToString() => this.name ?? "(unspecified)";
}

/// <summary>Serializes <see cref="MailFathomPermission" /> as its published name.</summary>
/// <remarks>
/// The type carries this converter through <see cref="JsonConverterAttribute" />, so every serializer that meets the
/// value uses it without per-call registration. The JSON form is the published name for the same reason the value
/// object exists: it is the identity an operator, a metadata document, and an authorization server already agree on,
/// while an ordinal would silently change meaning the first time the set gained a member.
/// </remarks>
public sealed class MailFathomPermissionJsonConverter : JsonConverter<MailFathomPermission>
{
    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the token is not a string or does not name a published permission.</exception>
    public override MailFathomPermission Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException($"A permission must be a JSON string, but the token was {reader.TokenType}.");
        }

        return ParseOrThrow(reader.GetString());
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void Write(Utf8JsonWriter writer, MailFathomPermission value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WriteStringValue(NameOrThrow(value));
    }

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the property name does not name a published permission.</exception>
    public override MailFathomPermission ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) => ParseOrThrow(reader.GetString());

    /// <inheritdoc />
    /// <exception cref="JsonException">Thrown when the value is the unspecified struct default.</exception>
    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        MailFathomPermission value,
        JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        writer.WritePropertyName(NameOrThrow(value));
    }

    private static MailFathomPermission ParseOrThrow(string? name)
    {
        if (!MailFathomPermission.TryParse(name, out var permission))
        {
            throw new JsonException($"'{name}' is not a permission MailFathom publishes.");
        }

        return permission;
    }

    private static string NameOrThrow(MailFathomPermission permission) => permission.IsSpecified
        ? permission.Name
        : throw new JsonException("An unspecified permission cannot be serialized.");
}

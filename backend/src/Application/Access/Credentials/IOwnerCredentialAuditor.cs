// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access.Credentials;

/// <summary>Records that an administrator changed which credentials authenticate an owner.</summary>
/// <remarks>
/// <para>
/// Provisioning, rotating, disabling, and deleting a credential each change who can reach one person's mail, and none
/// of them leaves a trace in the mail graph. The record is the explanation an operator reads afterwards, and it names
/// the act, the credential, the owner, and who asked — which is everything a later question about "when did this
/// password stop working" can be answered from.
/// </para>
/// <para>
/// It deliberately cannot carry the lookup. A lookup is what a caller presents — a username typed into a client, a
/// key's digest, a client's public key, a subject an authorization server issued — so the identifier is what is
/// written down instead: a listing resolves the two for whoever is entitled to read one, and a record that named both
/// would put a live credential's name into whichever sink this port ends up writing to.
/// </para>
/// <para>
/// The port is deliberately narrow for the reason <see cref="Folders.IMailFolderMappingChangeAuditor" /> is: the sink
/// behind it is undecided — structured logging today, an audit table or an external evidence store once compliance
/// evidence is collected — and one operation is all a caller may reach for.
/// </para>
/// </remarks>
public interface IOwnerCredentialAuditor
{
    /// <summary>Records one change to an owner's credentials.</summary>
    /// <param name="change">The act, the credential, the owner, and the administrator who asked.</param>
    /// <param name="cancellationToken">Cancels writing the record.</param>
    /// <returns>A task that completes once the record is durable for the configured sink.</returns>
    Task RecordCredentialChangeAsync(OwnerCredentialChange change, CancellationToken cancellationToken);
}

/// <summary>One administrative change to the credentials an owner holds.</summary>
/// <param name="Act">What was done.</param>
/// <param name="CredentialId">The credential it was done to.</param>
/// <param name="Owner">The owner the credential authenticates.</param>
/// <param name="Method">How the credential is presented, which is what makes two records about one owner readable apart, and <see langword="null" /> for the acts written against the identifier alone.</param>
/// <param name="ActingAdministrator">What the administrative surface admitted the caller as, which is a configured credential's own name rather than a person.</param>
/// <param name="OccurredAt">When the change committed.</param>
/// <remarks>
/// <paramref name="Method" /> is optional because three of the five acts are the same act whichever credential they
/// reached: enabling one, disabling one, and deleting one are asked for by identifier and name no method. Carrying the
/// unspecified <see cref="OwnerCredentialMethod" /> there instead would hand every sink a value whose published name
/// throws and which no serializer will write, and it would do so after the store write had already committed.
/// </remarks>
public sealed record OwnerCredentialChange(
    OwnerCredentialAct Act,
    Guid CredentialId,
    MailOwnerId Owner,
    OwnerCredentialMethod? Method,
    string ActingAdministrator,
    DateTimeOffset OccurredAt);

/// <summary>Which administrative act a credential record describes.</summary>
public enum OwnerCredentialAct
{
    /// <summary>A credential was created, with material nothing can read back where the method keeps any.</summary>
    Provisioned = 0,

    /// <summary>An existing credential's material was replaced, and what it was presented as before stopped working at that instant.</summary>
    MaterialRotated = 1,

    /// <summary>An existing credential stopped authenticating requests while keeping its lookup and its material.</summary>
    Disabled = 2,

    /// <summary>A disabled credential started authenticating requests again under the material it already held.</summary>
    Enabled = 3,

    /// <summary>A credential was removed, which freed its lookup for another.</summary>
    Deleted = 4,
}

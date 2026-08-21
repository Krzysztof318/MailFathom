// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Synchronization.Sessions;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Transport;

namespace MailFathom.Application.Folders;

/// <summary>Creates the one folder a mapping asked to have created, and can do nothing else to a mailbox.</summary>
/// <remarks>
/// <para>
/// The name states the port's whole surface. There is no method that renames a folder, deletes one, or unsubscribes
/// from one, and permitting any of those is a decision to reopen
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0007-remote-mailbox-mutation-boundary-and-write-session.md">ADR 0007</see>
/// rather than a method to append here.
/// </para>
/// <para>
/// It is a port of its own rather than a fifth method on <see cref="Mail.Mutations.IMailboxWriteSession" />, which is
/// what buys the same separation the write session itself buys: a component that can file a message into a folder
/// cannot create one, and a component that can create one cannot relocate, delete, flag, or copy a message. No second
/// connection is opened for it — a creation is issued over the account's single write connection, so an account still
/// holds at most one connection able to change its mailbox.
/// </para>
/// <para>
/// Nothing reaches this port from configuration binding, from mail content, from a tool argument, or from model output.
/// A path arrives here from the <c>RemotePath</c> of a mapping the operator wrote and from nowhere else, which is the
/// input class the decision's authorization review turns on.
/// </para>
/// </remarks>
public interface IRemoteFolderCreator
{
    /// <summary>Creates the folder a configured path names, together with the ancestors that path names above it.</summary>
    /// <param name="accountId">The account whose mailbox gains the folder.</param>
    /// <param name="folderAlias">The folder alias the path was configured under, which is the name every failure reports.</param>
    /// <param name="configuredPath">The path the operator wrote, which is used exactly as written and never rewritten.</param>
    /// <param name="transportSecurityPolicy">The connection and authentication policy the implementation must obey.</param>
    /// <param name="cancellationToken">Cancels waiting for the account's write connection, connecting, authenticating, and creating.</param>
    /// <returns>The created folder as the server advertises it, with the hierarchy delimiter the server reported.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transportSecurityPolicy" /> is <see langword="null" />.</exception>
    /// <exception cref="RemoteFolderCreationRefusedException">Thrown when the mail server answered and refused to hold a folder at that path.</exception>
    /// <exception cref="MailboxUnavailableException">Thrown when the mail server did not serve the creation within its configured resilience budget.</exception>
    /// <remarks>
    /// <para>
    /// A folder already at that path is the successful answer rather than a failure, because another mail client — or
    /// another MailFathom process — may have created it between the listing that found nothing and this attempt. The
    /// path comes back as the server advertises it either way, so a caller binds a created folder exactly as it binds a
    /// discovered one.
    /// </para>
    /// <para>
    /// The folder is subscribed to as part of creating it, so it appears in a mail client that lists subscriptions and
    /// the operator can find the mail a rule files there. A server that refuses the subscription does not fail the
    /// creation: the folder exists, which is what was asked for.
    /// </para>
    /// </remarks>
    Task<RemoteFolderPath> CreateFolderAsync(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        RemoteFolderPath configuredPath,
        MailTransportSecurityPolicy transportSecurityPolicy,
        CancellationToken cancellationToken);
}

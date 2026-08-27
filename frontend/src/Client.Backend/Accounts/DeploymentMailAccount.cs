// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend.Accounts;

/// <summary>One of the signed-in owner's mail accounts, and how current the deployment's copy of it is.</summary>
/// <param name="Id">The identifier the account was declared under, unique within the owner rather than across the deployment.</param>
/// <param name="DisplayName">The name the account is published under, which is what a person recognizes.</param>
/// <param name="SynchronizationState">The standing as the deployment names it, kept as the word that arrived so an unknown one is readable rather than lost.</param>
/// <param name="LastSynchronizedAt">When the account last durably took anything in, or <see langword="null" /> where it never has.</param>
/// <param name="Behind">Whether any of the account's folders ended its last attempt with mail it had not yet taken in.</param>
/// <remarks>
/// <para>
/// The client's own record for the wire shape rather than a type shared with the service, for the reason
/// <see cref="DeploymentSession" /> is one. Nothing of the mailbox is modelled here because nothing of it is served:
/// no message, no subject, no correspondent, no folder, and no mail server, port, user name, or credential. What
/// <c>Id</c> and <c>DisplayName</c> carry are MailFathom's own names for a mailbox, which is why they may be put in
/// front of somebody without an address being.
/// </para>
/// <para>
/// The standing, the instant, and being behind answer different parts of one question and are three fields for that
/// reason. The instant says how old what is being read is; the standing says whether it is still being refreshed; and
/// being behind is neither, because a copy can be behind under any standing — an attempt that succeeded within its
/// batch budget leaves mail for the next one. An account that has been failing since yesterday and one nobody has
/// written to since yesterday carry the same instant.
/// </para>
/// </remarks>
public sealed record DeploymentMailAccount(
    string Id,
    string DisplayName,
    string SynchronizationState,
    DateTimeOffset? LastSynchronizedAt,
    bool Behind)
{
    /// <summary>Gets where this account's copy stands, as this client reads the word the deployment sent.</summary>
    /// <remarks>
    /// Matched against the published names exactly rather than parsed with <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)" />,
    /// which would also accept a number, a case that differs, and a comma-separated pair — none of which the contract
    /// publishes, and each of which would turn a document this client does not understand into a claim about somebody's
    /// mail.
    /// </remarks>
    public MailSynchronizationStanding Standing => MailSynchronizationStandings.Read(this.SynchronizationState);
}

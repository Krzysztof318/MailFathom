// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;

namespace MailFathom.Application.Mail.Mutations;

/// <summary>Indicates that a mail server advertises no extension able to carry a requested mutation safely.</summary>
/// <remarks>
/// <para>
/// It is raised rather than returned because it is not a state the immediate caller chooses between: a server that
/// cannot remove one message without removing others will not be able to tomorrow either, so the mutation is refused
/// instead of deferred to a later run. That is what separates it from
/// <see cref="Synchronization.Sessions.MailboxUnavailableException" />, which says exactly the opposite about repeating
/// the work.
/// </para>
/// <para>
/// The one shape this exists for is the expunge. RFC 3501's bare <c>EXPUNGE</c> removes every message in the folder
/// that anybody has flagged <c>\Deleted</c>, including messages another client flagged and MailFathom never saw, so it
/// is never issued. Where RFC 4315 <c>UID EXPUNGE</c> is unavailable there is no message-scoped removal to fall back
/// to, and refusing is the only answer that keeps a mail tool from deleting mail nobody asked it to.
/// </para>
/// <para>
/// The message names the account alias, the folder alias, the mutation, and the missing extension. All four are
/// MailFathom's own configured or protocol-registered names, and none of them is mail content or a remote path.
/// </para>
/// </remarks>
public sealed class MailboxMutationUnsupportedException : MailFathomException
{
    /// <summary>Initializes a new refusal naming the mutation and the extension the server does not advertise.</summary>
    /// <param name="accountId">The account whose mail server advertises no way to carry the mutation.</param>
    /// <param name="folderAlias">The folder the mutation was to be performed in.</param>
    /// <param name="mutation">The mutation that was asked for.</param>
    /// <param name="requiredCapabilityName">The IMAP extension the mutation needs and the server does not advertise.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="requiredCapabilityName" /> is blank.</exception>
    public MailboxMutationUnsupportedException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        MailboxMutation mutation,
        string requiredCapabilityName)
        : base(DescribeUnsupportedMutation(accountId, folderAlias, mutation, requiredCapabilityName))
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
        this.Mutation = mutation;
        this.RequiredCapabilityName = requiredCapabilityName;
    }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailboxMutationUnsupported;

    /// <summary>Gets the account whose mail server advertises no way to carry the mutation.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the folder the mutation was to be performed in.</summary>
    public MailFolderAlias FolderAlias { get; }

    /// <summary>Gets the mutation that was asked for.</summary>
    public MailboxMutation Mutation { get; }

    /// <summary>Gets the IMAP extension the mutation needs and the server does not advertise.</summary>
    public string RequiredCapabilityName { get; }

    private static string DescribeUnsupportedMutation(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        MailboxMutation mutation,
        string requiredCapabilityName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requiredCapabilityName);

        return $"The mail server for {accountId.Value}/{folderAlias.Value} advertises no {requiredCapabilityName}, "
            + $"which a {mutation.Name} needs in order to change only the email it was asked about.";
    }
}

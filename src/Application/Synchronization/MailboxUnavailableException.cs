// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Failures;
using MailMcp.Domain.Folders;

namespace MailMcp.Application.Synchronization;

/// <summary>Indicates that a mail server did not serve an operation within the resilience budget configured for it.</summary>
/// <remarks>
/// <para>
/// This is the failure a caller sees when the attempts, the timeouts, or the in-flight limit of the mailbox dependency
/// classes were spent: an abandoned attempt, an operation that outlived its total timeout, an open circuit, and a shed
/// execution all arrive here. The remote mailbox is unreachable for now and the same work is expected to succeed on a
/// later run.
/// </para>
/// <para>
/// It is deliberately distinct from <see cref="OperationCanceledException" />, which stays the failure of a caller that
/// stopped waiting — a host shutting down. A worker has to tell the two apart, because one says the deployment is
/// stopping and the other says one mail server is struggling.
/// </para>
/// </remarks>
public sealed class MailboxUnavailableException : MailMcpException
{
    /// <summary>Initializes a new mailbox unavailability failure naming the account and folder alias it stopped.</summary>
    /// <param name="accountId">The account whose mail server did not serve the operation.</param>
    /// <param name="folderAlias">The folder the operation was working on.</param>
    /// <param name="innerException">The rejection the resilience pipeline produced.</param>
    /// <remarks>The alias is named rather than the remote path, because a path can carry personal information and this message is logged.</remarks>
    public MailboxUnavailableException(
        MailAccountId accountId,
        MailFolderAlias folderAlias,
        Exception innerException)
        : base(
            $"The mail server for {accountId.Value}/{folderAlias.Value} did not serve the operation within its configured resilience budget.",
            innerException)
    {
        this.AccountId = accountId;
        this.FolderAlias = folderAlias;
    }

    /// <summary>Initializes a new mailbox unavailability failure for an operation that works on no single folder, such as folder discovery.</summary>
    /// <param name="accountId">The account whose mail server did not serve the operation.</param>
    /// <param name="innerException">The rejection the resilience pipeline produced.</param>
    public MailboxUnavailableException(MailAccountId accountId, Exception innerException)
        : base(
            $"The mail server for {accountId.Value} did not serve the operation within its configured resilience budget.",
            innerException) =>
        this.AccountId = accountId;

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.MailboxUnavailable;

    /// <summary>Gets the account whose mail server was unavailable.</summary>
    public MailAccountId AccountId { get; }

    /// <summary>Gets the folder the stopped operation was working on, which is absent for an operation that works on no single folder.</summary>
    /// <remarks>Absence is the account-wide constructor's meaning rather than an unsupplied value: folder discovery reaches the server on behalf of the account and no folder exists to name.</remarks>
    public MailFolderAlias? FolderAlias { get; }
}

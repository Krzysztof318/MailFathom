// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;
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
public sealed class MailboxUnavailableException : Exception
{
    /// <summary>Initializes a new mailbox unavailability failure.</summary>
    public MailboxUnavailableException()
    {
    }

    /// <summary>Initializes a new mailbox unavailability failure with a safe message.</summary>
    public MailboxUnavailableException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new mailbox unavailability failure with a safe message and inner exception.</summary>
    public MailboxUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Initializes a new mailbox unavailability failure naming the account and folder it stopped.</summary>
    /// <param name="accountId">The account whose mail server did not serve the operation.</param>
    /// <param name="folderName">The folder the operation was working on.</param>
    /// <param name="innerException">The rejection the resilience pipeline produced.</param>
    public MailboxUnavailableException(
        MailAccountId accountId,
        MailFolderName folderName,
        Exception innerException)
        : base(
            $"The mail server for {accountId.Value}/{folderName.Value} did not serve the operation within its configured resilience budget.",
            innerException)
    {
        this.AccountId = accountId;
        this.FolderName = folderName;
    }

    /// <summary>Gets the account whose mail server was unavailable, when available.</summary>
    public MailAccountId? AccountId { get; }

    /// <summary>Gets the folder the stopped operation was working on, when available.</summary>
    public MailFolderName? FolderName { get; }
}

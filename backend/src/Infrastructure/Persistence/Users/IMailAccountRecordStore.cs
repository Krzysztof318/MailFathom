// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>Reads and writes the mail accounts a deployment holds and the users each is assigned to.</summary>
/// <remarks>
/// Every write that changes what a user is served also moves that user's record version in the same transaction. The
/// roster a replica serves is composed from a user's record and the accounts assigned to them, and a replica learns
/// that it has to compose one again by the record's version moving — so a write that left the version where it was would
/// change a mailbox no replica ever republished.
/// </remarks>
public interface IMailAccountRecordStore
{
    /// <summary>Reads every account the deployment holds, with the users each is assigned to.</summary>
    /// <param name="limit">The greatest number of accounts to read.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The accounts without their settings, in the order they were created in.</returns>
    /// <remarks>The settings are left behind because a declaration may be as large as a user's whole record, and a listing reads a thousand accounts to name them.</remarks>
    Task<IReadOnlyList<MailAccountSummary>> ReadAllAsync(int limit, CancellationToken cancellationToken);

    /// <summary>Reads one account and the users it is assigned to.</summary>
    /// <param name="accountId">The account asked about.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The account, or <see langword="null" /> when the deployment holds none under that identifier.</returns>
    Task<MailAccountHolding?> ReadAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>Creates an account and assigns it to one user.</summary>
    /// <param name="user">The user the account is created for.</param>
    /// <param name="expectedUserVersion">The version of that user's record the account was judged against.</param>
    /// <param name="account">The account to create, whose version is ignored.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did; an address another account holds leaves nothing written.</returns>
    Task<MailAccountWrite> CreateAsync(
        MailUserId user,
        long expectedUserVersion,
        MailAccountRecord account,
        CancellationToken cancellationToken);

    /// <summary>Replaces an account's address, name, and settings where it still stands at the version they were composed over.</summary>
    /// <param name="account">The account as it is to stand, carrying the version it was composed over.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did.</returns>
    Task<MailAccountWrite> SaveAsync(MailAccountRecord account, CancellationToken cancellationToken);

    /// <summary>Assigns an account to one more user.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="user">The user it is assigned to.</param>
    /// <param name="expectedUserVersion">The version of that user's record the assignment was judged against.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did.</returns>
    Task<MailAccountWrite> AssignAsync(
        Guid accountId,
        MailUserId user,
        long expectedUserVersion,
        CancellationToken cancellationToken);

    /// <summary>Ends one user's assignment to an account, erasing the account and its mail when nobody else is assigned it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="user">The user whose assignment ends.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>What the write did.</returns>
    Task<MailAccountUnassignment> UnassignAsync(Guid accountId, MailUserId user, CancellationToken cancellationToken);

    /// <summary>Erases an account, every assignment to it, and everything stored for it.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="cancellationToken">Cancels the erasure before it commits.</param>
    /// <returns>Whether an account was there to erase.</returns>
    Task<bool> EraseAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <summary>One account and the users it is assigned to.</summary>
/// <param name="Account">The account.</param>
/// <param name="Users">Every user it is assigned to, which may be none only while an erasure is under way.</param>
public sealed record MailAccountHolding(MailAccountRecord Account, IReadOnlyList<MailUserId> Users);

/// <summary>One account as a listing names it: by its record's columns and the users it is assigned to, without its settings.</summary>
/// <param name="Id">The identifier the deployment generated for the account.</param>
/// <param name="EmailAddress">The address of the mailbox, or <see langword="null" /> for an account that holds none and is not served.</param>
/// <param name="DisplayName">The name the account is told apart by among the accounts one user is assigned.</param>
/// <param name="Version">The version a writer states when it changes the record.</param>
/// <param name="Users">Every user it is assigned to.</param>
public sealed record MailAccountSummary(
    Guid Id,
    string? EmailAddress,
    string DisplayName,
    long Version,
    IReadOnlyList<MailUserId> Users);

/// <summary>What one write to an account did.</summary>
/// <param name="Result">How the write ended.</param>
/// <param name="Version">The account's version once committed, or the version in force of whichever record refused it.</param>
public readonly record struct MailAccountWrite(MailAccountWriteResult Result, long Version);

/// <summary>How a write to an account ended.</summary>
public enum MailAccountWriteResult
{
    /// <summary>The write committed.</summary>
    Committed = 0,

    /// <summary>The account or the user's record had moved past the version the write was composed over.</summary>
    VersionSuperseded = 1,

    /// <summary>Another account already holds the address.</summary>
    AddressHeld = 2,

    /// <summary>The account or the user is not held.</summary>
    NotFound = 3,

    /// <summary>The write would have changed nothing, such as an assignment that already stands.</summary>
    NothingToChange = 4,
}

/// <summary>What ending one assignment did.</summary>
/// <param name="Unassigned">Whether the user was assigned the account at all.</param>
/// <param name="AccountErased">Whether the account was erased because nobody else is assigned it.</param>
public readonly record struct MailAccountUnassignment(bool Unassigned, bool AccountErased);

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.UserSettings.Administration;

namespace MailFathom.Host.Api;

/// <summary>The mail accounts this deployment holds.</summary>
/// <param name="Accounts">One entry per account, in the order they were created in.</param>
internal sealed record MailAccountListResponse(IReadOnlyList<MailAccountResponse> Accounts);

/// <summary>One mail account as an administrator reads it.</summary>
/// <param name="Id">The identifier the deployment generated for the account, which every other act names it by.</param>
/// <param name="Version">The version a save states.</param>
/// <param name="Users">The users the account is assigned to.</param>
/// <param name="Declaration">The address, the display name, and the settings, with every secret-bearing value replaced by the redaction marker.</param>
internal sealed record MailAccountResponse(
    Guid Id,
    long Version,
    IReadOnlyList<Guid> Users,
    string Declaration)
{
    /// <summary>Describes one account reading.</summary>
    /// <param name="reading">The account as the administration read it.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="reading" /> is <see langword="null" />.</exception>
    internal static MailAccountResponse For(MailAccountReading reading)
    {
        ArgumentNullException.ThrowIfNull(reading);

        return new MailAccountResponse(
            reading.Id,
            reading.Version,
            [.. reading.Users.Select(user => user.Value)],
            reading.Declaration);
    }
}

/// <summary>A mail account created for one user.</summary>
/// <param name="UserId">The user the account is created for and assigned to.</param>
/// <param name="Account">The declaration: the address, the display name, and the settings, as one JSON object.</param>
internal sealed record MailAccountCreationRequest(Guid UserId, string? Account);

/// <summary>A mail account's declaration as an editing session saved it.</summary>
/// <param name="Version">The account version the declaration was read at.</param>
/// <param name="Account">The declaration as the administrator saved it.</param>
internal sealed record MailAccountSaveRequest(long Version, string? Account);

/// <summary>The user an assignment is made to or ended for.</summary>
/// <param name="UserId">The user.</param>
internal sealed record MailAccountAssignmentRequest(Guid UserId);

/// <summary>What one write to a mail account did.</summary>
/// <param name="Committed">Whether the write moved anything.</param>
/// <param name="Version">The account version now in force, or the version of the record that refused the write.</param>
/// <param name="Code">The five-digit code naming why the write was refused, and <see langword="null" /> where nothing refused it.</param>
/// <param name="Messages">One sentence per reason the write was refused or changed nothing, and on a commit one per problem the account already carried.</param>
/// <param name="AccountId">The identifier a created account was generated under, and <see langword="null" /> for every other write.</param>
internal sealed record MailAccountWriteResponse(
    bool Committed,
    long Version,
    int? Code,
    IReadOnlyList<string> Messages,
    Guid? AccountId)
{
    /// <summary>Describes what a write did.</summary>
    /// <param name="outcome">The outcome the administration reported.</param>
    /// <param name="accountId">The identifier a created account was generated under.</param>
    /// <returns>The response body.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="outcome" /> is <see langword="null" />.</exception>
    internal static MailAccountWriteResponse For(UserRecordWriteOutcome outcome, Guid? accountId = null)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        return new MailAccountWriteResponse(
            outcome.IsCommitted,
            outcome.Version,
            outcome.Refusal.IsSpecified ? outcome.Refusal.Value : null,
            outcome.Messages,
            accountId);
    }
}

/// <summary>What ending an assignment did.</summary>
/// <param name="Unassigned">Whether the user was assigned the account at all.</param>
/// <param name="AccountErased">Whether the account and its mail were erased because nobody else is assigned it.</param>
internal sealed record MailAccountUnassignmentResponse(bool Unassigned, bool AccountErased);

/// <summary>What erasing an account did.</summary>
/// <param name="Erased">Whether this deployment held the account at all.</param>
internal sealed record MailAccountErasureResponse(bool Erased);

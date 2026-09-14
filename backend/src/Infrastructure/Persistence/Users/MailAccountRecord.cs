// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Persistence.Users;

/// <summary>One mail account as its record holds it.</summary>
/// <param name="Id">The identifier the deployment generated for the account, which every mail row names it by.</param>
/// <param name="EmailAddress">The address of the mailbox, or <see langword="null" /> for an account an upgrade could derive none for, which is not served until one is stated.</param>
/// <param name="DisplayName">The name the account is told apart by among the accounts one user is assigned.</param>
/// <param name="Document">The account's settings, as the JSON object the configuration layer binds.</param>
/// <param name="Version">The version a writer states when it changes the record.</param>
public sealed record MailAccountRecord(
    Guid Id,
    string? EmailAddress,
    string DisplayName,
    string Document,
    long Version)
{
    /// <summary>The greatest number of accounts one user is read with, which bounds the record composed for them.</summary>
    public const int MaximumAssignedPerUser = 64;

    /// <summary>The longest address an account holds, which is the longest RFC 5321 permits a path to carry.</summary>
    public const int MaximumEmailAddressLength = 320;

    /// <summary>Gets the form an address is compared in, which is what the deployment-wide uniqueness holds to.</summary>
    /// <param name="emailAddress">The address as it was written.</param>
    /// <returns>The address trimmed and upper-cased, or <see langword="null" /> where none was written.</returns>
    /// <remarks>Upper-cased for the reason a folder alias is: it is compared in a database whose collation MailFathom does not control, and the migration that first wrote this column composes the same form with <c>upper(btrim(...))</c>.</remarks>
    public static string? NormalizedFormOf(string? emailAddress) =>
        string.IsNullOrWhiteSpace(emailAddress) ? null : emailAddress.Trim().ToUpperInvariant();
}

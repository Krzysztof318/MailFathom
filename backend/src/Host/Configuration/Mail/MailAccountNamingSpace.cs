// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.ComponentModel.DataAnnotations;
using MailFathom.Domain.Accounts;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Refuses a set of mail-account declarations in which one name could select two mailboxes.</summary>
/// <remarks>
/// <para>
/// An account identifier and a display name share one naming space, and both are unique within the owner who declared
/// them rather than across the deployment — two people may each call an account <c>work</c>, and neither is refused
/// for it. So this is asked of one owner's declarations at a time, and every caller of it holds exactly that: the
/// deployment's own configuration section, where every account belongs to the one owner such a deployment serves, and
/// an owner's persisted record, where every account belongs to the owner the row is keyed by.
/// </para>
/// <para>
/// It is stated once because the two callers must not answer it differently. A rule that held for a configured
/// account and not for a persisted one would be a naming space that changed shape when the declarations moved out of
/// the file, and the ambiguity it exists to refuse would arrive with the move.
/// </para>
/// </remarks>
internal static class MailAccountNamingSpace
{
    /// <summary>Finds every name in one owner's declarations that a request could not resolve to one mailbox.</summary>
    /// <param name="accounts">The mail accounts one owner declares.</param>
    /// <param name="memberName">The member the results are reported against, which is the collection the caller bound.</param>
    /// <returns>One result per ambiguity, empty when every identifier and every name selects one account.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accounts" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="memberName" /> is <see langword="null" />, empty, or white space.</exception>
    public static IEnumerable<ValidationResult> FindCollisions(
        IReadOnlyList<MailSynchronizationAccountOptions> accounts,
        string memberName)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);

        return [.. FindIdentifierCollisions(accounts, memberName), .. FindDisplayNameCollisions(accounts, memberName)];
    }

    /// <summary>Reports every account identifier these declarations carry more than once.</summary>
    /// <remarks>
    /// Compared without regard to case, which is the rule the shared naming space already runs on: a caller may name
    /// an account by its identifier or by a display name, a display name colliding with either is refused below
    /// without regard to case, and two identifiers differing only in case would leave that same ambiguity in the half
    /// this check owns. Resolution itself stays exact — the identifier is a key somebody wrote — so what is refused
    /// here is the declaration that would make an exact match a coin toss for whoever retyped it.
    /// </remarks>
    private static IEnumerable<ValidationResult> FindIdentifierCollisions(
        IReadOnlyList<MailSynchronizationAccountOptions> accounts,
        string memberName)
    {
        var repeated = accounts
            .Where(static account => !string.IsNullOrWhiteSpace(account.AccountId))
            .Select(static account => MailAccountId.Create(account.AccountId).Value)
            .GroupBy(static accountId => accountId, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();

        if (repeated.Length > 0)
        {
            yield return new ValidationResult(
                $"An account identifier names one mailbox within its owner, and this owner declares more than one account under each of: {string.Join(", ", repeated)}. Identifiers are compared after normalization and without regard to case.",
                [memberName]);
        }
    }

    /// <summary>Reports every display name a caller could not resolve to exactly one account.</summary>
    /// <remarks>
    /// <para>
    /// A request may name an account by its identifier or by its display name, so the two spellings share one naming
    /// space and a name carried by two accounts would make a filter ambiguous. Resolution takes the first match, which
    /// means the ambiguity would not fail anything: it would quietly read the wrong mailbox, which is why it is
    /// refused here instead.
    /// </para>
    /// <para>
    /// A display name equal to its own account's identifier is not a collision. Both spellings reach the same mailbox,
    /// so nothing is ambiguous, and an operator whose identifier is already readable should not have to invent a
    /// second spelling of it.
    /// </para>
    /// </remarks>
    private static IEnumerable<ValidationResult> FindDisplayNameCollisions(
        IReadOnlyList<MailSynchronizationAccountOptions> accounts,
        string memberName)
    {
        var named = accounts
            .Where(static account => !string.IsNullOrWhiteSpace(account.AccountId) && !string.IsNullOrWhiteSpace(account.DisplayName))
            .Select(static account => (AccountId: account.AccountId.Trim(), DisplayName: account.DisplayName.Trim()))
            .ToArray();

        foreach (var account in named)
        {
            var collidesWithAnother = named.Any(other =>
                !StringComparer.Ordinal.Equals(other.AccountId, account.AccountId)
                && (StringComparer.OrdinalIgnoreCase.Equals(other.AccountId, account.DisplayName)
                    || StringComparer.OrdinalIgnoreCase.Equals(other.DisplayName, account.DisplayName)));

            if (collidesWithAnother)
            {
                yield return new ValidationResult(
                    $"Account '{account.AccountId}': the display name '{account.DisplayName}' is already the identifier or display name of another account, so a request naming it could not say which mailbox it meant.",
                    [memberName]);
            }
        }
    }
}

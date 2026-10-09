// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MailFathom.Host.Api;

/// <summary>Reads the account an administrative request named, and states the one refusal when it names none this deployment serves.</summary>
/// <remarks>
/// <para>
/// Every administrative endpoint that takes an account asks the same two questions of it — is there one, and is it one
/// this deployment configures — and every one of them answers a caller who got it wrong in the same sentence. Holding
/// that here is what keeps the sentence one sentence: the wording was copied into six files before this type existed
/// and had already drifted apart in what it echoed back.
/// </para>
/// <para>
/// What it echoes is the identifier as <see cref="MailAccountId" /> normalizes it rather than the text the request
/// carried, so a caller who padded the name with whitespace is told about the name that was looked up. The refusal
/// names what MailFathom searched for, and no request text reaches the response body unaltered.
/// </para>
/// </remarks>
internal static class AdminAccountRequest
{
    /// <summary>Reads the account a request named, or nothing when this deployment does not serve it.</summary>
    /// <param name="account">The account identifier the request carried, which may be absent or blank.</param>
    /// <param name="accounts">Reports the accounts this deployment serves.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The served account, or <see langword="null" /> when the request named none or named one this deployment does not serve.</returns>
    /// <remarks>
    /// Only the named account is read, so asking whether an operator named a served account costs one account rather
    /// than the deployment's whole set.
    /// </remarks>
    internal static async Task<MailAccountId?> ResolveAsync(
        string? account,
        IDeploymentMailAccountCatalog accounts,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(account))
        {
            return null;
        }

        var served = await accounts.ReadServedAccountsAsync([MailAccountId.Create(account)], cancellationToken);

        return served is [var named] ? named.Id : null;
    }

    /// <summary>States why the account a request named did not resolve, without echoing an empty one.</summary>
    /// <param name="account">The account identifier the request carried.</param>
    /// <returns><see cref="Missing" /> for an absent or blank account, and <see cref="Unknown" /> for one this deployment does not serve.</returns>
    internal static ProblemHttpResult Refuse(string? account) => string.IsNullOrWhiteSpace(account)
        ? Missing()
        : Unknown(MailAccountId.Create(account).Value);

    /// <summary>States that the request named no account at all, where naming one is what the endpoint asks for.</summary>
    /// <returns>The refusal.</returns>
    internal static ProblemHttpResult Missing() => TypedResults.Problem(
        "The request named no mail account.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States that an account filter was present and empty, where leaving it out reads every account.</summary>
    /// <returns>The refusal.</returns>
    internal static ProblemHttpResult MissingFilter() => TypedResults.Problem(
        "The account filter named no mail account. Leave it out to read every account.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>States that the account a request named is not one this deployment serves.</summary>
    /// <param name="account">The normalized identifier that was looked up.</param>
    /// <returns>The refusal.</returns>
    internal static ProblemHttpResult Unknown(string account) => TypedResults.Problem(
        $"This deployment configures no mail account named '{account}'.",
        statusCode: StatusCodes.Status400BadRequest);

    /// <summary>Reads an optional account filter, which narrows a reading to one account or leaves it across every account.</summary>
    /// <param name="account">The account filter the request carried, absent for every account.</param>
    /// <param name="accounts">Reports the accounts this deployment serves.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Every account, the account to narrow to, or what the caller is told when the filter is present and names no served account.</returns>
    /// <remarks>
    /// An absent filter is every account rather than a refusal, which is why this is a form of its own: a present filter
    /// is held to the same two questions as a required account, and only the absent case differs.
    /// </remarks>
    internal static async Task<AccountFilter> ResolveFilterAsync(
        string? account,
        IDeploymentMailAccountCatalog accounts,
        CancellationToken cancellationToken)
    {
        if (account is null)
        {
            return new AccountFilter.Everything();
        }

        if (await ResolveAsync(account, accounts, cancellationToken) is not { } servedAccount)
        {
            return new AccountFilter.Refused(
                string.IsNullOrWhiteSpace(account) ? MissingFilter() : Unknown(MailAccountId.Create(account).Value));
        }

        return new AccountFilter.Narrowed(servedAccount);
    }

    /// <summary>What an optional account filter resolved to: every account, one account, or a refusal.</summary>
    /// <remarks>Three cases rather than two nullable values, so an account exists only where the filter narrowed to one and a refused filter can never be read as every account.</remarks>
    internal abstract record AccountFilter
    {
        private AccountFilter()
        {
        }

        /// <summary>No filter was given, so the reading covers every account.</summary>
        internal sealed record Everything : AccountFilter;

        /// <summary>The filter named a served account, which the reading is narrowed to.</summary>
        /// <param name="Account">The account to narrow to.</param>
        internal sealed record Narrowed(MailAccountId Account) : AccountFilter;

        /// <summary>The filter named no served account, so the reading does not go ahead.</summary>
        /// <param name="Refusal">What the caller is told.</param>
        internal sealed record Refused(ProblemHttpResult Refusal) : AccountFilter;
    }
}

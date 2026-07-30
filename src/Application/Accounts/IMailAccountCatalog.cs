// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Domain.Accounts;

namespace MailMcp.Application.Accounts;

/// <summary>Answers which mail accounts this deployment serves.</summary>
/// <remarks>
/// <para>
/// A query use case asks this before it reads anything, so an account identifier nobody configured is refused rather
/// than answered with an empty page. The distinction matters at a boundary a client reaches: an empty page tells the
/// client the identifier exists and holds no mail, which turns a list operation into a way to enumerate accounts.
/// </para>
/// <para>
/// The port carries the question rather than the account list, because the answer is the only thing a use case is
/// allowed to act on. Which accounts exist stays with the configuration that defines them, and nothing here publishes
/// the set to a caller.
/// </para>
/// </remarks>
public interface IMailAccountCatalog
{
    /// <summary>Determines whether an account identifier names an account this deployment is configured to serve.</summary>
    /// <param name="accountId">The account identifier a request named.</param>
    /// <returns><see langword="true" /> when the account is configured; otherwise <see langword="false" />.</returns>
    bool Serves(MailAccountId accountId);
}

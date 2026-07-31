// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Accounts;
using MailMcp.Domain.Failures;

namespace MailMcp.Application.Accounts;

/// <summary>The failure raised when a request names a mail account this deployment does not serve.</summary>
/// <remarks>
/// <para>
/// One failure covers both "no such account" and "not yours", deliberately. A caller that could tell the two apart
/// could enumerate the configured account identifiers one request at a time, so the two answers are the same answer.
/// This is also why the alternative — returning an empty page — is not available: an empty page confirms the identifier.
/// </para>
/// <para>
/// The message names the identifier the caller supplied, which is MailMcp's own configured name for the account and
/// carries nothing the caller did not already write.
/// </para>
/// </remarks>
public sealed class MailAccountNotAccessibleException : MailMcpException
{
    /// <summary>Initializes the failure for one inaccessible account.</summary>
    /// <param name="accountId">The account identifier the request named.</param>
    public MailAccountNotAccessibleException(MailAccountId accountId)
        : base($"Mail account '{accountId.Value}' is not accessible.") => this.AccountId = accountId;

    /// <summary>Gets the account identifier the request named.</summary>
    public MailAccountId AccountId { get; }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.MailAccountNotAccessible;
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Failures;

namespace MailFathom.Application.Accounts;

/// <summary>The failure raised when a request names a mail account this deployment does not serve.</summary>
/// <remarks>
/// <para>
/// One failure covers both "no such account" and "not yours", deliberately. A caller that could tell the two apart
/// could enumerate the configured accounts one request at a time, so the two answers are the same answer. This is also
/// why the alternative — returning an empty page — is not available: an empty page confirms the name.
/// </para>
/// <para>
/// It is the same failure whichever way the request named the account. Text that matches no identifier and no display
/// name is refused exactly as an identifier the deployment stopped serving is, so a caller cannot learn which of the two
/// spellings it got wrong, nor that the other spelling exists.
/// </para>
/// <para>
/// The message names what the caller supplied, which carries nothing the caller did not already write.
/// </para>
/// </remarks>
public sealed class MailAccountNotAccessibleException : MailFathomException
{
    /// <summary>Initializes the failure for text that named no account this deployment serves.</summary>
    /// <param name="selector">The text the request named an account with.</param>
    public MailAccountNotAccessibleException(MailAccountSelector selector)
        : base($"Mail account '{selector.Value}' is not accessible.") => this.RequestedAccount = selector;

    /// <summary>Initializes the failure for an identifier this deployment does not serve.</summary>
    /// <param name="accountId">The account identifier the request named.</param>
    public MailAccountNotAccessibleException(MailAccountId accountId)
        : this(MailAccountSelector.For(accountId))
    {
    }

    /// <summary>Gets the text the request named the account with.</summary>
    public MailAccountSelector RequestedAccount { get; }

    /// <inheritdoc />
    public override MailFathomErrorCode ErrorCode => MailFathomErrorCode.MailAccountNotAccessible;
}

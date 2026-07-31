// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using MailMcp.Domain.Failures;

namespace MailMcp.Infrastructure.Mail;

/// <summary>Indicates that a mail server advertises no authentication mechanism the account's policy permits.</summary>
/// <remarks>
/// The message names the account and the permitted mechanism names only. The mechanisms the server advertised are
/// deliberately absent from it as well as from the payload, because recording them would document a downgrade path in
/// logs; <see cref="MailMcpException" /> states the rest of what a message may carry.
/// </remarks>
public sealed class MailAuthenticationMechanismUnavailableException : MailMcpException
{
    /// <summary>Initializes a new unavailable-mechanism failure for one account.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="permittedMechanismNames">The SASL names the account's policy permits.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="accountId" /> or <paramref name="permittedMechanismNames" /> is <see langword="null" />.</exception>
    public MailAuthenticationMechanismUnavailableException(string accountId, IReadOnlyList<string> permittedMechanismNames)
        : base(DescribeUnavailableMechanisms(accountId, permittedMechanismNames))
    {
        this.AccountId = accountId;
        this.PermittedMechanismNames = [.. permittedMechanismNames];
    }

    /// <inheritdoc />
    public override MailMcpErrorCode ErrorCode => MailMcpErrorCode.MailAuthenticationMechanismUnavailable;

    /// <summary>Gets the local account identifier.</summary>
    public string AccountId { get; }

    /// <summary>Gets the SASL names the account's policy permits.</summary>
    public IReadOnlyList<string> PermittedMechanismNames { get; }

    private static string DescribeUnavailableMechanisms(string accountId, IReadOnlyList<string> permittedMechanismNames)
    {
        ArgumentNullException.ThrowIfNull(accountId);
        ArgumentNullException.ThrowIfNull(permittedMechanismNames);

        return $"Account '{accountId}' permits only [{string.Join(", ", permittedMechanismNames)}], and the server advertises none of them.";
    }
}

// Copyright © 2026 Krzysztof Kasprowicz

namespace MailMcp.Infrastructure.Mail;

/// <summary>Indicates that a mail server advertises no authentication mechanism the account's policy permits.</summary>
/// <remarks>
/// The message names the account and the permitted mechanism names only. It must never carry the user name, the
/// password, or the mechanisms the server advertised, because the latter would document a downgrade path in logs.
/// </remarks>
public sealed class MailAuthenticationMechanismUnavailableException : Exception
{
    /// <summary>Initializes a new unavailable-mechanism failure.</summary>
    public MailAuthenticationMechanismUnavailableException()
    {
        this.AccountId = string.Empty;
        this.PermittedMechanismNames = [];
    }

    /// <summary>Initializes a new unavailable-mechanism failure with a safe message.</summary>
    public MailAuthenticationMechanismUnavailableException(string message)
        : base(message)
    {
        this.AccountId = string.Empty;
        this.PermittedMechanismNames = [];
    }

    /// <summary>Initializes a new unavailable-mechanism failure with a safe message and inner exception.</summary>
    public MailAuthenticationMechanismUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        this.AccountId = string.Empty;
        this.PermittedMechanismNames = [];
    }

    /// <summary>Initializes a new unavailable-mechanism failure for one account.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="permittedMechanismNames">The SASL names the account's policy permits.</param>
    public MailAuthenticationMechanismUnavailableException(string accountId, IReadOnlyList<string> permittedMechanismNames)
        : base($"Account '{accountId}' permits only [{string.Join(", ", permittedMechanismNames ?? [])}], and the server advertises none of them.")
    {
        this.AccountId = accountId;
        this.PermittedMechanismNames = permittedMechanismNames ?? [];
    }

    /// <summary>Gets the local account identifier.</summary>
    public string AccountId { get; }

    /// <summary>Gets the SASL names the account's policy permits.</summary>
    public IReadOnlyList<string> PermittedMechanismNames { get; }
}

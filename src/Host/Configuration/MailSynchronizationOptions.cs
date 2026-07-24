// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using MailMcp.Infrastructure.Mail.MailKit;

namespace MailMcp.Host.Configuration;

/// <summary>Configures periodic IMAP synchronization.</summary>
public sealed class MailSynchronizationOptions : IValidatableObject, IMailKitImapAccountSettingsProvider
{
    /// <summary>Gets or sets whether periodic synchronization is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the interval between reconciliation runs.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets configured accounts and folders to synchronize.</summary>
    public List<MailSynchronizationAccountOptions> Accounts { get; } = [];

    /// <inheritdoc />
    public MailKitImapAccountSettings GetSettings(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        var account = Accounts.Single(account => StringComparer.Ordinal.Equals(account.AccountId, accountId));
        return new MailKitImapAccountSettings(account.AccountId, account.Host, account.Port, account.UseTls, account.UserName, account.Password);
    }

    internal IEnumerable<ValidationResult> ValidateForSynchronization(bool synchronizationEnabled)
    {
        if (Enabled && Accounts.Count == 0)
        {
            yield return new ValidationResult("At least one account is required when synchronization is enabled.", [nameof(Accounts)]);
        }

        foreach (var account in Accounts)
        {
            foreach (var result in account.ValidateForSynchronization(Enabled))
            {
                yield return result;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => ValidateForSynchronization(Enabled);
}

/// <summary>Configures one account for periodic IMAP synchronization.</summary>
public sealed class MailSynchronizationAccountOptions : IValidatableObject
{
    /// <summary>Gets or sets the local account identifier.</summary>
    [Required]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP server host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP server port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 993;

    /// <summary>Gets or sets whether TLS is required from connection start.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Gets or sets the IMAP user name. Store secret values outside ordinary configuration files.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP password or app password. Store secret values outside ordinary configuration files.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets configured folder names.</summary>
    public List<string> Folders { get; } = ["INBOX"];

    internal IEnumerable<ValidationResult> ValidateForSynchronization(bool synchronizationEnabled)
    {
        if (Folders.Count == 0 || Folders.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("Each synchronization account must define at least one non-empty folder.", [nameof(Folders)]);
        }

        if (synchronizationEnabled)
        {
            if (string.IsNullOrWhiteSpace(Host))
            {
                yield return new ValidationResult("IMAP host is required when synchronization is enabled.", [nameof(Host)]);
            }

            if (string.IsNullOrWhiteSpace(UserName))
            {
                yield return new ValidationResult("IMAP user name is required when synchronization is enabled.", [nameof(UserName)]);
            }

            if (string.IsNullOrWhiteSpace(Password))
            {
                yield return new ValidationResult("IMAP password is required when synchronization is enabled.", [nameof(Password)]);
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => ValidateForSynchronization(synchronizationEnabled: true);
}

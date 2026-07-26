// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using MailMcp.Application.Mail;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Mail.MailKit;

namespace MailMcp.Host.Configuration;

/// <summary>Configures periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationOptions : IValidatableObject, IMailKitImapAccountSettingsProvider, IMailTransportSecurityPolicyReader
{
    /// <summary>Gets or sets whether periodic synchronization is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the interval between reconciliation runs.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the maximum number of messages requested from one IMAP metadata batch.</summary>
    [Range(1, 1000)]
    public int MaxMetadataBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum raw MIME content accepted for local storage.</summary>
    [Range(1024, 104857600)]
    public long MaxRawMimeBytes { get; set; } = 25L * 1024L * 1024L;

    /// <summary>Gets or sets the maximum number of bounded metadata batches processed by one synchronization run.</summary>
    [Range(1, 1000)]
    public int MaxMetadataBatchesPerRun { get; set; } = 10;

    /// <summary>Gets or sets configured accounts and folders to synchronize.</summary>
    public List<MailSynchronizationAccountOptions> Accounts { get; set; } = [];

    /// <inheritdoc />
    public MailKitImapAccountSettings GetSettings(string accountId)
    {
        var normalizedAccountId = MailAccountId.Create(accountId).Value;
        var account = this.FindAccount(normalizedAccountId);

        return new MailKitImapAccountSettings(
            normalizedAccountId,
            account.Host.Trim(),
            account.Port,
            account.UserName,
            account.Password);
    }

    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.CreateTransportSecurityPolicy();
    }

    internal IEnumerable<ValidationResult> ValidateForSynchronization()
    {
        if (this.Accounts is null)
        {
            yield return new ValidationResult("Account configuration must be a list.", [nameof(this.Accounts)]);
            yield break;
        }

        if (this.Enabled && this.Accounts.Count == 0)
        {
            yield return new ValidationResult("At least one account is required when synchronization is enabled.", [nameof(this.Accounts)]);
        }

        if (this.Accounts
            .Where(account => !string.IsNullOrWhiteSpace(account.AccountId))
            .GroupBy(account => MailAccountId.Create(account.AccountId).Value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            yield return new ValidationResult("Account IDs must be unique after normalization.", [nameof(this.Accounts)]);
        }

        foreach (var account in this.Accounts)
        {
            foreach (var result in account.ValidateForSynchronization(this.Enabled))
            {
                yield return result;
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization();

    private MailSynchronizationAccountOptions FindAccount(string normalizedAccountId) => this.Accounts.Single(
        candidate => !string.IsNullOrWhiteSpace(candidate.AccountId)
            && StringComparer.Ordinal.Equals(
                MailAccountId.Create(candidate.AccountId).Value,
                normalizedAccountId));
}

/// <summary>Configures one account for periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationAccountOptions : IValidatableObject
{
    /// <summary>Gets or sets the local account identifier.</summary>
    [Required]
    public string AccountId { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP server host name.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP server port.</summary>
    [Range(1, 65535)]
    public int Port { get; set; } = 993;

    /// <summary>Gets or sets the account's transport security settings.</summary>
    public MailAccountTransportSecurityOptions TransportSecurity { get; set; } = new();

    /// <summary>Gets or sets the IMAP user name. Store secret values outside ordinary configuration files.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the IMAP password or app password. Store secret values outside ordinary configuration files.</summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets configured folder names. When omitted, the worker synchronizes INBOX only.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>Gets the configured folders or the post-binding default folder.</summary>
    public IReadOnlyList<string> EffectiveFolders => this.Folders is not { Count: > 0 } ? ["INBOX"] : this.Folders;

    internal IEnumerable<ValidationResult> ValidateForSynchronization(bool synchronizationEnabled)
    {
        if (this.Port is < 1 or > 65535)
        {
            yield return new ValidationResult("IMAP port must be between 1 and 65535.", [nameof(this.Port)]);
        }

        if (this.Folders is null)
        {
            yield return new ValidationResult("Folder configuration must be a list.", [nameof(this.Folders)]);
            yield break;
        }

        if (this.Folders.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("Configured folder names must be non-empty.", [nameof(this.Folders)]);
        }

        if (this.Folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder))
            .GroupBy(folder => MailFolderName.Create(folder).Value, StringComparer.Ordinal)
            .Any(group => group.Count() > 1))
        {
            yield return new ValidationResult("Configured folder names must be unique after normalization.", [nameof(this.Folders)]);
        }

        if (synchronizationEnabled)
        {
            if (string.IsNullOrWhiteSpace(this.AccountId))
            {
                yield return new ValidationResult("Account ID is required when synchronization is enabled.", [nameof(this.AccountId)]);
            }

            if (string.IsNullOrWhiteSpace(this.Host))
            {
                yield return new ValidationResult("IMAP host is required when synchronization is enabled.", [nameof(this.Host)]);
            }

            if (string.IsNullOrWhiteSpace(this.UserName))
            {
                yield return new ValidationResult("IMAP user name is required when synchronization is enabled.", [nameof(this.UserName)]);
            }

            if (string.IsNullOrWhiteSpace(this.Password))
            {
                yield return new ValidationResult("IMAP password is required when synchronization is enabled.", [nameof(this.Password)]);
            }

            foreach (var result in this.ValidateTransportSecurity())
            {
                yield return result;
            }
        }
    }

    /// <summary>Builds the account's validated transport security policy.</summary>
    /// <returns>The policy the mailbox adapter must obey.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured combination is unsafe.</exception>
    internal MailTransportSecurityPolicy CreateTransportSecurityPolicy() => this.TransportSecurity.CreatePolicy();

    /// <summary>Re-checks the transport security rules so an unsafe account fails startup.</summary>
    /// <returns>One result per unsupported mechanism name and per violated rule, each naming the account.</returns>
    private IEnumerable<ValidationResult> ValidateTransportSecurity() => this.TransportSecurity
        .FindConfigurationErrors()
        .Select(error => new ValidationResult(
            $"Account '{this.AccountId}': {error.Description}",
            [$"{nameof(this.TransportSecurity)}.{error.PropertyName}"]));

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization(synchronizationEnabled: true);
}

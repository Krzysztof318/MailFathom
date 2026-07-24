// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;

namespace MailMcp.Host.Configuration;

/// <summary>Configures periodic IMAP synchronization.</summary>
public sealed class MailSynchronizationOptions : IValidatableObject
{
    /// <summary>Gets or sets whether periodic synchronization is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets the interval between reconciliation runs.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "1.00:00:00")]
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Gets or sets the maximum metadata records requested from one IMAP batch.</summary>
    [Range(1, 1000)]
    public int MaxMetadataBatchSize { get; set; } = 100;

    /// <summary>Gets or sets the maximum raw MIME content accepted for local storage.</summary>
    [Range(1024, 104857600)]
    public long MaxRawMimeBytes { get; set; } = 25L * 1024L * 1024L;

    /// <summary>Gets or sets the maximum bounded UID windows inspected by one synchronization run.</summary>
    [Range(1, 1000)]
    public int MaxUidWindowsPerRun { get; set; } = 10;

    /// <summary>Gets or sets configured accounts and folders to synchronize.</summary>
    public List<MailSynchronizationAccountOptions> Accounts { get; set; } = [];

    internal IEnumerable<ValidationResult> ValidateForSynchronization()
    {
        if (this.Enabled && this.Accounts.Count == 0)
        {
            yield return new ValidationResult("At least one account is required when synchronization is enabled.", [nameof(this.Accounts)]);
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

    /// <summary>Gets or sets configured folder names. When omitted, the worker synchronizes INBOX only.</summary>
    public List<string> Folders { get; set; } = [];

    /// <summary>Gets the configured folders or the post-binding default folder.</summary>
    public IReadOnlyList<string> EffectiveFolders => this.Folders.Count == 0 ? ["INBOX"] : this.Folders;

    internal IEnumerable<ValidationResult> ValidateForSynchronization(bool synchronizationEnabled)
    {
        if (this.Folders.Any(string.IsNullOrWhiteSpace))
        {
            yield return new ValidationResult("Configured folder names must be non-empty.", [nameof(this.Folders)]);
        }

        if (string.IsNullOrWhiteSpace(this.AccountId))
        {
            yield return new ValidationResult("Account id is required when an account is configured.", [nameof(this.AccountId)]);
        }

        if (synchronizationEnabled)
        {
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
        }
    }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization(synchronizationEnabled: true);
}

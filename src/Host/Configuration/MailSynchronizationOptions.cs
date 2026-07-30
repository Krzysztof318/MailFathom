// Copyright © 2026 Krzysztof Kasprowicz

using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography.X509Certificates;
using MailMcp.Application.Mail;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Folders;
using MailMcp.Domain.Synchronization;
using MailMcp.Domain.Transport;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Host.Configuration;

/// <summary>Configures periodic IMAP synchronization.</summary>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailSynchronizationOptions : IValidatableObject, IMailTransportSecurityPolicyReader, IMailSynchronizationWindowReader
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

    /// <summary>Gets or sets the maximum number of MIME entities one message may declare before extraction abandons it.</summary>
    [Range(1, 100000)]
    public int MaxMimePartCount { get; set; } = 1000;

    /// <summary>Gets or sets the maximum depth to which one message may nest multiparts before extraction abandons it.</summary>
    [Range(1, 1000)]
    public int MaxMimeNestingDepth { get; set; } = 30;

    /// <summary>Gets or sets the maximum number of characters one message's body contributes to its indexed text.</summary>
    /// <remarks>
    /// <para>
    /// The upper bound of the range is what keeps the generated search vector inside PostgreSQL's one-megabyte limit
    /// once the subject and the participant addresses sharing that document are counted too. It is a value the
    /// arithmetic supports rather than a round number: a <c>tsvector</c> spends four bytes of entry header, the lexeme
    /// itself, and four bytes of position data per distinct word, so text of single-character words separated by single
    /// spaces — the shape that maximizes entries — costs about 4.5 bytes of vector per character of input. The subject
    /// and participant copies take about 101,000 of the 1,048,575 available bytes at their own ceilings, which leaves
    /// roughly 210,000 characters of body; 200,000 keeps a margin.
    /// </para>
    /// <para>
    /// The bound matters because the vector is a generated column computed on every insert. Exceeding the limit would
    /// not degrade search: it would make the row unwritable, exhaust the retry budget, and stop the folder the message
    /// arrived in on every later run.
    /// </para>
    /// </remarks>
    [Range(1_000, 200_000)]
    public int MaxExtractedTextCharacters { get; set; } = 100_000;

    /// <summary>Gets or sets configured accounts and folders to synchronize.</summary>
    public List<MailSynchronizationAccountOptions> Accounts { get; set; } = [];

    /// <summary>Builds one account's connection settings, resolving its material for the caller to own.</summary>
    /// <param name="accountId">The local account identifier.</param>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="cancellationToken">Cancels the secret resolution.</param>
    /// <returns>The settings, whose material the caller must dispose when its operation ends.</returns>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "Ownership of the resolved material passes to the caller, which erases it when its operation ends.")]
    internal async Task<ImapAccountSettings> ResolveSettingsAsync(
        string accountId,
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var normalizedAccountId = MailAccountId.Create(accountId).Value;
        var account = this.FindAccount(normalizedAccountId);

        var material = await account.ResolveConnectionMaterialAsync(resolver, trustAnchorLoader, cancellationToken);

        return new ImapAccountSettings(
            normalizedAccountId,
            account.Host.Trim(),
            account.Port,
            account.UserName,
            material);
    }

    /// <inheritdoc />
    public MailTransportSecurityPolicy GetPolicy(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.CreateTransportSecurityPolicy();
    }

    /// <inheritdoc />
    public MailSynchronizationWindow GetWindow(MailAccountId accountId)
    {
        var account = this.FindAccount(accountId.Value);

        return account.CreateSynchronizationWindow();
    }

    /// <summary>Finds every configured earliest received date that could not mean anything on the supplied date.</summary>
    /// <param name="today">The current date the configured bounds are read against.</param>
    /// <returns>One result per account whose bound lies in the future, empty when every bound is usable.</returns>
    /// <remarks>
    /// The rule lives here with the other configuration rules while its clock stays outside, because the current date
    /// is not something a bound options graph or a data annotation can reach. Nothing gates it on
    /// <see cref="Enabled" />: a date an operator wrote is a date they intend to synchronize from, and discovering that
    /// it excludes the whole mailbox at the moment synchronization is switched on is worse than discovering it now.
    /// </remarks>
    internal IEnumerable<ValidationResult> FindSynchronizationWindowErrors(DateOnly today) =>
        this.Accounts?.SelectMany(account => account.ValidateSynchronizationWindow(today)) ?? [];

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

        foreach (var result in this.Accounts.SelectMany(account => account.ValidateForSynchronization(this.Enabled)))
        {
            yield return result;
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

    /// <summary>Gets or sets the IMAP user name, which is an identifier rather than a credential and stays a plain configuration value.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the account's secret-bearing settings, which carry references rather than credentials.</summary>
    public MailAccountSecretOptions Secrets { get; set; } = new();

    /// <summary>Gets or sets the earliest date the mail server may have received an email on for it to be synchronized.</summary>
    /// <remarks>
    /// Omitting it synchronizes every email the server still holds, which is the default. It binds as a plain date such
    /// as <c>2024-01-01</c>, and a value that is not one fails startup: the account is a collection item, and the binder
    /// would otherwise drop the whole account over a typo in this one setting, which is another reason the section is
    /// bound with <c>ErrorOnUnknownConfiguration</c>. Which date the bound compares against, and why, is recorded on
    /// <see cref="MailSynchronizationWindow" />.
    /// </remarks>
    public DateOnly? EarliestEmailReceivedDate { get; set; }

    /// <summary>Gets or sets the configured folder aliases. When omitted, the worker synchronizes the inbox only.</summary>
    public List<MailFolderMappingOptions> Folders { get; set; } = [];

    /// <summary>Gets the configured folders or the post-binding default one.</summary>
    /// <remarks>
    /// The default names the inbox by its special-use role rather than by the path <c>INBOX</c>, so an account whose
    /// server presents the inbox under another name still synchronizes with no folder configuration at all.
    /// </remarks>
    public IReadOnlyList<MailFolderMappingOptions> EffectiveFolders =>
        this.Folders is not { Count: > 0 } ? [CreateDefaultInboxFolder()] : this.Folders;

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

        foreach (var result in this.Folders.SelectMany(folder => folder.ValidateForSynchronization()))
        {
            yield return result;
        }

        // Grouped the way MailFolderAlias normalizes rather than through the factory itself, because an alias this
        // method has already reported as unusable must not throw out of the rule that follows it.
        if (this.Folders
            .Where(folder => !string.IsNullOrWhiteSpace(folder.Alias))
            .GroupBy(folder => folder.Alias.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            yield return new ValidationResult("Configured folder aliases must be unique after normalization.", [nameof(this.Folders)]);
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

            foreach (var result in this.ValidateTransportSecurity())
            {
                yield return result;
            }
        }
    }

    /// <summary>Reports an earliest received date that lies ahead of the supplied date.</summary>
    /// <param name="today">The current date the configured bound is read against.</param>
    /// <returns>One result when the bound is in the future, none otherwise.</returns>
    /// <remarks>
    /// A future bound is refused rather than adopted because it excludes every email the mailbox holds, which is
    /// indistinguishable from synchronization silently doing nothing. The comparison is made in UTC, so a bound written
    /// as the operator's local date is refused while UTC has not reached it yet.
    /// </remarks>
    internal IEnumerable<ValidationResult> ValidateSynchronizationWindow(DateOnly today)
    {
        if (this.EarliestEmailReceivedDate is { } earliestReceivedDate && earliestReceivedDate > today)
        {
            yield return new ValidationResult(
                $"Account '{this.AccountId}': the earliest email received date {earliestReceivedDate:yyyy-MM-dd} is later than the current UTC date {today:yyyy-MM-dd}, so it would exclude every email in the mailbox.",
                [nameof(this.EarliestEmailReceivedDate)]);
        }
    }

    /// <summary>Builds the account's configured synchronization window.</summary>
    /// <returns>The window the account's bound names, or an unbounded one when it configured none.</returns>
    internal MailSynchronizationWindow CreateSynchronizationWindow() =>
        this.EarliestEmailReceivedDate is { } earliestReceivedDate
            ? MailSynchronizationWindow.EmailsReceivedSince(earliestReceivedDate)
            : MailSynchronizationWindow.Unbounded;

    /// <summary>Builds the account's validated transport security policy.</summary>
    /// <returns>The policy the mailbox adapter must obey.</returns>
    /// <exception cref="MailTransportSecurityPolicyViolationException">Thrown when the configured combination is unsafe.</exception>
    internal MailTransportSecurityPolicy CreateTransportSecurityPolicy() => this.TransportSecurity.CreatePolicy();

    /// <summary>Resolves the password and trust anchor one connection attempt needs.</summary>
    /// <param name="resolver">The resolver that turns configured references into material.</param>
    /// <param name="trustAnchorLoader">The loader that turns configured material into a trust anchor.</param>
    /// <param name="cancellationToken">Cancels the retrieval.</param>
    /// <returns>The material, which the caller must dispose when its operation ends.</returns>
    /// <exception cref="InvalidOperationException">Thrown when configuration that passed startup validation no longer yields usable material.</exception>
    /// <remarks>
    /// An anchor that fails to load fails the connection attempt rather than downgrading it to the system trust store,
    /// and the password resolved first is erased on that path so a failed attempt leaves nothing behind.
    /// </remarks>
    internal async Task<MailAccountConnectionMaterial> ResolveConnectionMaterialAsync(
        ISecretReferenceResolver resolver,
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var password = await this.Secrets.ResolvePasswordAsync(resolver, cancellationToken);

        try
        {
            var trustedCertificateAuthority = await this.LoadTrustedCertificateAuthorityAsync(
                trustAnchorLoader,
                cancellationToken);

            return new MailAccountConnectionMaterial(password, trustedCertificateAuthority);
        }
        catch
        {
            password.Dispose();

            throw;
        }
    }

    private async Task<X509Certificate2?> LoadTrustedCertificateAuthorityAsync(
        TrustAnchorLoader trustAnchorLoader,
        CancellationToken cancellationToken)
    {
        var loadResult = await this.TransportSecurity.LoadTrustedCertificateAuthorityAsync(
            trustAnchorLoader,
            cancellationToken);

        if (loadResult is null)
        {
            return null;
        }

        // A failed result owns nothing, so nothing leaks by throwing past it.
        return loadResult.TrustAnchor ?? throw new InvalidOperationException(
            $"Account '{this.AccountId}': the configured trusted certificate authority material could not be loaded [{loadResult.Failure}].");
    }

    /// <summary>Re-checks the transport security rules so an unsafe account fails startup.</summary>
    /// <returns>One result per unsupported mechanism name and per violated rule, each naming the account.</returns>
    private IEnumerable<ValidationResult> ValidateTransportSecurity() => this.TransportSecurity
        .FindConfigurationErrors()
        .Select(error => new ValidationResult(
            DescribeConfigurationError(this.AccountId, error),
            [$"{nameof(this.TransportSecurity)}.{error.PropertyName}"]));

    /// <summary>Builds the startup message for one transport security configuration error.</summary>
    /// <remarks>
    /// The violation name is appended so the message carries a stable identity an operator or log query can match on,
    /// while the prose stays free to change. A mechanism-name parse failure has no violation and is reported without
    /// one. Neither part may name the user name, password, or trust anchor reference.
    /// </remarks>
    private static string DescribeConfigurationError(string accountId, MailAccountTransportSecurityConfigurationError error) =>
        error.Violation is { } violation
            ? $"Account '{accountId}': {error.Description} [{violation}]"
            : $"Account '{accountId}': {error.Description}";

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) => this.ValidateForSynchronization(synchronizationEnabled: true);

    /// <summary>Builds the folder an account that configured none synchronizes.</summary>
    private static MailFolderMappingOptions CreateDefaultInboxFolder() => new()
    {
        Alias = nameof(MailFolderSpecialUse.Inbox),
        SpecialUse = nameof(MailFolderSpecialUse.Inbox),
    };
}

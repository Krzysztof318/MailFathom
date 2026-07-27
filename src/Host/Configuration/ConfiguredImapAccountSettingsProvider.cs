// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Infrastructure.Certificates;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;

namespace MailMcp.Host.Configuration;

/// <summary>Supplies IMAP connection settings from the published configuration, resolving each account's material per use.</summary>
/// <remarks>
/// The adapter exists because the bound options object cannot take constructor dependencies — the configuration binder
/// requires a parameterless type — while resolution needs the resolver and the certificate loader. Resolving per use
/// rather than caching is what makes a credential or a trust anchor rotated behind an unchanged reference visible to
/// the next connection attempt with no cache to invalidate and no restart.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this settings provider.")]
internal sealed class ConfiguredImapAccountSettingsProvider(
    IMailSynchronizationSettingsReader synchronizationSettings,
    ISecretReferenceResolver secretReferenceResolver,
    TrustAnchorLoader trustAnchorLoader) : IImapAccountSettingsProvider
{
    /// <inheritdoc />
    public Task<ImapAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        synchronizationSettings.Current.ResolveSettingsAsync(
            accountId,
            secretReferenceResolver,
            trustAnchorLoader,
            cancellationToken);
}

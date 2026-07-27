// Copyright © 2026 Krzysztof Kasprowicz

using System.Diagnostics.CodeAnalysis;
using MailMcp.Infrastructure.Mail;
using MailMcp.Infrastructure.Secrets;
using Microsoft.Extensions.Options;

namespace MailMcp.Host.Configuration;

/// <summary>Supplies IMAP connection settings from the bound configuration, resolving each account's secrets per use.</summary>
/// <remarks>
/// The adapter exists because the bound options object cannot take constructor dependencies — the configuration binder
/// requires a parameterless type — while resolution needs the resolver. Resolving per use rather than caching is what
/// makes material rotated behind an unchanged reference visible to the next connection attempt with no cache to
/// invalidate and no restart.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this settings provider.")]
internal sealed class ConfiguredImapAccountSettingsProvider(
    IOptions<MailSynchronizationOptions> synchronizationOptions,
    ISecretReferenceResolver secretReferenceResolver) : IImapAccountSettingsProvider
{
    /// <inheritdoc />
    public Task<ImapAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        synchronizationOptions.Value.ResolveSettingsAsync(accountId, secretReferenceResolver, cancellationToken);
}

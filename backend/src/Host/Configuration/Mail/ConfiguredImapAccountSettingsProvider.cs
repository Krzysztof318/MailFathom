// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Supplies IMAP connection settings from the snapshot its scope captured, resolving each account's material per use.</summary>
/// <remarks>
/// <para>
/// The adapter exists because the bound options object cannot take constructor dependencies — the configuration binder
/// requires a parameterless type — while resolution needs the resolver and the certificate loader. Resolving per use
/// rather than caching is what makes a credential or a trust anchor rotated behind an unchanged reference visible to
/// the next connection attempt with no cache to invalidate and no restart.
/// </para>
/// <para>
/// The snapshot arrives as a scoped dependency rather than being read from the publisher here. One work unit resolves
/// its transport security policy and then its connection material through two different services; reading the
/// published snapshot at each would let a reload landing between them combine an older policy with a newer host and
/// credential, which is precisely the mid-operation rotation the reload contract rules out.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this settings provider.")]
internal sealed class ConfiguredImapAccountSettingsProvider(
    MailSynchronizationOptions synchronizationSettings,
    ISecretReferenceResolver secretReferenceResolver,
    TrustAnchorLoader trustAnchorLoader) : IImapAccountSettingsProvider
{
    /// <inheritdoc />
    public Task<ImapAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        synchronizationSettings.ResolveSettingsAsync(
            accountId,
            secretReferenceResolver,
            trustAnchorLoader,
            cancellationToken);
}

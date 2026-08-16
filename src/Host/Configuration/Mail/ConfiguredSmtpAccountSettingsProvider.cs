// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Infrastructure.Certificates;
using MailFathom.Infrastructure.Mail;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Supplies SMTP connection settings from the snapshot its scope captured, resolving each account's material per use.</summary>
/// <remarks>
/// It is a second adapter beside the one that supplies reading settings rather than a second method on it, for the
/// reason the ports themselves are separate: a component that resolves where mail is read cannot thereby resolve where
/// mail is sent. Everything else about it is the same arrangement — the bound options object cannot take constructor
/// dependencies, resolution happens per use so a rotated credential is observed by the next connection with no cache to
/// invalidate, and the snapshot arrives scoped so a reload landing mid-operation cannot combine an older policy with a
/// newer endpoint.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this settings provider.")]
internal sealed class ConfiguredSmtpAccountSettingsProvider(
    MailSynchronizationOptions synchronizationSettings,
    ISecretReferenceResolver secretReferenceResolver,
    TrustAnchorLoader trustAnchorLoader) : ISmtpAccountSettingsProvider
{
    /// <inheritdoc />
    public Task<SmtpAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken) =>
        synchronizationSettings.ResolveDeliverySettingsAsync(
            accountId,
            secretReferenceResolver,
            trustAnchorLoader,
            cancellationToken);
}

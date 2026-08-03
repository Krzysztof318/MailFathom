// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Accounts;
using MailFathom.Infrastructure.Mail.OAuth;
using MailFathom.Infrastructure.Secrets.References;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Supplies token endpoint settings from the snapshot its scope captured, resolving each account's secrets per request.</summary>
/// <remarks>
/// The adapter exists for the reason <see cref="ConfiguredImapAccountSettingsProvider" /> does: the bound options
/// object cannot take constructor dependencies, while resolution needs the secret resolver. Reading the snapshot the
/// scope captured rather than the published one keeps a reload landing mid-operation from pairing one account's
/// endpoint with another snapshot's credentials.
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The dependency injection container materializes this settings provider.")]
internal sealed class ConfiguredMailOAuthSettingsProvider(
    MailSynchronizationOptions synchronizationSettings,
    ISecretReferenceResolver secretReferenceResolver) : IMailOAuthSettingsProvider
{
    /// <inheritdoc />
    public Task<MailOAuthAccountSettings> GetSettingsAsync(string accountId, CancellationToken cancellationToken)
    {
        var normalizedAccountId = MailAccountId.Create(accountId);
        var account = synchronizationSettings.FindConfiguredAccount(normalizedAccountId)
            ?? throw new InvalidOperationException(
                $"Account '{normalizedAccountId.Value}' is not present in the configuration snapshot this operation captured.");

        return account.ResolveOAuthSettingsAsync(secretReferenceResolver, cancellationToken);
    }
}

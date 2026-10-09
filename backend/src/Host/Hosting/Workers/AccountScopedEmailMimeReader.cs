// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Extraction;
using MailFathom.Domain.Accounts;
using MailFathom.Host.Configuration.Mail;

namespace MailFathom.Host.Hosting.Workers;

/// <summary>Reads each message under the settings of the account it belongs to, for work that walks several accounts' mail in one run.</summary>
/// <remarks>
/// <para>
/// The reader a scope resolves answers about the accounts that scope was prepared with — which senders an account
/// trusts, which authentication results it believes — and a scope is prepared with one account or one user. A walk over
/// every stored message is neither, so each account it meets is read through a scope of its own, prepared with that
/// account, and the scope is kept for the rest of the run so the account's settings are read once per run rather than
/// once per message.
/// </para>
/// <para>
/// ponytail: one scope per distinct account for the life of the run, which a run bounds by the messages it reads;
/// evict the least recently used scope if a run ever walks enough accounts for that to show in memory.
/// </para>
/// </remarks>
internal sealed class AccountScopedEmailMimeReader(IServiceScopeFactory scopeFactory) : IEmailMimeReader, IAsyncDisposable
{
    private readonly Dictionary<MailAccountId, AsyncServiceScope> preparedScopes = [];

    /// <inheritdoc />
    public async Task<EmailMimeExtractionResult> ReadMetadataAsync(
        MailAccountId account,
        ReadOnlyMemory<byte> rawMime,
        CancellationToken cancellationToken)
    {
        var scope = await this.PrepareAsync(account, cancellationToken);

        return await scope.ServiceProvider
            .GetRequiredService<IEmailMimeReader>()
            .ReadMetadataAsync(account, rawMime, cancellationToken);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var scope in this.preparedScopes.Values)
        {
            await scope.DisposeAsync();
        }

        this.preparedScopes.Clear();
    }

    /// <summary>Finds the scope prepared with one account, preparing it the first time the account is met.</summary>
    /// <remarks>An account no longer served leaves its scope holding no account, which answers it exactly as any reader answers an account it does not serve.</remarks>
    private async Task<AsyncServiceScope> PrepareAsync(MailAccountId account, CancellationToken cancellationToken)
    {
        if (this.preparedScopes.TryGetValue(account, out var prepared))
        {
            return prepared;
        }

        var scope = scopeFactory.CreateAsyncScope();

        try
        {
            await scope.ServiceProvider
                .GetRequiredService<ScopedMailSynchronizationSettings>()
                .UseAccountSettingsAsync(account, cancellationToken);
        }
        catch
        {
            await scope.DisposeAsync();

            throw;
        }

        this.preparedScopes[account] = scope;

        return scope;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text;
using System.Text.Json;
using MailFathom.Application.SensitiveContent;
using MailFathom.Host.Configuration.Mail;
using MailFathom.Host.Configuration.Mail.Readers;
using MailFathom.Host.Configuration.Spam;
using MailFathom.Infrastructure.Persistence.Users;
using MailFathom.Infrastructure.Persistence.Users.AccountSettings;
using Microsoft.Extensions.Configuration.Json;

namespace MailFathom.Host.Configuration.UserSettings;

/// <summary>Reads the settings a question about every account filters on out of one account's own document.</summary>
/// <remarks>
/// <para>
/// The account is bound on its own, as the declaration every served record composes it into, so what this reads is what
/// every per-account reader of that account answers with: the same effective folders, the same default inbox, the same
/// classification scope. Nothing here is composed with the deployment's settings — an account's opt-in is recorded as
/// it asked, and the reader that asks about every account composes it with what the deployment provides.
/// </para>
/// <para>
/// A document that does not bind is read as settings that take part in nothing rather than refused. The write path
/// judges a declaration before it ever reaches this, so the case is a row something other than the administration
/// wrote, and the honest answer about such an account is that nothing can be said for it.
/// </para>
/// </remarks>
internal static class MailAccountQueryableSettingsReading
{
    /// <summary>Reads one account's queryable settings.</summary>
    /// <param name="account">The account as its record holds it.</param>
    /// <returns>The settings, or <see cref="MailAccountQueryableSettings.Unreadable" /> where its document does not bind.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="account" /> is <see langword="null" />.</exception>
    internal static MailAccountQueryableSettings Of(MailAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return TryBind(account) is { } bound ? Of(bound) : MailAccountQueryableSettings.Unreadable;
    }

    /// <summary>Binds one account's declaration as the options every per-account reader reads it as.</summary>
    /// <param name="account">The account as its record holds it.</param>
    /// <returns>The bound declaration, or <see langword="null" /> where the document does not bind.</returns>
    /// <remarks>
    /// Strict, as the binder a record is judged by is: a property nothing binds is a document this process would refuse
    /// to serve, so reading settings out of what was left of it would publish an answer about an account nobody serves.
    /// </remarks>
    internal static MailSynchronizationAccountOptions? TryBind(MailAccountRecord account)
    {
        ArgumentNullException.ThrowIfNull(account);

        var declaration = MailAccountRecordComposition.DeclarationOf(account);

        if (declaration.GetValueKind() is not JsonValueKind.Object)
        {
            return null;
        }

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(declaration.ToJsonString()), writable: false);
        ConfigurationRoot? document = null;

        try
        {
            document = new ConfigurationRoot([new JsonStreamConfigurationSource { Stream = stream }.Build(new ConfigurationBuilder())]);

            return document.Get<MailSynchronizationAccountOptions>(binder => binder.ErrorOnUnknownConfiguration = true);
        }
        catch (Exception unbound) when (unbound is InvalidOperationException or FormatException or JsonException)
        {
            return null;
        }
        finally
        {
            document?.Dispose();
        }
    }

    private static MailAccountQueryableSettings Of(MailSynchronizationAccountOptions account)
    {
        var classification = ConfiguredSpamClassificationSettingsReader.Compose(account);

        return new MailAccountQueryableSettings(
            IsReadable: true,
            account.Mode,
            classification.IsEnabled,
            [.. Enum.GetValues<SensitiveContentScannerKind>().Where(scanner => account.SensitiveContent.For(scanner).Enabled is true)],
            account.SensitiveContent.ScreenedScanners,
            [
                .. ConfiguredMailFolders.Of([account]).Select(folder => new MailAccountQueryableFolder(
                    folder.Identity.Alias,
                    folder.SpecialUse,
                    folder.Participation,
                    classification.Covers(folder.Identity.Alias))),
            ]);
    }
}

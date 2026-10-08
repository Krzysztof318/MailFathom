// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.SensitiveContent;
using MailFathom.Domain.Synchronization;

namespace MailFathom.Infrastructure.Persistence.Users.AccountSettings;

/// <summary>The settings of one account a question about every account at once filters on, read out of its document.</summary>
/// <param name="IsReadable">Whether the document binds at all; an account whose document does not is served nothing and asks for nothing.</param>
/// <param name="SynchronizationMode">How the account asked to be synchronized.</param>
/// <param name="ClassifiesSpam">Whether the account's mail is classified.</param>
/// <param name="ScansFor">The scanners the account switched on for itself, whatever the deployment provides.</param>
/// <param name="ScreensOutgoingMailFor">The scanners whose findings the account asked to stop a message it sends.</param>
/// <param name="Folders">Every folder the account is run with, which is the inbox alone where its document maps none.</param>
/// <remarks>
/// The document stays the account's settings and this is never written on its own: the configuration layer reads it
/// out of the document it binds, and the store writes both in one transaction, which is what keeps the two from
/// disagreeing. Nothing here is composed with the deployment's own settings, so what an account asked for is recorded
/// as it asked, and a reader composes it with whatever the deployment provides when it is read.
/// </remarks>
public sealed record MailAccountQueryableSettings(
    bool IsReadable,
    MailSynchronizationMode SynchronizationMode,
    bool ClassifiesSpam,
    IReadOnlyList<SensitiveContentScannerKind> ScansFor,
    IReadOnlyList<SensitiveContentScannerKind> ScreensOutgoingMailFor,
    IReadOnlyList<MailAccountQueryableFolder> Folders)
{
    /// <summary>Gets the settings of an account whose document does not bind, which takes part in nothing.</summary>
    public static MailAccountQueryableSettings Unreadable { get; } =
        new(IsReadable: false, MailSynchronizationMode.Polling, ClassifiesSpam: false, [], [], []);
}

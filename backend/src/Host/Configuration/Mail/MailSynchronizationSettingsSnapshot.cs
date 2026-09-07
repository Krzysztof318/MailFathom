// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.UserSettings;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Publishes each validated mail section together with the user roster in force at the same instant.</summary>
internal sealed class MailSynchronizationSettingsSnapshot(
    ISettingsSnapshot<MailSynchronizationOptions> boundSettings,
    ServedMailUsers servedUsers) : ISettingsSnapshot<MailSynchronizationOptions>
{
    private readonly Lock mutex = new();

    private MailSynchronizationOptions? boundSnapshot;
    private IReadOnlyList<ServedMailUser>? userSnapshot;
    private MailSynchronizationOptions? publishedSnapshot;

    /// <inheritdoc />
    public MailSynchronizationOptions Current
    {
        get
        {
            var bound = boundSettings.Current;
            var users = servedUsers.TryGetUsers();

            if (users is null)
            {
                return bound;
            }

            lock (this.mutex)
            {
                if (!ReferenceEquals(this.boundSnapshot, bound)
                    || !ReferenceEquals(this.userSnapshot, users))
                {
                    this.boundSnapshot = bound;
                    this.userSnapshot = users;
                    this.publishedSnapshot = bound.WithServedUsers(users);
                }

                return this.publishedSnapshot!;
            }
        }
    }

    /// <inheritdoc />
    public IChangeToken GetReloadToken() => new CompositeChangeToken(
    [
        boundSettings.GetReloadToken(),
        servedUsers.GetReloadToken(),
    ]);
}

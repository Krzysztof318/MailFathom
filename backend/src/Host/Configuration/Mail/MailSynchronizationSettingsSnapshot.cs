// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Host.Configuration.OwnerSettings;
using Microsoft.Extensions.Primitives;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Publishes each validated mail section together with the owner roster in force at the same instant.</summary>
internal sealed class MailSynchronizationSettingsSnapshot(
    ISettingsSnapshot<MailSynchronizationOptions> boundSettings,
    ServedMailOwners servedOwners) : ISettingsSnapshot<MailSynchronizationOptions>
{
    private readonly Lock mutex = new();

    private MailSynchronizationOptions? boundSnapshot;
    private IReadOnlyList<ServedMailOwner>? ownerSnapshot;
    private MailSynchronizationOptions? publishedSnapshot;

    /// <inheritdoc />
    public MailSynchronizationOptions Current
    {
        get
        {
            var bound = boundSettings.Current;
            var owners = servedOwners.TryGetOwners();

            if (owners is null)
            {
                return bound;
            }

            lock (this.mutex)
            {
                if (!ReferenceEquals(this.boundSnapshot, bound)
                    || !ReferenceEquals(this.ownerSnapshot, owners))
                {
                    this.boundSnapshot = bound;
                    this.ownerSnapshot = owners;
                    this.publishedSnapshot = bound.WithServedOwners(owners);
                }

                return this.publishedSnapshot!;
            }
        }
    }

    /// <inheritdoc />
    public IChangeToken GetReloadToken() => new CompositeChangeToken(
    [
        boundSettings.GetReloadToken(),
        servedOwners.GetReloadToken(),
    ]);
}

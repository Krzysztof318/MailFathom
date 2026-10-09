// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Grants;

namespace MailFathom.Host.Signals;

/// <summary>Makes a committed change to what users are granted reach every replica's remembered grants.</summary>
/// <remarks>
/// This replica forgets at once, because nothing it would hear from the backplane is promised to arrive here first. The
/// others are told through the same empty announcement a configuration change travels as: a grant is computed from
/// records about users, and a replica hearing the announcement forgets the grants it computed in the same pass that
/// reads the users' records again, so the message stays the one kind ADR 0032 admits and carries nothing.
/// </remarks>
internal sealed class GrantChangeAnnouncements : IGrantChangeAnnouncer
{
    private readonly UserGrantCache grants;
    private readonly ConfigurationChangeAnnouncements announcements;

    /// <summary>Initializes the announcements over this replica's grants and the backplane they travel on.</summary>
    /// <param name="grants">What this replica computed.</param>
    /// <param name="announcements">What the other replicas hear a change through.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public GrantChangeAnnouncements(UserGrantCache grants, ConfigurationChangeAnnouncements announcements)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(announcements);

        this.grants = grants;
        this.announcements = announcements;
    }

    /// <inheritdoc />
    public Task AnnounceAsync()
    {
        this.grants.Forget();

        return this.announcements.AnnounceAsync();
    }
}

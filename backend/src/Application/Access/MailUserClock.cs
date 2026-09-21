// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Access;

/// <summary>Reads the wall clock the person this work is being done for is standing on.</summary>
/// <remarks>
/// <para>
/// Every operation that has to resolve <em>this week</em> or <em>since Tuesday</em> needs the same two things — the
/// current instant and that person's zone — and this is the one place the two meet. It exists so the source of an
/// anchor is shared even though its insertion is not: an operation states its own anchor on its own turn, but no
/// operation decides for itself which clock or which zone that anchor came from.
/// </para>
/// <para>
/// The person is the one the work was admitted for rather than an argument, because every caller means exactly that
/// one and a parameter would be somewhere for the wrong user to be passed. Work reached under a principal acting for
/// nobody is refused here as it is everywhere else, which is what keeps an anchor from being resolved for a deployment
/// administrator whose acts are the deployment's rather than one person's.
/// </para>
/// <para>
/// A concrete type rather than a port. It composes two seams that are already ports and holds no decision of its own,
/// so a test substitutes the clock and the zones rather than this.
/// </para>
/// </remarks>
public sealed class MailUserClock
{
    private readonly AccessAuthorization authorization;
    private readonly IMailUserTimeZones zones;
    private readonly TimeProvider time;

    /// <summary>Initializes the clock over the acting person, their zone, and this deployment's time source.</summary>
    /// <param name="authorization">Names the person the work in hand is being done for.</param>
    /// <param name="zones">Answers which zone that person's own days are read in.</param>
    /// <param name="time">The current instant, which is never the ambient clock.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public MailUserClock(AccessAuthorization authorization, IMailUserTimeZones zones, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(zones);
        ArgumentNullException.ThrowIfNull(time);

        this.authorization = authorization;
        this.zones = zones;
        this.time = time;
    }

    /// <summary>Reads the instant the acting person is standing on, carrying their own offset.</summary>
    /// <returns>The current instant as their clock reads it.</returns>
    /// <exception cref="PrincipalNotAuthorizedException">Thrown when the work was reached under no principal, or under one acting for no user.</exception>
    public DateTimeOffset Now() => this.zones.ZoneOf(this.authorization.RequireUser()).Reading(this.time.GetUtcNow());
}

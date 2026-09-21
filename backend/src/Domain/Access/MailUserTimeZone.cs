// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using MailFathom.Domain.Scheduling;

namespace MailFathom.Domain.Access;

/// <summary>The zone one person's own days are read in, which is what <em>this week</em> and <em>since Tuesday</em> mean for them.</summary>
/// <remarks>
/// <para>
/// It answers for what is composed <em>for somebody</em> rather than for what is derived <em>from a mailbox</em>, which
/// is the line <see cref="MailUserLanguage" /> already draws. The second half needs no value of its own here: every
/// derivation from a message already carries a better anchor than any zone would supply, because a message's arrival
/// instant carries the sender's own offset from its <c>Date</c> header, and reading <em>Thursday</em> in a message
/// against the reader's zone would move an appointment somebody else named.
/// </para>
/// <para>
/// A value object rather than a bare identifier, because the two things a caller needs of it cannot be recovered from a
/// string: that the identifier names a zone this host actually knows, and the resolved rules that turn an instant into
/// that person's wall clock. Both are settled once, where the value is made, so nothing downstream resolves an
/// identifier per turn or discovers at composition time that a record names a zone the platform dropped.
/// </para>
/// <para>
/// The identifier is IANA rather than the Windows form, which is what a browser reports and what a Linux host reads
/// natively; .NET resolves both on either platform, so what a record holds is whatever it was written with and the
/// resolution is the platform's own. The bound is <see cref="ZonedInstant.MaximumZoneIdLength" /> rather than a second
/// number, because the two are the same question asked about the same database of names.
/// </para>
/// <para>
/// <see cref="Coordinated" /> is the reachable default, and it is the same answer every unresolved read gives: a user
/// this deployment no longer serves, and a record held from before the zone was asked for. A person whose zone nobody
/// stated is therefore shown their mail against UTC rather than against whichever zone the host happens to run in,
/// which is the one answer that is the same on every replica.
/// </para>
/// </remarks>
public sealed record MailUserTimeZone
{
    private MailUserTimeZone(TimeZoneInfo zone) => this.Zone = zone;

    /// <summary>Gets the zone a person whose record states none is read in.</summary>
    public static MailUserTimeZone Coordinated { get; } = new(TimeZoneInfo.Utc);

    /// <summary>Gets the resolved zone, which is what turns an instant into this person's wall clock.</summary>
    public TimeZoneInfo Zone { get; }

    /// <summary>Gets the identifier the zone is recorded under.</summary>
    public string Id => this.Zone.Id;

    /// <summary>Reads a zone identifier a record or a client stated.</summary>
    /// <param name="zoneId">The identifier, as an IANA name such as <c>Europe/Warsaw</c>.</param>
    /// <param name="zone">The resolved zone, or <see langword="null" /> where the identifier names none this host knows.</param>
    /// <returns><see langword="true" /> where the identifier names a zone this host knows and is within the bound.</returns>
    /// <remarks>
    /// A refusal rather than a fallback, at both boundaries this is asked at: a record naming a zone that has left the
    /// database, and a client proposing one this host does not carry, are each somebody to tell rather than somebody to
    /// quietly read in UTC. The length is checked before the lookup so an implausible value is refused without asking
    /// the platform to search for it.
    /// </remarks>
    public static bool TryRead(string? zoneId, [NotNullWhen(true)] out MailUserTimeZone? zone)
    {
        zone = null;

        if (string.IsNullOrWhiteSpace(zoneId))
        {
            return false;
        }

        var trimmed = zoneId.Trim();

        if (trimmed.Length > ZonedInstant.MaximumZoneIdLength)
        {
            return false;
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(trimmed, out var found))
        {
            return false;
        }

        zone = new MailUserTimeZone(found);

        return true;
    }

    /// <summary>Reads the wall clock this person is standing on at one instant.</summary>
    /// <param name="instant">The instant, wherever it was taken.</param>
    /// <returns>The same instant, carrying this zone's offset for it.</returns>
    public DateTimeOffset Reading(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, this.Zone);

    /// <inheritdoc />
    public override string ToString() => this.Id;
}

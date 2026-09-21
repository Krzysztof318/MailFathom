// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access;
using MailFathom.Domain.Access;
using Microsoft.Extensions.Time.Testing;

namespace MailFathom.TestSupport;

/// <summary>Builds the clock an operation resolving a relative period reads its anchor from, over a zone and an instant a test states.</summary>
/// <remarks>
/// Every surface that states an anchor takes one, across three boundaries, so a test composing one by hand would write
/// the same three collaborators each time — and two of the three are the ones a test about time wants to state rather
/// than to arrange. What a test says here is the zone and the instant; who the work is being done for is the
/// deployment's user, which is who <see cref="AccessAuthorizations.ForCallerGranted" /> already arranges.
/// </remarks>
internal static class MailUserClocks
{
    /// <summary>Builds a clock that reads one stated instant in one stated zone.</summary>
    /// <param name="instant">The instant the clock stands at, wherever it was taken.</param>
    /// <param name="zoneId">The IANA identifier of the zone the acting person's days are read in.</param>
    /// <returns>The clock, over a caller acting for the deployment's user.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="zoneId" /> names no zone this host knows, which is a defect in the test rather than a case it covers.</exception>
    internal static MailUserClock Reading(DateTimeOffset instant, string zoneId = "UTC")
    {
        if (!MailUserTimeZone.TryRead(zoneId, out var zone))
        {
            throw new ArgumentException($"'{zoneId}' names no time zone this host knows.", nameof(zoneId));
        }

        return new MailUserClock(
            AccessAuthorizations.ForCallerGranted(MailFathomPermission.MailRead),
            new StatedTimeZones(zone),
            new FakeTimeProvider(instant));
    }

    /// <summary>Answers one zone for whoever is asked about, which is the whole of what a test stating an anchor needs.</summary>
    private sealed class StatedTimeZones(MailUserTimeZone zone) : IMailUserTimeZones
    {
        public MailUserTimeZone ZoneOf(MailUserId user) => zone;

        public MailUserTimeZone? StatedZoneOf(MailUserId user) => zone;
    }
}

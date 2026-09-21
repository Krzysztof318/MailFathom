// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.Access;

/// <summary>Answers which zone one person's own days are read in.</summary>
/// <remarks>
/// <para>
/// It is a port for the reason <see cref="IUserLanguages" /> is one, and it is answered the same way: the value
/// comes out of that user's record, which is the host's, while every operation that has to resolve a relative period
/// lives above it. Resolution is synchronous and reaches no database, so an agent about to state its anchor never puts
/// a read in front of the call it is about to make.
/// </para>
/// <para>
/// There is no mail account's zone beside it, deliberately. What is derived from a mailbox already has a better anchor
/// than a configured zone would be — a message's own arrival instant, carrying the sender's offset — and an account
/// zone would only become worth having at a background derivation with no signed-in person and no instant in the data.
/// </para>
/// <para>
/// A user this deployment does not serve is answered with <see cref="UserTimeZone.Coordinated" /> rather than with
/// nothing, which is the same answer a record held from before the zone was asked for reads as. One value covers both,
/// so an operation resolving a period always has a zone and never one it had to decide for itself.
/// </para>
/// </remarks>
public interface IUserTimeZones
{
    /// <summary>Finds the zone one person's own days are read in.</summary>
    /// <param name="user">The user whose relative period is about to be resolved.</param>
    /// <returns>That user's zone, or the coordinated zone where this deployment serves no such user.</returns>
    UserTimeZone ZoneOf(UserId user);

    /// <summary>Finds the zone one person's own record states, where it states one.</summary>
    /// <param name="user">The user whose record is being read.</param>
    /// <returns>The stated zone, or <see langword="null" /> where the record states none and where this deployment serves no such user.</returns>
    /// <remarks>
    /// The same question <see cref="ZoneOf" /> answers, asked without the fallback, and it exists because one caller
    /// acts on the difference: a client offers somebody the zone their own machine reports exactly once, when nobody
    /// has asked them yet, and a deployment that could not tell that apart from a chosen UTC would offer it over a
    /// choice. Every other caller wants a zone rather than a fact about a record and asks <see cref="ZoneOf" />.
    /// </remarks>
    UserTimeZone? StatedZoneOf(UserId user);
}

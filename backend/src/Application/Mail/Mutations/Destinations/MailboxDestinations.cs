// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Folders;

namespace MailFathom.Application.Mail.Mutations.Destinations;

/// <summary>Every destination one batch of authored changes names, already turned into folders on the server.</summary>
/// <remarks>
/// <para>
/// It exists so that resolving a destination and writing a mutation down are two steps a caller cannot take in the
/// wrong order. Resolution can reach the mail server, which must not happen while a local transaction is open, so the
/// author is handed the answers rather than the resolver and cannot ask for one halfway through a commit.
/// </para>
/// <para>
/// A destination nobody asked to have resolved is reported as <see cref="MailboxDestinationOutcome.Unbound" />, which
/// is what an author with no answer for a folder truthfully has: nothing here invents a path, and the action fails
/// visibly rather than filing mail somewhere nobody named.
/// </para>
/// </remarks>
public sealed class MailboxDestinations
{
    private readonly IReadOnlyDictionary<MailFolderReference, MailboxDestinationResolution> resolutions;

    internal MailboxDestinations(IReadOnlyDictionary<MailFolderReference, MailboxDestinationResolution> resolutions) =>
        this.resolutions = resolutions;

    /// <summary>Gets the answers for a batch that named no destination at all.</summary>
    public static MailboxDestinations None { get; } =
        new(new Dictionary<MailFolderReference, MailboxDestinationResolution>());

    /// <summary>Finds what one destination resolved to.</summary>
    /// <param name="destination">The alias or role the author named.</param>
    /// <returns>The resolution, or an unbound result when this set was never asked about that destination.</returns>
    public MailboxDestinationResolution Find(MailFolderReference destination) =>
        this.resolutions.TryGetValue(destination, out var resolution)
            ? resolution
            : MailboxDestinationResolution.Unbound();
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;

namespace MailFathom.Application.SensitiveContent;

/// <summary>Answers what one owner's mail is scanned under, composed from the deployment's posture and their own.</summary>
/// <remarks>
/// <para>
/// The composition is one-directional and that is the whole rule: an owner may switch on a scanner the deployment left
/// off and refuse more of their own outgoing mail, and may never switch off or narrow what the deployment set. The
/// deployment operator carries the legal obligation, so what they required stands for every owner whose mail they hold;
/// what an owner adds is theirs alone. A write attempting the other direction is refused at the write, naming the
/// deployment setting it would narrow.
/// </para>
/// <para>
/// A record already held is composed rather than refused, so this port and the record are two different readings and a
/// consumer asking what is in force asks here. An owner whose record was accepted before the deployment tightened past
/// it goes on holding what they asked for while the stricter answer runs over their mail, and an owner who switched the
/// personal-data scanner on holds that answer after an operator removes the analyzer address, while nothing scans them
/// for it — a record that stayed authoritative through either would fail a deployment closed at its next start, from
/// behind the surface that would have let its owner rewrite it. The record states what the owner asked for; this states
/// what happens to their mail.
/// </para>
/// <para>
/// It is a port because the postures are composed from configuration, which is the host's, while every path that scans
/// lives above it. Resolution is synchronous and allocation-free on the common path: the answer follows the roster the
/// startup gate published and each owner-document commit republishes, so no path that scans puts a database read in
/// front of a scan.
/// </para>
/// </remarks>
public interface ISensitiveContentPostures
{
    /// <summary>Gets whether any owner this deployment serves has anything scanned for at all.</summary>
    /// <remarks>
    /// Read by a consumer deciding whether work only a scan makes necessary is worth arranging at all — opening a
    /// guarded operation, parsing a message back into values — never as permission to hand text on unguarded. A
    /// deployment where this is false constructs no detector and takes no permit on any path.
    /// </remarks>
    bool IsActiveForAnyOwner { get; }

    /// <summary>Gets what every owner this deployment serves has their mail scanned under, ordered by owner.</summary>
    /// <remarks>
    /// For the one consumer that judges rows belonging to several owners in one query — the walk that re-derives what
    /// was written under a posture nobody runs any more. The order is fixed so a value composed from it, such as the
    /// configuration a resume position was reached under, does not depend on the order the roster was published in.
    /// Everything else resolves the owner it is acting for and calls <see cref="ForOwner" />.
    /// </remarks>
    IReadOnlyList<OwnerSensitiveContentPosture> Current { get; }

    /// <summary>Finds what one owner's mail is scanned under.</summary>
    /// <param name="owner">The owner whose mail is about to be scanned, stored, or handed out.</param>
    /// <returns>That owner's posture, which scans nothing where neither the deployment nor the owner switched anything on.</returns>
    /// <remarks>
    /// An owner this deployment does not serve is answered with the deployment's own posture rather than with nothing.
    /// Their mail is not readable through any path that resolves a scope, so the answer is reached only by work
    /// racing an erasure, and the deployment's posture is the stricter of the two candidates.
    /// </remarks>
    SensitiveContentPosture ForOwner(MailOwnerId owner);

    /// <summary>Reports whether one scanner runs over anybody's mail on this deployment.</summary>
    /// <param name="scanner">The scanner to ask about.</param>
    /// <returns><see langword="true" /> when at least one owner's posture runs it.</returns>
    /// <remarks>
    /// Asked by what answers for a scanner's own dependency rather than by anything that scans: the readiness probe of
    /// the analyzer the personal-data scanner reaches. A deployment that stood that analyzer up and scans nobody's mail
    /// with it is not unready while the analyzer is silent, and one where a single owner switched it on is.
    /// </remarks>
    bool RunsForAnyOwner(SensitiveContentScannerKind scanner);
}

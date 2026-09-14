// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using MailFathom.Domain.Accounts;

namespace MailFathom.Application.SensitiveContent;

/// <summary>Answers what the mail in one account is scanned under, composed from the deployment's posture and the account's own.</summary>
/// <remarks>
/// <para>
/// The composition is one-directional and that is the whole rule: an account may switch on a scanner the deployment
/// left off and refuse more of its own outgoing mail, and may never switch off or narrow what the deployment set. The
/// deployment operator carries the legal obligation, so what they required stands for every mailbox they hold; what an
/// account adds is that mailbox's alone. A write attempting the other direction is refused at the write, naming the
/// deployment setting it would narrow.
/// </para>
/// <para>
/// It is the account rather than the user because the mail is one copy: a mailbox two people are assigned is scanned
/// once, under the settings written on the account they share, so neither of them reads mail the other's posture
/// judged. A path that reads across the accounts one user is assigned has no single account to ask about and asks
/// <see cref="AcrossAccountsOf" /> instead, which answers with the strictest of them.
/// </para>
/// <para>
/// A record already held is composed rather than refused, so this port and the record are two different readings and a
/// consumer asking what is in force asks here. An account whose record was accepted before the deployment tightened
/// past it goes on holding what it asked for while the stricter answer runs over its mail, and one that switched the
/// personal-data scanner on holds that answer after an operator removes the analyzer address, while nothing scans it
/// for that — a record that stayed authoritative through either would fail a deployment closed at its next start, from
/// behind the surface that would have let its settings be rewritten. The record states what was asked for; this states
/// what happens to the mail.
/// </para>
/// <para>
/// It is a port because the postures are composed from configuration, which is the host's, while every path that scans
/// lives above it. Resolution is synchronous and allocation-free on the common path: the answer follows the roster the
/// startup gate published and each account commit republishes, so no path that scans puts a database read in front of
/// a scan.
/// </para>
/// </remarks>
public interface ISensitiveContentPostures
{
    /// <summary>Gets whether any account this deployment serves has anything scanned for at all.</summary>
    /// <remarks>
    /// Read by a consumer deciding whether work only a scan makes necessary is worth arranging at all — opening a
    /// guarded operation, parsing a message back into values — never as permission to hand text on unguarded. A
    /// deployment where this is false constructs no detector and takes no permit on any path.
    /// </remarks>
    bool IsActiveForAnyAccount { get; }

    /// <summary>Gets what every account this deployment serves has its mail scanned under, ordered by account.</summary>
    /// <remarks>
    /// For the one consumer that judges rows belonging to several accounts in one query — the walk that re-derives what
    /// was written under a posture nobody runs any more. The order is fixed so a value composed from it, such as the
    /// configuration a resume position was reached under, does not depend on the order the roster was published in.
    /// Everything else resolves the account the mail belongs to and calls <see cref="ForAccount" />.
    /// </remarks>
    IReadOnlyList<MailAccountSensitiveContentPosture> Current { get; }

    /// <summary>Finds what the mail in one account is scanned under.</summary>
    /// <param name="account">The account whose mail is about to be scanned, stored, or handed out.</param>
    /// <returns>That account's posture, which scans nothing where neither the deployment nor the account switched anything on.</returns>
    /// <remarks>
    /// An account this deployment does not serve is answered with the deployment's own posture rather than with
    /// nothing. Its mail is not readable through any path that resolves a scope, so the answer is reached only by work
    /// racing an erasure, and the deployment's posture is the stricter of the two candidates.
    /// </remarks>
    SensitiveContentPosture ForAccount(MailAccountId account);

    /// <summary>Finds what a read spanning every account one user is assigned is scanned under.</summary>
    /// <param name="user">The user whose mail is about to be handed out.</param>
    /// <returns>The strictest posture over their accounts, which scans nothing where none of them switched anything on.</returns>
    /// <remarks>
    /// For a read that resolves a user and then hands out mail from whichever of their accounts matched — a search, a
    /// timeline, a question answered from a mailbox — where the value being guarded is several layers below the point
    /// the account could still be named. Composing the strictest of them is the safe direction: a message is scanned
    /// under at least what its own account asked for, and a scanner another of the user's accounts switched on only
    /// ever redacts more.
    /// </remarks>
    SensitiveContentPosture AcrossAccountsOf(MailUserId user);

    /// <summary>Reports whether one scanner runs over any mail on this deployment.</summary>
    /// <param name="scanner">The scanner to ask about.</param>
    /// <returns><see langword="true" /> when at least one account's posture runs it.</returns>
    /// <remarks>
    /// Asked by what answers for a scanner's own dependency rather than by anything that scans: the readiness probe of
    /// the analyzer the personal-data scanner reaches. A deployment that stood that analyzer up and scans no mail with
    /// it is not unready while the analyzer is silent, and one where a single account switched it on is.
    /// </remarks>
    bool RunsForAnyAccount(SensitiveContentScannerKind scanner);
}

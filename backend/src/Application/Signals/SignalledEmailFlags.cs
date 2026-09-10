// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Emails;

namespace MailFathom.Application.Signals;

/// <summary>Where one stored email's two server flags now stand, for the one signal that states a value rather than naming a place to look.</summary>
/// <param name="Email">The stored identity the client already holds the row for.</param>
/// <param name="IsSeen">Where the remote <c>\Seen</c> flag stands, or <see langword="null" /> where this statement is not about it.</param>
/// <param name="IsFlagged">Where the remote <c>\Flagged</c> flag stands, or <see langword="null" /> where this statement is not about it.</param>
/// <remarks>
/// <para>
/// Each value is optional because the two publishers know different amounts. A reconciliation window has the server's
/// whole answer and states both; a change this deployment authored has only the flag it asked for, and stating the
/// other one would be a guess about a value nobody wrote. A reader applies what is stated and leaves the rest alone,
/// which is also what makes applying the same statement twice the same state.
/// </para>
/// <para>
/// <b>It is still no mail.</b> Two booleans beside an identifier this deployment issued say nothing about a subject, a
/// sender, or a body, so the channel's posture is what it was — which is the whole reason the flags are the one change
/// worth stating rather than pointing at.
/// </para>
/// </remarks>
public readonly record struct SignalledEmailFlags(StoredEmailId Email, bool? IsSeen, bool? IsFlagged)
{
    /// <summary>Gets whether this states anything at all, which a statement about neither flag does not.</summary>
    public bool SaysAnything => this.IsSeen is not null || this.IsFlagged is not null;

    /// <summary>Takes what a later statement about the same email says, keeping whatever it is silent about.</summary>
    /// <param name="later">The statement raised after this one, about the same email.</param>
    /// <returns>The one statement that says what both said.</returns>
    /// <remarks>Per value rather than wholesale, because the two publishers state different halves: a folded window that stated both flags must not be erased by an authored change that stated one.</remarks>
    internal SignalledEmailFlags FoldedWith(SignalledEmailFlags later) =>
        this with { IsSeen = later.IsSeen ?? this.IsSeen, IsFlagged = later.IsFlagged ?? this.IsFlagged };
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.EmailContent.Release;

/// <summary>Bounds one release of retained database copies, and how long one is held before it may be freed at all.</summary>
/// <remarks>
/// Nothing here releases anything, and no interval elapsing does either. A release is an operator's request each time,
/// because it is the one irreversible step of the move — so what these two settle is how much one request frees and how
/// recently the copy it frees may have been verified.
/// </remarks>
public sealed class RetainedContentReleaseOptions
{
    /// <summary>Gets or sets how long a retained copy is held after its object was verified, before any release may free it.</summary>
    /// <remarks>
    /// <para>
    /// Zero by default, which is not the same as releasing on its own: the default hold is the operator's own decision,
    /// and nothing frees a copy until they ask. What a positive value adds is a floor beneath that decision — a
    /// deployment that wants a week of real reads against the bucket before the originals can go states a week here, and
    /// then even a release asked for on the same day frees nothing verified inside it.
    /// </para>
    /// <para>
    /// It is measured from when the move verified the object rather than from when the message was stored, because the
    /// question it answers is how long this deployment has been reading that object rather than how old the mail is.
    /// </para>
    /// </remarks>
    public TimeSpan SafetyInterval { get; set; } = TimeSpan.Zero;

    /// <summary>Gets or sets how many retained copies one release frees before it answers.</summary>
    /// <remarks>
    /// The bound is on one request rather than on the operation: the command repeats it until nothing is left, and
    /// answering after each batch is what makes an interrupted release resumable rather than a state nothing can finish.
    /// What it costs the database is one bounded read and one narrow update per batch.
    /// </remarks>
    public int PayloadsPerBatch { get; set; } = 200;
}

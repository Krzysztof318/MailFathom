// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures whether one account keeps a durable record of the changes MailFathom made to its mailbox.</summary>
/// <remarks>
/// <para>
/// The trail answers "why is this message in this folder" months later, without depending on anybody's memory and
/// without holding any of the message. It is a second store from the operational mutation record, which exists only
/// until a change finishes, and it is off by default because the two decisions are different: making a change correctly
/// is something MailFathom does for every account, while keeping a history of where a person's mail has been is
/// something an operator undertakes to hold, describe, and erase.
/// </para>
/// <para>
/// Turning it off stops new entries and leaves the ones already written to age out under <see cref="Retention" />, which
/// is why the window is configured whether or not the trail is currently on.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailboxMutationAuditTrailOptions
{
    /// <summary>The shortest retention accepted, below which an entry could be erased before the run that would show it.</summary>
    internal static readonly TimeSpan MinimumRetention = TimeSpan.FromDays(1);

    /// <summary>The longest retention accepted, beyond which the window stops being one anybody could justify holding.</summary>
    internal static readonly TimeSpan MaximumRetention = TimeSpan.FromDays(3650);

    /// <summary>Gets or sets whether a finished change to this account's mailbox leaves an audit entry behind.</summary>
    /// <remarks>
    /// It is read when a change is written down, not when it finishes, and the answer travels on the record. A change
    /// authored while the trail was on therefore still leaves its entry after the trail is switched off, which is what
    /// stops a mid-flight toggle from producing a history whose gaps look like changes nobody made.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how long an entry of this account's is kept before it is erased.</summary>
    /// <remarks>
    /// <para>
    /// Erasure rides the account's own synchronization run, so the window is honored as often as the account comes
    /// round rather than to the minute. It is read from the current configuration rather than from the entries, so
    /// shortening it applies to the history already held as much as to what is still to come — which is what makes it a
    /// storage-limitation decision an operator can actually change their mind about.
    /// </para>
    /// <para>
    /// The floor is a day because a shorter window would erase entries before the run that would have shown them, and
    /// the ceiling is ten years because a retention nobody can justify is the thing this setting exists to prevent. The
    /// bounds are checked by the account's own validation rather than by a data annotation, because nothing binds this
    /// nested block as an options graph of its own and an annotation here would never be read.
    /// </para>
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(90);
}

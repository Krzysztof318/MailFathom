// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;

namespace MailFathom.Host.Configuration.Mail;

/// <summary>Configures whether one account keeps a durable record of the questions answered from its mailbox.</summary>
/// <remarks>
/// <para>
/// The record answers "which of my messages did that answer come from" months later, without holding any of the
/// messages. An answer produced by a model is not reproducible, so the only way to explain one afterwards is to have
/// recorded what produced it — and it is off by default because it says what a person's mail was read for, which is
/// something an operator undertakes to hold, describe, and erase rather than something MailFathom accumulates for
/// everyone.
/// </para>
/// <para>
/// A separate decision from the mutation trail beside it, and deliberately not the same switch. One record says where a
/// person's mail has been; this says what it was read for. An operator may want either without the other.
/// </para>
/// <para>
/// Turning it off stops new entries and leaves the ones already written to age out under <see cref="Retention" />, which
/// is why the window is configured whether or not the record is currently on.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class MailAnsweringAuditTrailOptions
{
    /// <summary>The shortest retention accepted, below which an entry could be erased before the reader that would show it.</summary>
    internal static readonly TimeSpan MinimumRetention = TimeSpan.FromDays(1);

    /// <summary>The longest retention accepted, beyond which the window stops being one anybody could justify holding.</summary>
    internal static readonly TimeSpan MaximumRetention = TimeSpan.FromDays(3650);

    /// <summary>Gets or sets whether a question answered from this account's mailbox leaves an entry behind.</summary>
    /// <remarks>
    /// It is read as a run ends rather than carried from when it began, which is the difference from the mutation
    /// trail's own switch. A mutation is authored and converged over minutes or hours, so a toggle flipped mid-flight
    /// would leave gaps that look like changes nobody made; a run is one request, and there is no window worth carrying
    /// an answer across.
    /// </remarks>
    public bool Enabled { get; set; }

    /// <summary>Gets or sets how long an entry of this account's is kept before it is erased.</summary>
    /// <remarks>
    /// <para>
    /// Erasure rides the account's own synchronization run, so the window is honored as often as the account comes
    /// round rather than to the minute. It is read from the current configuration rather than from the entries, so
    /// shortening it applies to the record already held as much as to what is still to come — which is what makes it a
    /// storage-limitation decision an operator can actually change their mind about.
    /// </para>
    /// <para>
    /// The floor is a day because a shorter window would erase entries before anybody could read them, and the ceiling
    /// is ten years because a retention nobody can justify is the thing this setting exists to prevent. The bounds are
    /// checked by the account's own validation rather than by a data annotation, because nothing binds this nested block
    /// as an options graph of its own and an annotation here would never be read.
    /// </para>
    /// <para>
    /// The default is shorter than the mutation trail's. What a mailbox change did stays worth knowing for as long as
    /// the mail does, while what one question read is diagnostic evidence about an answer somebody has in front of them
    /// now — and every entry names messages, so the record grows with how much this instance is asked rather than with
    /// how much it is told to change.
    /// </para>
    /// </remarks>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);
}

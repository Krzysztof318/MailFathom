// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using MailFathom.Infrastructure.Security.Passwords;

namespace MailFathom.Host.Configuration.Access;

/// <summary>States that a user-facing endpoint accepts a username and password, and how often one may be tried.</summary>
/// <remarks>
/// <para>
/// The block is optional on the entry that names the method, which is the whole of what it is for: it carries no
/// credential, because a username and a password are records of one user's own, provisioned through the administrative
/// surface and stored in relational columns. There is nothing for an operator to write here beyond the bound, and
/// nothing for a deployment file to leak.
/// </para>
/// <para>
/// An endpoint carries at most one entry accepting a password, and startup refuses a second. A presented credential
/// names a username rather than an entry, so two would leave which bound applies decided by configuration order, and
/// rotation is a second credential row rather than a second entry.
/// </para>
/// <para>
/// The one setting is the bound on guessing, and it is per source and per username rather than per endpoint because
/// those are the two shapes an attack takes: one host trying many passwords, and many hosts trying one account's. It is
/// separate from the endpoint's own <c>RateLimiting</c> section, which bounds requests and counts an unauthenticated
/// caller in the surface's shared bucket — a bound on the endpoint rather than on the guessing.
/// </para>
/// </remarks>
[SuppressMessage("Performance", "CA1812:Avoid uninstantiated internal classes", Justification = "The options framework materializes this type during configuration binding.")]
internal sealed class BasicAuthenticationOptions
{
    /// <summary>How many attempts one source and one username each get per minute where a deployment states nothing.</summary>
    /// <remarks>Readable rather than only a property initializer, because an entry accepting a password may write no block at all — the method is named rather than selected by the block's presence — and the registration then has to reach the same number the binder would have left.</remarks>
    internal const int DefaultAttemptsPerMinute = 10;

    /// <summary>The most attempts a deployment may allow one source or one username each minute.</summary>
    /// <remarks>Past this the setting stops being a bound: a thousand verifications a minute against one username is an offline guessing rate rather than a person mistyping a password, and an operator who wants that has misread what the setting is for.</remarks>
    internal const int MaximumAttemptsPerMinute = 600;

    /// <summary>How many password verifications the surface may have in flight at once where a deployment states nothing.</summary>
    /// <remarks>Four times what one source or one username may have in flight, so several users signing in together each keep that smaller bound meaningful. Readable for the reason <see cref="DefaultAttemptsPerMinute" /> is.</remarks>
    internal const int DefaultMaxConcurrentVerifications = 128;

    /// <summary>The most password verifications a deployment may let the surface have in flight at once.</summary>
    /// <remarks>Every verification is a deliberately expensive derivation occupying a thread, so a number past this is a bound on nothing a replica has: it would let a caller varying the username queue far more derivations than any machine runs at once.</remarks>
    internal const int MaximumConcurrentVerifications = 512;

    /// <summary>Gets or sets how many password attempts one source and one username each get per minute.</summary>
    /// <remarks>
    /// <para>
    /// Ten, which is a person mistyping a password several times and correcting it, and is nowhere near a rate at which
    /// guessing a password of the length this deployment requires becomes feasible.
    /// </para>
    /// <para>
    /// <strong>It counts wrong passwords rather than requests.</strong> A right password costs the allowance nothing
    /// however often it is presented, which is what Basic makes a client do, having no session — and nothing here caps
    /// how many requests a user may have in flight, which is a separate bound the limiter states for itself.
    /// </para>
    /// <para>
    /// <strong>A wrong password holds its share for a minute and then gives it back.</strong> So an axis that has spent
    /// its allowance waits the window out rather than being locked out until an operator lifts something. That window is
    /// the cost of the per-username axis being shared with the user it protects: somebody who knows a username can
    /// spend it on wrong passwords and have that user's correct password refused until the minute elapses. Nothing can
    /// avoid that while the answer is unknowable before the derivation — what a lower number here buys in guessing cost
    /// it pays for in how cheaply a stranger can hold one user out, and what the per-source axis buys is catching the
    /// caller doing it wherever this deployment can tell one caller from another.
    /// </para>
    /// </remarks>
    public int AttemptsPerMinute { get; set; } = DefaultAttemptsPerMinute;

    /// <summary>Gets or sets how many password verifications the surface may have in flight at once, whatever usernames they name.</summary>
    /// <remarks>
    /// <para>
    /// A bound on this replica's work rather than on guessing: every distinct username is a partition of its own, so
    /// without it a caller varying the name would have the process derive once per connection it opened. A roster
    /// signing in together — each browser re-presenting the credential on several connections — is what raises it, and
    /// a replica with few cores is what lowers it.
    /// </para>
    /// <para>
    /// It cannot go below what one source or one username may have in flight, because a surface ceiling under that
    /// would make the smaller bound unreachable and refuse a single user's parallel calls with the answer a wrong
    /// password gets.
    /// </para>
    /// </remarks>
    public int MaxConcurrentVerifications { get; set; } = DefaultMaxConcurrentVerifications;

    /// <summary>Finds everything an operator must fix before the method can guard an endpoint.</summary>
    /// <param name="settingPath">The configuration path this block was bound from, which every message is written against.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> FindConfigurationErrors(string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        var errors = new List<string>();

        if (this.AttemptsPerMinute is <= 0 or > MaximumAttemptsPerMinute)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1} — '{2}' is not a bound this deployment will run under. Write a number between 1 and {3}; the "
                + "default of 10 is a person correcting a mistyped password, and anything near the ceiling is a guessing "
                + "rate rather than a bound.",
                settingPath,
                nameof(this.AttemptsPerMinute),
                this.AttemptsPerMinute,
                MaximumAttemptsPerMinute));
        }

        if (this.MaxConcurrentVerifications is < PasswordAttemptLimiter.ConcurrentVerificationsPerPartition
            or > MaximumConcurrentVerifications)
        {
            errors.Add(string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1} — '{2}' is not a bound this deployment will run under. Write a number between {3}, which is "
                + "what one source or one username may already have in flight, and {4}, past which the surface would "
                + "queue more password derivations than a replica runs at once.",
                settingPath,
                nameof(this.MaxConcurrentVerifications),
                this.MaxConcurrentVerifications,
                PasswordAttemptLimiter.ConcurrentVerificationsPerPartition,
                MaximumConcurrentVerifications));
        }

        return errors;
    }
}

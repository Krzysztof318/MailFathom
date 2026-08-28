// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace MailFathom.Host.Configuration.Access;

/// <summary>States that an owner-facing endpoint accepts a username and password, and how often one may be tried.</summary>
/// <remarks>
/// <para>
/// The block's presence is what selects the method, exactly as every other block on an entry is. What it does not carry
/// is a credential: a username and a password are records of one owner's own, provisioned through the administrative
/// surface and stored in relational columns, so there is nothing for an operator to write here and nothing for a
/// deployment file to leak. That is the whole difference between this method and the configured ones beside it — the
/// deployment states that the method is available and the database states who may use it.
/// </para>
/// <para>
/// An endpoint carries at most one of these, and startup refuses a second. A presented credential names a username
/// rather than an entry, so two Basic entries would leave which one's grant a caller holds decided by configuration
/// order, and rotation — the reason a second entry exists for the other methods — is a second credential row here
/// rather than a second block.
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
    /// <summary>The most attempts a deployment may allow one source or one username each minute.</summary>
    /// <remarks>Past this the setting stops being a bound: a thousand verifications a minute against one username is an offline guessing rate rather than a person mistyping a password, and an operator who wants that has misread what the setting is for.</remarks>
    internal const int MaximumAttemptsPerMinute = 600;

    /// <summary>Gets or sets how many password attempts one source and one username each get per minute.</summary>
    /// <remarks>
    /// <para>
    /// Ten, which is a person mistyping a password several times and correcting it, and is nowhere near a rate at which
    /// guessing a password of the length this deployment requires becomes feasible.
    /// </para>
    /// <para>
    /// <strong>It counts wrong passwords rather than requests.</strong> A right password costs the allowance nothing
    /// however often it is presented, which is what Basic makes a client do, having no session — and nothing here caps
    /// how many requests an owner may have in flight, which is a separate bound the limiter states for itself.
    /// </para>
    /// <para>
    /// <strong>A wrong password holds its share for a minute and then gives it back.</strong> So an axis that has spent
    /// its allowance waits the window out rather than being locked out until an operator lifts something. That window is
    /// the cost of the per-username axis being shared with the owner it protects: somebody who knows a username can
    /// spend it on wrong passwords and have that owner's correct password refused until the minute elapses. Nothing can
    /// avoid that while the answer is unknowable before the derivation — what a lower number here buys in guessing cost
    /// it pays for in how cheaply a stranger can hold one owner out, and what the per-source axis buys is catching the
    /// caller doing it wherever this deployment can tell one caller from another.
    /// </para>
    /// </remarks>
    public int AttemptsPerMinute { get; set; } = 10;

    /// <summary>Finds everything an operator must fix before the method can guard an endpoint.</summary>
    /// <param name="settingPath">The configuration path this block was bound from, which every message is written against.</param>
    /// <returns>One message per faulty setting, each naming its configuration path, empty when the settings are usable.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="settingPath" /> is <see langword="null" />.</exception>
    public IReadOnlyList<string> FindConfigurationErrors(string settingPath)
    {
        ArgumentNullException.ThrowIfNull(settingPath);

        if (this.AttemptsPerMinute is > 0 and <= MaximumAttemptsPerMinute)
        {
            return [];
        }

        return
        [
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1} — '{2}' is not a bound this deployment will run under. Write a number between 1 and {3}; the "
                + "default of 10 is a person correcting a mistyped password, and anything near the ceiling is a guessing "
                + "rate rather than a bound.",
                settingPath,
                nameof(this.AttemptsPerMinute),
                this.AttemptsPerMinute,
                MaximumAttemptsPerMinute),
        ];
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Rules;

/// <summary>Which rules of a set one walk runs: the ones an automatic trigger reaches, or every rule declared.</summary>
/// <remarks>
/// <para>
/// A rule declares the automatic triggers it takes part in, so a walk started by one of them reaches the rules that
/// named it and passes over the rest. A walk somebody asked for is not a trigger and is never one a rule opts into: an
/// operator asking for a run is the request itself, and a rule declining to run because it had not agreed to be asked
/// would be surprising in the one place surprise is least affordable. Such a walk therefore reaches every rule of the
/// set, manual-only rules included.
/// </para>
/// <para>
/// A type of its own rather than a nullable trigger, because the two are different statements rather than a value and
/// its absence, and because the paragraph above is a rule that belongs somewhere a reader can find it.
/// </para>
/// </remarks>
public sealed record MailRuleReach
{
    private readonly MailRuleTrigger trigger;

    private MailRuleReach(MailRuleTrigger trigger) => this.trigger = trigger;

    /// <summary>Gets the reach of a walk somebody asked for, which is every rule the set declares.</summary>
    /// <remarks>The unspecified trigger is what carries that here: no trigger started this walk, so none filters it.</remarks>
    public static MailRuleReach EveryRule { get; } = new(default(MailRuleTrigger));

    /// <summary>Reads the reach of a walk one automatic trigger started.</summary>
    /// <param name="trigger">The trigger the walk runs for.</param>
    /// <returns>The reach, which is the rules declaring that trigger.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="trigger" /> is the unspecified struct default.</exception>
    public static MailRuleReach TriggeredBy(MailRuleTrigger trigger) => trigger.IsSpecified
        ? new MailRuleReach(trigger)
        : throw new ArgumentException("A walk cannot be started by a trigger that is unspecified.", nameof(trigger));

    /// <summary>Reports whether one rule takes part in this walk.</summary>
    /// <param name="rule">The rule the walk has reached in declared order.</param>
    /// <returns><see langword="true" /> when the walk runs the rule.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="rule" /> is <see langword="null" />.</exception>
    public bool Reaches(MailRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        return !this.trigger.IsSpecified || rule.RunsOn(this.trigger);
    }

    /// <inheritdoc />
    public override string ToString() => this.trigger.IsSpecified ? this.trigger.Name : "every rule";
}

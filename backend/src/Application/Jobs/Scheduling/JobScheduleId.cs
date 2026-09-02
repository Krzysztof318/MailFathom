// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Jobs.Scheduling;

/// <summary>Identifies one recurring dispatch across restarts, reloads, and replicas.</summary>
/// <remarks>
/// <para>
/// The identity is what the durable state of a schedule is keyed by, so it has to survive a process and mean the same
/// thing on every instance reading one configuration. It is therefore composed of what the declaration names — an
/// account identifier, a rule name — rather than generated, and it changes only when the declaration it stands for is a
/// different one. A schedule whose identity changed would be seeded afresh and would forget the occasion it last
/// dispatched.
/// </para>
/// <para>
/// It is composed of MailFathom's own names, never of anything out of a message, for the reason
/// <see cref="JobIdempotencyKey" /> is: an operator reads this text when they ask why a scheduled run has not happened,
/// and personal data in it would make the schedule table a second uncontrolled copy of it.
/// </para>
/// </remarks>
public sealed record JobScheduleId
{
    /// <summary>The greatest length an identity may have, which bounds the column it is keyed by.</summary>
    public const int MaximumLength = 256;

    private JobScheduleId(string value) => this.Value = value;

    /// <summary>Gets the text the schedule's durable state is keyed by.</summary>
    public string Value { get; }

    /// <summary>Creates an identity from the text whoever declares the schedule composed.</summary>
    /// <param name="value">The composed identity of one recurring dispatch.</param>
    /// <returns>A validated schedule identity.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="value" /> is blank, carries a control character, or is longer than <see cref="MaximumLength" />.</exception>
    public static JobScheduleId Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var trimmedValue = value.Trim();

        if (trimmedValue.Length > MaximumLength)
        {
            throw new ArgumentException(
                $"A job schedule identity may be at most {MaximumLength} characters long.",
                nameof(value));
        }

        // A control character would make the identity unreadable in the query an operator asks a schedule's state with,
        // and it is never part of a name an operator wrote or an identity MailFathom generated.
        if (trimmedValue.Any(char.IsControl))
        {
            throw new ArgumentException("A job schedule identity cannot contain a control character.", nameof(value));
        }

        return new JobScheduleId(trimmedValue);
    }

    /// <inheritdoc />
    public override string ToString() => this.Value;
}

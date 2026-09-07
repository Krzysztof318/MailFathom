// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Enrichment;

/// <summary>Says what produced one mark and what within that source it was: a rule's name, or an agent's.</summary>
/// <remarks>
/// <para>
/// Provenance travels with every mark rather than with the message's enrichment as a whole, because one message's marks
/// may come from different places: a date a deterministic reading of the message settles is not the same standing as a
/// sentence a model wrote, and a record naming only the derivation that wrote the row would leave a reader unable to
/// tell which of them a mark actually rests on.
/// </para>
/// <para>
/// The origin is MailFathom's own name for the producer — a rule identity or a composed agent's name — and never
/// anything a message carried. It is refused rather than shortened when it is over-long, for the reason a spam signal's
/// provenance refuses one: every origin this system writes is short by construction, so a long one is a producer that
/// named itself wrongly rather than a name to trim.
/// </para>
/// </remarks>
public sealed record EmailEnrichmentProvenance
{
    /// <summary>The greatest length an origin may carry.</summary>
    public const int MaximumOriginLength = 128;

    private EmailEnrichmentProvenance(EmailEnrichmentSource source, string origin)
    {
        this.Source = source;
        this.Origin = origin;
    }

    /// <summary>Gets what produced the mark.</summary>
    public EmailEnrichmentSource Source { get; }

    /// <summary>Gets what within that source produced it: a rule identity, or the name the agent was composed under.</summary>
    public string Origin { get; }

    /// <summary>Records that a mark was produced by one of this deployment's own deterministic rules.</summary>
    /// <param name="ruleIdentity">The rule that produced it.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the identity is blank, over-long, or carries a control character.</exception>
    /// <remarks>
    /// No rule writes a mark yet, and the constructor exists anyway because the distinction it draws is what the record
    /// is for: a shape that could only ever express one of the two would make the source column a constant, and the
    /// first deterministic producer would then arrive as a schema change rather than as a caller.
    /// </remarks>
    public static EmailEnrichmentProvenance FromRule(string ruleIdentity) =>
        new(EmailEnrichmentSource.DeterministicRule, Checked(ruleIdentity, nameof(ruleIdentity)));

    /// <summary>Records that a mark was produced by a model, named by the agent the derivation was composed as.</summary>
    /// <param name="agentName">The name the agent runs under.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the name is blank, over-long, or carries a control character.</exception>
    public static EmailEnrichmentProvenance FromAgent(string agentName) =>
        new(EmailEnrichmentSource.Model, Checked(agentName, nameof(agentName)));

    /// <summary>Reads back a provenance this system recorded earlier.</summary>
    /// <param name="source">What produced the mark.</param>
    /// <param name="origin">What within that source produced it.</param>
    /// <returns>The provenance.</returns>
    /// <exception cref="ArgumentException">Thrown when the origin is blank, over-long, or carries a control character.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="source" /> is not a defined member.</exception>
    public static EmailEnrichmentProvenance Restore(EmailEnrichmentSource source, string origin)
    {
        if (!Enum.IsDefined(source))
        {
            throw new ArgumentOutOfRangeException(
                nameof(source),
                source,
                "A recorded mark names one of the sources this system derives marks from.");
        }

        return new EmailEnrichmentProvenance(source, Checked(origin, nameof(origin)));
    }

    private static string Checked(string origin, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(origin, parameterName);

        var trimmed = origin.Trim();

        if (trimmed.Length > MaximumOriginLength)
        {
            throw new ArgumentException(
                $"An enrichment origin carries at most {MaximumOriginLength} characters.",
                parameterName);
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("An enrichment origin cannot contain control characters.", parameterName);
        }

        return trimmed;
    }
}

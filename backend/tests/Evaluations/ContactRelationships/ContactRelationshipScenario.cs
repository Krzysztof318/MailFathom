// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.AI.ContactRelationships;
using MailFathom.AI.Orchestration;
using MailFathom.Domain.Access;
using MailFathom.Evaluations.Corpus;
using MailFathom.Evaluations.StructuredAnswers;
using MailFathom.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace MailFathom.Evaluations.ContactRelationships;

/// <summary>Puts one person's correspondence to the relationship agent and holds the card to what the correspondence says.</summary>
/// <remarks>
/// The turn is the one a derivation composes and the answer is read through <see cref="ContactRelationshipReading" />, so a
/// line the reading drops — one citing nothing the turn numbered — is absent here exactly as it would be from the card.
/// No case reads two ways, so none is judged.
/// </remarks>
internal static class ContactRelationshipScenario
{
    /// <summary>The name every case's scenario name begins with.</summary>
    public const string Name = "ContactRelationship";

    /// <summary>The name the deterministic verdict is recorded under.</summary>
    public const string ExpectationMetricName = "Card as expected";

    /// <summary>Describes one correspondence as the case the shared scenario runs.</summary>
    /// <param name="scenario">The correspondence.</param>
    /// <returns>The request.</returns>
    public static StructuredAnswerRequest RequestFor(ContactRelationshipCase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        var correspondence = scenario.Correspondence;
        var language = scenario.Language ?? UserLanguage.English;
        var instruction = ContactRelationshipInstructions.TextFor(language);

        return new StructuredAnswerRequest(
            $"{Name}.{scenario.Name}",
            instruction,
            (plan, cancellationToken) => ContactRelationshipAgent.ComposeTurnAsync(
                correspondence,
                SensitiveContentEgressGuards.Inactive(),
                plan,
                cancellationToken),
            (model, plan) => ContactRelationshipAgentComposition.Compose(
                model,
                plan,
                language,
                new EmptyAgentInstructionEnvelope(),
                NullLoggerFactory.Instance),
            answer => HostileMail.Obeyed(answer, instruction) ?? (StructuredAnswerScenario.IsReadableObject(answer)
                ? scenario.Held(ContactRelationshipReading.Read(answer, correspondence), correspondence)
                : "the answer holds no JSON object, so the contact would be shown no card."),
            ExpectationMetricName,
            StructuredAnswerScenario.JudgedWhen(readsTwoWays: false));
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Application.Contacts.Collection;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Reports how a contact book is filling from arriving mail, and by whose decision.</summary>
/// <remarks>
/// <para>
/// Collection is the one part of this system that writes personal data about third parties without anybody asking it
/// to, so an owner who switched it on is owed a way to see what it is doing. The counter is that way: one measurement
/// per address considered, tagged with which of the six conclusions was reached, so a book filling too fast, a policy
/// excluding everything, and a run repeatedly stopping at its ceiling are all readable apart from each other.
/// </para>
/// <para>
/// The one tag is MailFathom's own closed set. No address, no name, no display name, no folder, and no message identity
/// reaches an instrument from collection — the outcome is a decision about a person and never the person.
/// </para>
/// </remarks>
public sealed class ContactCollectionTelemetry : IContactCollectionTelemetry
{
    private const string OutcomeTagName = "mailfathom.contacts.collection.outcome";

    private readonly Counter<long> outcomeCount;

    /// <summary>Initializes the instrument collection reports through.</summary>
    public ContactCollectionTelemetry() =>
        this.outcomeCount = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.contacts.collection.decisions",
            unit: "{decision}",
            description: "Decisions collection reached about one address arriving mail carried, by outcome.");

    /// <inheritdoc />
    public void RecordOutcome(ContactCollectionOutcome outcome) =>
        this.outcomeCount.Add(1, new TagList { { OutcomeTagName, TagOf(outcome) } });

    private static string TagOf(ContactCollectionOutcome outcome) => outcome switch
    {
        ContactCollectionOutcome.AlreadyHeld => "already_held",
        ContactCollectionOutcome.BelowThreshold => "below_threshold",
        ContactCollectionOutcome.Excluded => "excluded",
        ContactCollectionOutcome.NotCorrespondence => "not_correspondence",
        ContactCollectionOutcome.RunBoundReached => "run_bound_reached",
        _ => "recorded",
    };
}

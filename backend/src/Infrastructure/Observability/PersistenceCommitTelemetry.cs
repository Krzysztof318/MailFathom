// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Diagnostics;
using System.Diagnostics.Metrics;
using MailFathom.Common.Observability;

namespace MailFathom.Infrastructure.Observability;

/// <summary>Counts how every local write transaction ended, which is the only place a conflict rate is visible.</summary>
/// <remarks>
/// <para>
/// An optimistic concurrency conflict is an expected branch here rather than a failure: the retry policy commits again
/// from a fresh read, and a conflict it resolves leaves no trace at all. What does leave one is the conflict nobody
/// resolved, which arrives as a single exception after every allowed attempt has been spent — so today a deployment
/// where two writers race constantly and a deployment where they never meet look identical until the day the retries
/// stop being enough. The rate is what separates them, and it is the reading that says a bound wants raising before
/// anybody sees the exception.
/// </para>
/// <para>
/// Every outcome is counted rather than the conflicts alone, because a rate needs the writes it is a rate of, and the
/// denominator has to be MailFathom's own sessions: EF Core's own meter counts every <c>SaveChanges</c> this process
/// issues, including the ones no session and no retry policy own.
/// </para>
/// <para>
/// The one dimension is the outcome, and it is four of this type's own words. What was being written, which entities it
/// touched, and which constraint the database refused are all deliberately absent — the first two would carry mail and
/// the third is the provider's text rather than a set anybody chose.
/// </para>
/// </remarks>
internal sealed class PersistenceCommitTelemetry
{
    internal const string OutcomeTagName = "mailfathom.persistence.commit.outcome";

    /// <summary>Names a session whose write became durable.</summary>
    internal const string CommittedOutcomeName = "committed";

    /// <summary>Names a session a competing writer beat, which the caller's retry policy may resolve.</summary>
    internal const string ConcurrencyConflictOutcomeName = "concurrency_conflict";

    /// <summary>Names a session the database dropped before the commit, which the caller's retry policy may resolve.</summary>
    internal const string TransientFailureOutcomeName = "transient_failure";

    /// <summary>Names a session whose commit round trip went unanswered, which nothing may resolve by repeating it.</summary>
    internal const string CommitOutcomeUnknownOutcomeName = "outcome_unknown";

    private readonly Counter<long> commits;

    /// <summary>Initializes the instrument every commit attempt is counted on.</summary>
    public PersistenceCommitTelemetry() =>
        this.commits = Telemetry.Meter.CreateCounter<long>(
            "mailfathom.persistence.commits",
            unit: "{commit}",
            description: "Local write transactions this process completed, by whether they committed, lost a race, met a database failure that can clear on its own, or lost the connection while committing.");

    /// <summary>Counts a session whose write became durable.</summary>
    public void RecordCommitted() => this.Record(CommittedOutcomeName);

    /// <summary>Counts a session rolled back because a competing writer got there first.</summary>
    public void RecordConcurrencyConflict() => this.Record(ConcurrencyConflictOutcomeName);

    /// <summary>Counts a session the database ended by failing in a way that can clear on its own.</summary>
    public void RecordTransientFailure() => this.Record(TransientFailureOutcomeName);

    /// <summary>Counts a session whose commit round trip went unanswered, leaving its outcome unread.</summary>
    public void RecordCommitOutcomeUnknown() => this.Record(CommitOutcomeUnknownOutcomeName);

    private void Record(string outcome) => this.commits.Add(1, new TagList { { OutcomeTagName, outcome } });
}

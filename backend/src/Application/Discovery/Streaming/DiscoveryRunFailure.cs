// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Streaming;

/// <summary>Why a run stopped without finishing, in the terms a client can act on.</summary>
/// <remarks>
/// <para>
/// A closed set rather than a message, because what ends a run is read by a screen deciding what to offer next — retry
/// now, retry later, or say the deployment does not do this — and because an exception's own text is written for an
/// operator and may name a folder, a filter, or an account. Nothing derived from the question or from the mail reaches
/// a client through this.
/// </para>
/// <para>
/// Three of the members are not faults at all and are here because they end a run the same way: the person stopped it,
/// or one of the two spend ceilings refused it. A ceiling reached is the deployment behaving exactly as its operator
/// configured it, and rendering that as an error produces somebody retrying three times something that will not become
/// cheaper — which is what
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0022-what-an-ai-run-reports-about-cost-cancellation-and-the-model.md">ADR 0022</see>
/// makes them states rather than a failure carrying a code.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<DiscoveryRunFailure>))]
public enum DiscoveryRunFailure
{
    /// <summary>This deployment answers no questions about mail, so no retry will change the outcome.</summary>
    Unavailable = 0,

    /// <summary>This deployment answers questions and currently cannot, so the same question is worth asking again.</summary>
    TemporarilyUnavailable = 1,

    /// <summary>Every lookup the plan held carried a filter this deployment refuses, so there was nothing to answer from.</summary>
    RetrievalRefused = 2,

    /// <summary>The run reached the longest a run may take and was stopped where it stood.</summary>
    TimedOut = 3,

    /// <summary>The run ended for a reason it does not publish, which an operator reads in the deployment's own logs.</summary>
    Failed = 4,

    /// <summary>
    /// The deployment stopped while the run was executing, so nothing was established about the question and asking it
    /// again is worth doing once the deployment is back.
    /// </summary>
    /// <remarks>
    /// Kept apart from <see cref="TimedOut" /> because the two are the same cancellation to the code that observes it
    /// and opposite facts to a person reading it: a timeout says this question was more than one run could answer, and
    /// this says the run never got its turn.
    /// </remarks>
    Stopped = 5,

    /// <summary>The person who started the run stopped it, and everything it had already published still stands.</summary>
    /// <remarks>
    /// Not a fault, and the third value the same cancellation reaches this type as. It is published as an ending rather
    /// than as a completion because the run was stopped before it composed what it was going to compose — but what it
    /// had already published is kept, which is the whole reason stopping is worth offering. Cancelling buys the
    /// remainder and never a refund: what the run had already spent stays spent, in this run's counts and in the
    /// period's.
    /// </remarks>
    Cancelled = 6,

    /// <summary>
    /// This deployment has spent what it allows answering to cost for the current period, so nothing about the question
    /// caused the refusal and the same question is worth asking once the period turns over.
    /// </summary>
    /// <remarks>
    /// The one ending that names an instant: <see cref="DiscoveryRunFailed.RetryAt" /> carries the roll-over the fixed
    /// epoch-anchored window places, so a client re-enables the question then instead of offering a retry that will be
    /// refused. It names the deployment rather than the person, the ceiling being deployment-wide, and it names no
    /// consumed amount at all — how much of the period is left is a fact about what everybody on the deployment has been
    /// asking.
    /// </remarks>
    PeriodSpent = 7,

    /// <summary>The run reached what this deployment allows one question to spend and was stopped before an answer was written.</summary>
    /// <remarks>
    /// It names no instant, because asking the same question again reaches the same ceiling by the same route. The one
    /// thing the person can change is how much the question asks for.
    /// </remarks>
    RunSpent = 8,
}

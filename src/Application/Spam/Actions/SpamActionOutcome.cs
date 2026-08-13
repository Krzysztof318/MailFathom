// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Spam.Actions;

/// <summary>How one attempt to act on a spam verdict ended.</summary>
/// <remarks>
/// Every member but <see cref="Requested" /> is a reason no mailbox was written to, and none of them is a failure. They
/// are separate members rather than one negative because a caller reporting what a run did — above all a run an operator
/// asked for over a whole mailbox — has to say why a message was left alone, and <em>nothing to change</em> and <em>the
/// owner has already moved this back</em> are different answers to that question.
/// </remarks>
public enum SpamActionOutcome
{
    /// <summary>At least one change was written down for the account's convergence pass to carry out.</summary>
    Requested = 0,

    /// <summary>Neither switch is on, so nothing was read and nothing was asked for.</summary>
    NoActionConfigured = 1,

    /// <summary>The verdict is not spam, which includes a classification that concluded nothing either way.</summary>
    NotSpam = 2,

    /// <summary>A scanner decided the verdict and its score is below the score the operator acts at.</summary>
    BelowThreshold = 3,

    /// <summary>Nothing is stored under that identifier, which is what an expunged message reaches.</summary>
    OccurrenceMissing = 4,

    /// <summary>Filing is on and the destination named no folder on the account's server.</summary>
    /// <remarks>
    /// <para>
    /// It is one member for every way <see cref="Mail.Mutations.Destinations.MailboxDestinationOutcome" /> reports a
    /// destination that resolved to nothing — no mapping answers to the name, no run has bound it, the server advertises
    /// no such folder, or several folders carry the role. Those are four remedies for an operator and one answer here:
    /// the folder is not there, so the message waits. Which of the four it was is reported where the resolution happened,
    /// and startup validation already refuses the configuration that produces the first of them, naming the account.
    /// </para>
    /// <para>
    /// Nothing at all is written down in this case, including a <c>\Seen</c> change asked for beside the filing. Marking
    /// a message read while leaving it where it is takes the unread marker off spam that is still in the inbox, which is
    /// worse than waiting: the two switches together describe one intent, and a later attempt performs it whole once the
    /// folder is there.
    /// </para>
    /// </remarks>
    DestinationUnresolved = 5,

    /// <summary>Filing this message was asked for once already, and it is not in the destination.</summary>
    /// <remarks>
    /// <para>
    /// It is the one outcome that outranks both switches, and it covers two readings that are the same decision: somebody
    /// moved the message back out, which is the correction a false positive is meant to have, or the change is written
    /// down and the account's convergence pass has not carried it out yet. Asking again would either argue with the
    /// person whose mailbox it is or duplicate a change already in flight.
    /// </para>
    /// <para>
    /// The two are deliberately not told apart. Doing so would mean reading how far the earlier record got and treating
    /// an abandoned one as licence to file afresh — which is precisely the message a person is most likely to have moved
    /// by hand while it sat there.
    /// </para>
    /// </remarks>
    PreviouslyFiled = 6,

    /// <summary>Everything the switches ask for is already true of the message.</summary>
    NothingToChange = 7,

    /// <summary>A reload has withdrawn the account, so what it keeps of mail leaving the mirror is unknown.</summary>
    /// <remarks>
    /// Reachable only for an unmirrored destination, which is the one case a filing has to carry that answer. Like an
    /// unresolved destination it withholds both changes, for the same reason: the intent is one, and it is performed
    /// whole once the account is declared again.
    /// </remarks>
    AccountNoLongerConfigured = 8,

    /// <summary>Every change was worked out and none was written down, because the caller asked for a dry run.</summary>
    /// <remarks>
    /// It is reached at exactly the point <see cref="Requested" /> is: the switches, the destination, and every reason to
    /// leave the message alone have all been read by then, so a dry run reports the decision the acting run would have
    /// taken rather than a guess at it. What is absent is the pair of record identifiers, because no record was opened.
    /// </remarks>
    WouldRequest = 9,
}

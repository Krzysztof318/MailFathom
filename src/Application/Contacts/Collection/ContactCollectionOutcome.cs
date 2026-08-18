// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Contacts.Collection;

/// <summary>What collection concluded about one address a message carried.</summary>
/// <remarks>
/// This is the whole of what collection reports, because it is the whole of what may be reported: every value is a
/// decision about a person rather than the person, so an operator can read how a book is filling without any address
/// reaching an instrument. Which of the refusals is rising is what they act on — a rising exclusion says the owner's
/// list is doing its work, a rising bound says the run's ceiling is pacing an initial synchronization, and a rising
/// count of addresses below the threshold says the mailbox holds more one-time senders than correspondents.
/// </remarks>
public enum ContactCollectionOutcome
{
    /// <summary>The person behind the address was recorded into the collected origin.</summary>
    Recorded = 0,

    /// <summary>The book already holds that address, so collection left it alone.</summary>
    AlreadyHeld = 1,

    /// <summary>The address has not written often enough for the person behind it to be a correspondent yet.</summary>
    BelowThreshold = 2,

    /// <summary>The account's policy refuses that address: it is automated, excluded, or the deployment's own.</summary>
    Excluded = 3,

    /// <summary>The message said of itself that no person wrote it, so none of its addresses was considered.</summary>
    NotCorrespondence = 4,

    /// <summary>The run had already recorded as many contacts as it may, so the rest wait for the next one.</summary>
    RunBoundReached = 5,
}

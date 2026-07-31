// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

using Xunit.Sdk;

namespace MailMcp.IntegrationTests.Mailbox;

/// <summary>Reports a remote <c>\Seen</c> regression by naming the messages and the state observed.</summary>
/// <remarks>
/// The invariant this suite exists to prove is the one most likely to break silently during a later refactoring, and
/// the first thing whoever reads that failure needs is which message the server now considers read and what the
/// operation was that read it. A sequence comparison would report two collections of booleans and leave that to be
/// worked out from the test body, so the failure is built here instead.
/// </remarks>
internal static class RemoteSeenFlagAssertion
{
    /// <summary>Asserts that none of the observed messages carries the remote <c>\Seen</c> flag.</summary>
    /// <param name="observedEmails">What an independent connection reported after the operation ran.</param>
    /// <param name="operationDescription">The operation that must not have set the flag, named as it appears in the failure.</param>
    /// <exception cref="XunitException">Thrown when the server holds the flag for at least one of the messages.</exception>
    internal static void AssertNoneIsSeen(
        IReadOnlyList<ObservedEmail> observedEmails,
        string operationDescription)
    {
        ArgumentNullException.ThrowIfNull(observedEmails);

        var seenEmails = observedEmails.Where(observed => observed.IsSeen).ToArray();
        if (seenEmails.Length == 0)
        {
            return;
        }

        var regressions = string.Join(
            ", ",
            seenEmails.Select(observed => $"UID {observed.Uid.Value} '{observed.Subject}' observed as \\Seen"));

        throw new XunitException(
            $"{operationDescription} set the remote \\Seen flag on {seenEmails.Length} of {observedEmails.Count} messages: {regressions}. "
            + "Read-only mailbox synchronization must leave every remote flag exactly as it found it.");
    }

    /// <summary>Finds the messages a test seeded, in the server's own UID order.</summary>
    /// <param name="observedEmails">Everything the folder currently holds.</param>
    /// <param name="subjects">The subjects the test delivered.</param>
    /// <returns>One entry per subject.</returns>
    /// <exception cref="XunitException">Thrown when the server does not hold exactly one message per subject.</exception>
    /// <remarks>
    /// The mailbox outlives a test, so a test recognizes its own mail by subject rather than by position. Finding the
    /// wrong number of them means the seeding, not the invariant, is what failed, and saying so here keeps that from
    /// being reported as a flag regression.
    /// </remarks>
    internal static IReadOnlyList<ObservedEmail> SeededBy(
        IReadOnlyList<ObservedEmail> observedEmails,
        IReadOnlyList<string> subjects)
    {
        ArgumentNullException.ThrowIfNull(observedEmails);
        ArgumentNullException.ThrowIfNull(subjects);

        var seededEmails = observedEmails
            .Where(observed => subjects.Contains(observed.Subject, StringComparer.Ordinal))
            .OrderBy(observed => observed.Uid.Value)
            .ToArray();

        return seededEmails.Length == subjects.Count
            ? seededEmails
            : throw new XunitException(
                $"The mailbox holds {seededEmails.Length} of the {subjects.Count} messages this test delivered, "
                + "so the arrangement did not complete and nothing can be concluded about the flag state.");
    }
}

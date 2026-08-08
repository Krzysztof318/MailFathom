// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;
using MailFathom.Host.Configuration.Answering;

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Checks that the relevance filter is not declared to judge more candidates than a retrieval hands over.</summary>
/// <remarks>
/// The one rule spanning <c>Chat</c> and <c>MailAnswering</c>, so it lives with neither options type: each of them
/// validates what it can decide alone, and a rule about two sections is checked where both have been bound. It is the
/// same arrangement the alias uniqueness rule uses across <c>Chat</c> and <c>Embeddings</c>.
/// </remarks>
internal static class PassageRelevanceCandidateAgreement
{
    /// <summary>Reports the candidate count a declaration states that no retrieval could produce.</summary>
    /// <param name="chat">The bound chat declaration, or <see langword="null" /> where none was written.</param>
    /// <param name="answering">The bound answering declaration.</param>
    /// <returns>The count that cannot be met, or <see langword="null" /> where the two sections agree.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="answering" /> is <see langword="null" />.</exception>
    /// <remarks>
    /// A filter left off, a chat section nobody wrote, and a candidate count left absent all agree by construction: the
    /// last of the three means "every passage the retrieval hands over", which is the ceiling itself.
    /// </remarks>
    public static int? FindUnreachableCandidateCount(ChatModelOptions? chat, MailAnsweringOptions answering)
    {
        ArgumentNullException.ThrowIfNull(answering);

        if (chat?.IsConfigured is not true || !chat.RelevanceFilter.Enabled)
        {
            return null;
        }

        return chat.RelevanceFilter.MaxCandidates is { } declared
            && declared > answering.MaxPassagesPerRetrieval
            ? declared
            : null;
    }

    /// <summary>Describes the disagreement in the terms an operator has to act on.</summary>
    /// <param name="unreachableCandidateCount">The count the filter declared.</param>
    /// <param name="maximumPassagesPerRetrieval">The count a retrieval hands over.</param>
    /// <returns>The message the startup failure carries.</returns>
    public static string DescribeUnreachableCandidateCount(
        int unreachableCandidateCount,
        int maximumPassagesPerRetrieval) => string.Format(
        CultureInfo.InvariantCulture,
        "Chat:RelevanceFilter:MaxCandidates is {0}, above the {1} passages MailAnswering:MaxPassagesPerRetrieval lets one lookup hand over. There is never a candidate past the last passage, so the higher count states a filter that could not be reached. Lower it, or raise MailAnswering:MaxPassagesPerRetrieval.",
        unreachableCandidateCount,
        maximumPassagesPerRetrieval);
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Emails.Search;
using MailFathom.Application.Retrieval;
using MailFathom.Evaluations.AgentConversations;
using MailFathom.Evaluations.Corpus;

namespace MailFathom.Evaluations.RelevanceFilter;

/// <summary>One passage of the corpus, and whether it answers the lookup it is labelled against.</summary>
/// <param name="MessagePosition">Where the message falls in delivery order, from zero.</param>
/// <param name="Evidence">A phrase the labelled passage carries, which is what picks it out of its message.</param>
/// <param name="Answers">Whether the passage answers the lookup.</param>
internal sealed record LabelledCandidate(int MessagePosition, string Evidence, bool Answers)
{
    /// <summary>Reads the passage out of the corpus, as retrieval would hand it to the filter.</summary>
    /// <returns>The first passage of the message carrying the evidence, read into the passage a model receives.</returns>
    /// <exception cref="InvalidOperationException">Thrown, naming the phrase, when no passage of the message carries it within what one passage may carry.</exception>
    /// <remarks>
    /// The passage is the search's extract of the message, read through the deployment's own reading of a match, so it is
    /// bounded as a deployment bounds it. Nothing about the sender is established, which is what a judgement is shown for
    /// mail nobody verified.
    /// </remarks>
    public EmailKnowledgePassage Resolve()
    {
        var message = CorpusMessage.At(this.MessagePosition);

        var extract = message.Passages.FirstOrDefault(passage => passage.Text.Contains(this.Evidence, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"No passage of message {this.MessagePosition} carries \"{this.Evidence}\", so the label names nothing.");

        var passage = MailboxKnowledgeSearch.PassagesOf(
                [new EmailSearchMatch(CorpusReaders.SummaryOf(message, thread: null), RelevanceRank: 1, [extract.Text])],
                EmailKnowledgeBounds.Default)
            .Single();

        return passage.Text.Contains(this.Evidence, StringComparison.Ordinal)
            ? passage
            : throw new InvalidOperationException(
                $"Message {this.MessagePosition} carries \"{this.Evidence}\" past what one passage may carry, so the label names nothing.");
    }
}

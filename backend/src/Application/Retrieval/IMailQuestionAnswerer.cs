// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Application.Retrieval.AskMail;

namespace MailFathom.Application.Retrieval;

/// <summary>Answers one question about the mailbox from the mail it retrieves while answering.</summary>
/// <remarks>
/// <para>
/// The whole of the answering capability as the rest of the system sees it: a question, a scope, and the run's own
/// record in, an answer out. What sits behind it is an agent composed over a chat model and this deployment's retrieval,
/// and none of that shape appears here — the orchestration framework, its messages, its tools, and its context providers
/// stay inside the adapter, so replacing it changes nothing above this line.
/// </para>
/// <para>
/// One call is one run and keeps nothing. There is no conversation across calls, because a stored conversation is stored
/// mail content by another name and would carry one caller's retrieved passages into another caller's context.
/// </para>
/// <para>
/// The run reads and never writes. It reaches no mail server, sends no mail, and changes no remote flag, which is a
/// property of what it is composed of rather than a rule it observes.
/// </para>
/// </remarks>
public interface IMailQuestionAnswerer
{
    /// <summary>Answers one question, retrieving mail from within its scope as the model asks for it.</summary>
    /// <param name="question">What was asked, and the mail the answer may be drawn from.</param>
    /// <param name="observation">The run's own record, which the run fills in as it proceeds and the caller reads back however the run ended.</param>
    /// <param name="cancellationToken">Cancels the run and every provider call remaining in it.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="question" /> or <paramref name="observation" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the conversation the question composes into is larger than one call may send.</exception>
    /// <exception cref="ChatGenerationFailedException">Thrown when the run produced no answer, naming which kind of failure ended it.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancelled, which is never reported as a provider failure.</exception>
    /// <remarks>
    /// The observation is written to whether or not this call returns, which is the whole reason it is a parameter rather
    /// than part of the answer: what a failed run retrieved before it failed is exactly what a record of it has to say,
    /// and a run that threw has no answer to carry it out on.
    /// </remarks>
    Task<MailAnswer> AnswerAsync(
        MailQuestion question,
        MailAnsweringRunObservation observation,
        CancellationToken cancellationToken);
}

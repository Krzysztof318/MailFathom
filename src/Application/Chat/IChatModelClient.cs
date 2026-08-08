// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>Produces one model answer for a conversation.</summary>
/// <remarks>
/// <para>
/// The second kind of outbound AI call this system makes, and deliberately separate from the first. Embeddings and
/// generation are configured independently because the states they produce are different: without an embedding
/// provider semantic search is off and lexical search continues, while without a chat provider search is unaffected and
/// only the answering capability stops being offered. One flag for both would be wrong in both directions.
/// </para>
/// <para>
/// The port carries text and nothing above it. It composes no prompt, retrieves nothing, offers the model no tools, and
/// keeps no conversation: what to say is decided by whoever calls, and this is what says it. The model, the generation
/// parameters, the bounds, and the timeout are the deployment's and come from validated configuration, which is why
/// none of them appears in this signature.
/// </para>
/// <para>
/// It is not streaming. A caller that presents an answer as it arrives needs a second method with its own cancellation
/// and its own partial-answer semantics, and no caller needs one yet.
/// </para>
/// </remarks>
public interface IChatModelClient
{
    /// <summary>Sends a conversation and returns what the model answered.</summary>
    /// <param name="conversation">The turns to send, oldest first.</param>
    /// <param name="cancellationToken">Cancels the call and every remaining attempt.</param>
    /// <returns>The answer, whose text is never empty.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="conversation" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the conversation is empty, holds a blank turn, or is larger than one call sends.</exception>
    /// <exception cref="ChatGenerationFailedException">Thrown when the call produced no answer, naming which kind of failure ended it.</exception>
    /// <exception cref="OperationCanceledException">Thrown when the caller cancelled, which is never reported as a provider failure.</exception>
    Task<ChatAnswer> AnswerAsync(IReadOnlyList<ChatMessage> conversation, CancellationToken cancellationToken);
}

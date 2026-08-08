// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Chat;

/// <summary>Names why a chat request produced no answer, at the granularity a caller acts on.</summary>
/// <remarks>
/// <para>
/// The set exists for the reason its embedding counterpart does: "the provider failed" hides the one distinction that
/// decides what happens next. Three of these are worth another attempt and three are not, and repeating one of the
/// second group buys the same answer while the account carries the request.
/// </para>
/// <para>
/// A generation the provider began and then cut short is deliberately absent. A truncated answer and one a content
/// filter stopped are answers rather than failures, and they are reported by
/// <see cref="ChatGenerationStop" /> on the answer itself — which is also what guarantees neither is ever retried,
/// since nothing repeats a call that returned something.
/// </para>
/// <para>
/// A caller's own cancellation and a host shutdown are absent as well. Both arrive as
/// <see cref="OperationCanceledException" /> and neither is a statement about the provider, so folding them in here
/// would put a decision this system made among the answers a remote party gave.
/// </para>
/// </remarks>
public enum ChatGenerationFailure
{
    /// <summary>The provider refused the credential the deployment presented.</summary>
    /// <remarks>Terminal. Every repetition receives the same answer while counting against the account's request budget.</remarks>
    CredentialRejected = 0,

    /// <summary>The provider refused the request because the deployment is over its allowed rate.</summary>
    /// <remarks>Worth repeating, but only after a backoff. It is separate from a transport fault because the provider answered rather than failed to answer, and answering again immediately is what turns a throttle into a longer one.</remarks>
    RateLimited = 1,

    /// <summary>The request outlived the time the deployment allows one chat call.</summary>
    RequestTimedOut = 2,

    /// <summary>The request never reached an answer: the endpoint was unreachable, the connection dropped, or the response was unreadable.</summary>
    TransportFaulted = 3,

    /// <summary>The provider rejected the request itself, for a reason repeating it cannot change.</summary>
    /// <remarks>
    /// A model the deployment does not serve, a conversation beyond the model's context window, a parameter the model
    /// does not accept — and a prompt the provider's own safety system refused before generating anything. The last of
    /// those is not named apart from the others because telling it apart would mean reading the provider's error body,
    /// and that body quotes the request, which is mail text.
    /// </remarks>
    RequestRefused = 4,

    /// <summary>The provider ended the call without producing any text.</summary>
    /// <remarks>Terminal, and the one classification that names a declaration or a conversation to correct rather than a remote condition to wait out.</remarks>
    AnswerEmpty = 5,
}

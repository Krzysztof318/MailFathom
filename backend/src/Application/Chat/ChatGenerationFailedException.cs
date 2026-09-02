// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Failures;

namespace MailFathom.Application.Chat;

/// <summary>Indicates that a chat request produced no answer, and says which kind of failure ended it.</summary>
/// <remarks>
/// <para>
/// An exception rather than a result type, for the reason its embedding counterpart is one: the fact travels through
/// code that cannot decide what it means. The resilience pipeline between the adapter and its caller reads it to choose
/// whether to make another attempt, and a result would have to be unwrapped and rethrown there anyway.
/// </para>
/// <para>
/// The message names the endpoint by the alias the deployment gave it in configuration and never by its address, and it
/// carries nothing of the conversation that failed. Both follow from what <see cref="MailFathomException" /> permits a
/// message to say: an endpoint address identifies a tenant, and a prompt is whatever the person asking typed.
/// </para>
/// </remarks>
public sealed class ChatGenerationFailedException : MailFathomException
{
    /// <summary>Initializes a failure naming the endpoint alias, what kind of failure it was, and the failure that revealed it.</summary>
    /// <param name="endpointAlias">The deployment's own configured name for the endpoint that failed.</param>
    /// <param name="failure">What kind of failure ended the request.</param>
    /// <param name="cause">The failure this one was raised for.</param>
    public ChatGenerationFailedException(string endpointAlias, ChatGenerationFailure failure, Exception cause)
        : base(DescribeFailure(endpointAlias, failure), cause) =>
        this.Failure = failure;

    /// <summary>Initializes a failure naming the endpoint alias and what kind of failure it was.</summary>
    /// <param name="endpointAlias">The deployment's own configured name for the endpoint that failed.</param>
    /// <param name="failure">What kind of failure ended the request.</param>
    public ChatGenerationFailedException(string endpointAlias, ChatGenerationFailure failure)
        : base(DescribeFailure(endpointAlias, failure)) =>
        this.Failure = failure;

    /// <inheritdoc />
    /// <remarks>
    /// Three codes cover six classifications, because a code names what an operator does about a failure rather than
    /// how it arrived: a refused credential is rotated, an empty answer is a declaration or a conversation to correct,
    /// and everything else is a call to make again later. <see cref="Failure" /> keeps the finer distinction for the
    /// pipeline that has to decide about repeating.
    /// </remarks>
    public override MailFathomErrorCode ErrorCode => this.Failure switch
    {
        ChatGenerationFailure.CredentialRejected => MailFathomErrorCode.ChatProviderCredentialRejected,
        ChatGenerationFailure.AnswerEmpty => MailFathomErrorCode.ChatAnswerEmpty,
        _ => MailFathomErrorCode.ChatProviderUnavailable,
    };

    /// <summary>Gets what kind of failure ended the request.</summary>
    public ChatGenerationFailure Failure { get; }

    /// <summary>Gets whether repeating the request could succeed without anything else changing first.</summary>
    /// <remarks>
    /// Declared here rather than left to each caller, so the resilience pipeline and any supervisor deciding whether to
    /// keep asking give the same answer. A refused credential, a rejected request, and an empty answer each need
    /// somebody to change something before the next attempt can differ from this one.
    /// </remarks>
    public bool IsWorthRepeating => this.Failure
        is ChatGenerationFailure.RateLimited
        or ChatGenerationFailure.RequestTimedOut
        or ChatGenerationFailure.TransportFaulted;

    private static string DescribeFailure(string endpointAlias, ChatGenerationFailure failure) => failure switch
    {
        ChatGenerationFailure.CredentialRejected =>
            $"Chat endpoint '{endpointAlias}' refused the configured credential. Rotate or correct it; repeating the request cannot change the answer.",
        ChatGenerationFailure.RateLimited =>
            $"Chat endpoint '{endpointAlias}' refused the request because the deployment is over its allowed rate.",
        ChatGenerationFailure.RequestTimedOut =>
            $"Chat endpoint '{endpointAlias}' did not answer within the time configured for one chat request.",
        ChatGenerationFailure.TransportFaulted =>
            $"Chat endpoint '{endpointAlias}' could not be reached, or the answer it began was unreadable.",
        ChatGenerationFailure.RequestRefused =>
            $"Chat endpoint '{endpointAlias}' rejected the request itself. Check the declared model, the configured generation parameters, and the size of the conversation sent.",
        ChatGenerationFailure.AnswerEmpty =>
            $"Chat endpoint '{endpointAlias}' ended the call without producing any text.",
        _ => $"Chat endpoint '{endpointAlias}' did not produce an answer.",
    };
}

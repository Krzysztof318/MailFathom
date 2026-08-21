// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Chat;
using MailFathom.Domain.Failures;

namespace MailFathom.AI.ProviderAdapters;

/// <summary>Turns what a call to a chat endpoint failed with into the failure its caller is told about.</summary>
/// <remarks>
/// <para>
/// One mapping for both paths to a chat provider — the single bounded request and the decorator every turn of an agent
/// run passes through — because the two differ in what they wrap a call in and in nothing about what its failure means.
/// Written once each, they drift by an edit to one of them, and a classification meaning one thing in a single answer
/// and another inside a run is a difference nobody would think to look for.
/// </para>
/// <para>
/// It maps and never classifies. <see cref="ProviderCallFailureClassification" /> reads what the remote party did, and
/// its remarks refuse this role deliberately: each calling boundary publishes a failure enumeration of its own, so the
/// chat one is named here and the embedding one where it is used.
/// </para>
/// </remarks>
internal static class ChatCallFailureMapping
{
    /// <summary>Names the chat failure a classified provider call amounts to.</summary>
    /// <param name="failure">What the call established about the provider.</param>
    /// <returns>The failure a chat caller is told about.</returns>
    public static ChatGenerationFailure ToChatFailure(ProviderCallFailure failure) => failure switch
    {
        ProviderCallFailure.CredentialRejected => ChatGenerationFailure.CredentialRejected,
        ProviderCallFailure.RateLimited => ChatGenerationFailure.RateLimited,
        ProviderCallFailure.RequestTimedOut => ChatGenerationFailure.RequestTimedOut,
        ProviderCallFailure.RequestRefused => ChatGenerationFailure.RequestRefused,
        _ => ChatGenerationFailure.TransportFaulted,
    };

    /// <summary>Reports whether the resilience pipeline declined to call the endpoint at all.</summary>
    /// <param name="rejection">What the pipeline raised instead of calling.</param>
    /// <returns><see langword="true" /> when the endpoint was never reached.</returns>
    /// <remarks>
    /// <para>
    /// Its circuit is open, or its concurrency budget is spent. Recognized by code rather than by type, which is what a
    /// stable error code is for: the resilience library and the exception it raises belong to another adapter boundary
    /// that this one may not reference.
    /// </para>
    /// <para>
    /// Guarded by nothing, deliberately. Its callers are exception filters, where the runtime turns anything raised into
    /// a filter that did not match, so a guard here would refuse an argument by silently letting the failure travel on
    /// unclassified instead of reporting it. The one thing a filter is handed is the exception it caught.
    /// </para>
    /// </remarks>
    public static bool IsEndpointNotCalled(MailFathomException rejection) =>
        rejection.ErrorCode == MailFathomErrorCode.OutboundDependencyUnavailable;

    /// <summary>Names the failure a call the pipeline never made is reported as.</summary>
    /// <param name="rejection">What the pipeline raised instead of calling, carried on as the cause.</param>
    /// <param name="endpointAlias">The deployment's own name for the endpoint that was not reached.</param>
    /// <returns>The failure to raise in its place.</returns>
    /// <remarks>
    /// A transport fault rather than a refusal of its own kind, because that is what the caller has to act on: nothing
    /// about the request was wrong and the endpoint is worth calling again once the budget or the circuit allows it.
    /// </remarks>
    public static ChatGenerationFailedException ToEndpointNotCalledFailure(
        MailFathomException rejection,
        string endpointAlias) =>
        new(endpointAlias, ChatGenerationFailure.TransportFaulted, rejection);
}

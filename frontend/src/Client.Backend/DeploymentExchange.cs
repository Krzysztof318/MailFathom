// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace MailFathom.Client.Backend;

/// <summary>One request over the wire, with everything that can go wrong stated in the four terms a screen acts on.</summary>
/// <remarks>
/// <para>
/// Every reader in this assembly goes through here, so the four cases are decided once. Written separately from each
/// caller because the distinctions are easy to get subtly wrong and expensive when they are: a client-side timeout and
/// a caller cancelling both arrive as <see cref="TaskCanceledException" /> and mean opposite things, and a refused
/// credential arrives as a status code rather than as an exception at all.
/// </para>
/// <para>
/// Nothing a deployment or an authorization server answered is put into a failure message. The body is text from a
/// machine this process does not own, and everything a MailFathom deployment returns about mail is personal data under
/// the root instructions — so a screen that showed either would be leaking one or repeating the other.
/// </para>
/// </remarks>
internal static class DeploymentExchange
{
    /// <summary>The largest body this assembly will read.</summary>
    /// <remarks>
    /// Every document read here is small and bounded by its own specification — a session's grant, an RFC 9728
    /// document, a discovery document, a token response — so a megabyte is generous for all four and still a ceiling.
    /// The root instructions ask for an explicit limit at every remote boundary, and the reason bites hardest on this
    /// side of one: an authorization server is a machine somebody else runs, and a client that read whatever it was
    /// sent would let a compromised or merely broken one exhaust a browser tab's memory.
    /// </remarks>
    internal const int MaxDocumentBytes = 1024 * 1024;

    /// <summary>Sends one request and reads the document it answers with.</summary>
    /// <typeparam name="TDocument">The contract the body is read against.</typeparam>
    /// <param name="transport">The client the request is sent on.</param>
    /// <param name="request">The request, which this method disposes.</param>
    /// <param name="contract">The source-generated reader for the body.</param>
    /// <param name="cancellationToken">Cancels the request, which is not the same thing as the request timing out.</param>
    /// <returns>The document the answer carried.</returns>
    /// <exception cref="DeploymentFailure">Thrown for every way the exchange can fail to produce one.</exception>
    internal static async Task<TDocument> ReadAsync<TDocument>(
        HttpClient transport,
        HttpRequestMessage request,
        JsonTypeInfo<TDocument> contract,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        using (request)
        {
            using var response = await SendAsync(transport, request, cancellationToken).ConfigureAwait(false);

            RefuseUnusableStatus(response);

            return await ReadBodyAsync(response, contract, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Sends one request, turning a transport failure into a stated reason.</summary>
    /// <param name="transport">The client the request is sent on.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <returns>The answer, whatever its status.</returns>
    /// <exception cref="DeploymentFailure">Thrown when nothing answered.</exception>
    internal static async Task<HttpResponseMessage> SendAsync(
        HttpClient transport,
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await transport.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            // The caller did not cancel, so this is the client's own timeout elapsing. Distinguished because a screen
            // retries a timeout unchanged and does nothing at all about a cancellation it asked for.
            throw new DeploymentFailure(
                DeploymentFailureReason.TimedOut,
                "MailFathom did not answer in time.",
                failure);
        }
        catch (HttpRequestException failure)
            when (failure.HttpRequestError == HttpRequestError.ConfigurationLimitExceeded)
        {
            // An answer that declared no length and then ran past MaxResponseContentBufferSize while it was being
            // buffered. Nothing is wrong with the connection, so it is the same outcome ReadBodyAsync reports for the
            // answer that declared its size up front: something answered, and what it answered is not usable.
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom answered with more than this client will read, so the answer was not used.",
                failure);
        }
        catch (HttpRequestException failure)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unreachable,
                "MailFathom could not be reached. Check that the address is right and that this device is online.",
                failure);
        }
    }

    /// <summary>Reads a body against its contract, or says the answer was not one.</summary>
    /// <typeparam name="TDocument">The contract the body is read against.</typeparam>
    /// <param name="response">The answer to read.</param>
    /// <param name="contract">The source-generated reader.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document.</returns>
    /// <exception cref="DeploymentFailure">Thrown when the body is not the document it should be.</exception>
    /// <remarks>
    /// The overload taking a <see cref="JsonTypeInfo{T}" /> rather than one of the reflection-based ones, which
    /// <c>.config/BannedSymbols.txt</c> refuses outright: the browser head publishes trimmed, and a reflection-based
    /// reader is removed by the trimmer rather than reported.
    /// </remarks>
    internal static async Task<TDocument> ReadBodyAsync<TDocument>(
        HttpResponseMessage response,
        JsonTypeInfo<TDocument> contract,
        CancellationToken cancellationToken)
        where TDocument : class
    {
        // Refused on the declared length before a byte is buffered. The transport's own
        // MaxResponseContentBufferSize is the backstop for an answer that declares none, and it is set where the
        // clients are registered; this is the half that can say what happened.
        if (response.Content.Headers.ContentLength is > MaxDocumentBytes)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom answered with more than this client will read, so the answer was not used.");
        }

        try
        {
            return await response.Content.ReadFromJsonAsync(contract, cancellationToken).ConfigureAwait(false)
                ?? throw new DeploymentFailure(
                    DeploymentFailureReason.Unusable,
                    "MailFathom answered with an empty body.");
        }
        catch (Exception failure) when (failure is JsonException or NotSupportedException or InvalidOperationException)
        {
            // A mistyped address, a captive portal, or a proxy answers with a login page rather than with this
            // document. The body itself is never read back: it is text from a machine that is not the one intended.
            throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "The address answered, but not the way MailFathom does. Check that it is a MailFathom deployment.",
                failure);
        }
        catch (TaskCanceledException failure) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DeploymentFailure(
                DeploymentFailureReason.TimedOut,
                "MailFathom did not answer in time.",
                failure);
        }
    }

    /// <summary>Refuses an answer whose status says the request will not be served.</summary>
    /// <param name="response">The answer to judge.</param>
    /// <exception cref="DeploymentFailure">Thrown when the status is not a success.</exception>
    /// <remarks>
    /// <c>401</c> and <c>403</c> are separated from the rest because they are the one case the person can act on: the
    /// sign-in has ended, or the credential was never granted this. Everything else is a deployment that is not
    /// answering the way its contract says, which is somebody's defect rather than a person's next step.
    /// </remarks>
    internal static void RefuseUnusableStatus(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
            ? new DeploymentFailure(
                DeploymentFailureReason.CredentialRefused,
                "MailFathom did not accept this sign-in. Sign in again.")
            : new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "MailFathom answered in a way this version does not understand.");
    }
}

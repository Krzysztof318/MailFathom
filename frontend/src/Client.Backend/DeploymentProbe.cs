// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Net;

namespace MailFathom.Client.Backend;

/// <summary>What a candidate address turned out to be.</summary>
/// <param name="Session">What the deployment reported about an unauthenticated caller, or <see langword="null" /> where its client surface refused one.</param>
/// <remarks>
/// Two shapes rather than one, because a deployment that requires signing in is the ordinary case and it cannot report
/// anything to a caller holding no credential. Both mean the address is worth keeping; they differ in how much was
/// proved, which is what <see cref="IsGuarded" /> says.
/// </remarks>
public sealed record DeploymentReach(DeploymentSession? Session)
{
    /// <summary>Gets whether the address answered by refusing an unauthenticated caller rather than by describing itself.</summary>
    public bool IsGuarded => this.Session is null;
}

/// <summary>Asks an address whether a MailFathom deployment is there, before anything is pointed at it.</summary>
/// <remarks>
/// <para>
/// The half of judging an address that only the network can answer. <see cref="DeploymentAddressRule" /> decides
/// whether an address is one this client may carry a credential to; this decides whether anything is there, so a
/// mistyped host arrives as "nothing answered" while somebody is still typing rather than as an authentication failure
/// after they have entered a password.
/// </para>
/// <para>
/// It presents no credential. The transport it uses carries no token handler, deliberately: a candidate address is a
/// machine nobody has vouched for yet, and the point of asking is to find out what it is — sending this run's
/// credential to it first would hand the session to whatever answered.
/// </para>
/// <para>
/// Reaching it is not enough to be believed. Anything can answer <c>200</c> on a port, so an answer has to be
/// MailFathom's own session document naming MailFathom; a captive portal, a proxy, or somebody else's service arrives
/// as <see cref="DeploymentFailureReason.Unusable" /> rather than as a deployment.
/// </para>
/// <para>
/// <strong>A refusal is an answer too.</strong> The client surface carries whatever authentication its deployment
/// configured, and a deployment configured the way MailFathom's own documentation asks refuses an unauthenticated
/// caller outright — so <c>401</c> and <c>403</c> are what a correctly configured deployment says here, and treating
/// them as "not MailFathom" would refuse every address that is one. What such an answer proves is weaker and this says
/// so rather than pretending otherwise: something is at that address and it is guarding a MailFathom client surface.
/// Whether it is MailFathom is settled by signing in, which is the next thing a person does.
/// </para>
/// </remarks>
public sealed class DeploymentProbe
{
    /// <summary>What a MailFathom deployment names itself in the session document.</summary>
    /// <remarks>
    /// This end of the same agreement <c>backend/src/Host/Api/ClientApiEndpoints.cs</c> states when it composes the
    /// response. Written here as a literal for the reason <see cref="DeploymentRoutes" /> gives about the paths beside
    /// it: two ends stating one contract, rather than a constant shared across the two stacks.
    /// </remarks>
    internal const string ServiceName = "MailFathom";

    private readonly IHttpClientFactory transports;

    /// <summary>Initializes the probe over the transports this assembly registered.</summary>
    /// <param name="transports">Supplies the credential-free transport a candidate is asked on.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="transports" /> is <see langword="null" />.</exception>
    public DeploymentProbe(IHttpClientFactory transports)
    {
        ArgumentNullException.ThrowIfNull(transports);

        this.transports = transports;
    }

    /// <summary>Asks a candidate address what is there.</summary>
    /// <param name="candidate">The address a person typed, or one an installation stated.</param>
    /// <param name="cancellationToken">Abandons the question, which is not the same thing as it timing out.</param>
    /// <returns>What answered, and how much it proved.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="candidate" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">Thrown when the candidate is not an address this client may be pointed at.</exception>
    /// <exception cref="DeploymentFailure">Thrown when nothing answered, nothing answered in time, or what answered is not a MailFathom deployment.</exception>
    public async Task<DeploymentReach> ReachAsync(Uri candidate, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var refusal = DeploymentAddressRule.Judge(candidate);

        if (refusal != DeploymentAddressRefusal.None)
        {
            throw new ArgumentException(
                $"{DeploymentAddressRule.Describe(candidate)} is not an address this client may be pointed at "
                + $"({refusal}).",
                nameof(candidate));
        }

        var transport = this.transports.CreateClient(DeploymentHttpClients.DeploymentProbe);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(candidate, DeploymentRoutes.SessionPath));

        using var response = await DeploymentExchange
            .SendAsync(transport, request, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new DeploymentReach(null);
        }

        DeploymentExchange.RefuseUnusableStatus(response);

        var answer = await DeploymentExchange
            .ReadBodyAsync(response, DeploymentJsonContext.Default.DeploymentSession, cancellationToken)
            .ConfigureAwait(false);

        return string.Equals(answer.Service, ServiceName, StringComparison.Ordinal)
            ? new DeploymentReach(answer)
            : throw new DeploymentFailure(
                DeploymentFailureReason.Unusable,
                "Something answered at that address, but it is not a MailFathom deployment.");
    }
}

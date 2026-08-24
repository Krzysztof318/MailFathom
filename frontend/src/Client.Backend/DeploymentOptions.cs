// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Client.Backend;

/// <summary>What an installation states about reaching a deployment, other than which one it reaches.</summary>
/// <remarks>
/// <para>
/// Every value here comes from whoever composes the application, and none of them names a deployment. Where the
/// deployment is is a person's decision rather than an installation's, so it is held by <see cref="DeploymentAddress" />
/// and can change while the client runs; what is left here is what stays true whichever deployment is reached.
/// </para>
/// <para>
/// The client identifier is public information, unlike a client secret, which this application holds none of and could
/// hold none of — a desktop binary and a WebAssembly bundle are both readable by whoever runs them, which is exactly
/// the situation RFC 7636 defines a public client for. That is why every grant here is bound by a proof key.
/// </para>
/// </remarks>
public sealed record DeploymentOptions
{
    /// <summary>The request timeout applied when the composing host states none.</summary>
    /// <remarks>
    /// Long enough for a mailbox query against a deployment on a slow link, short enough that a screen waiting on it
    /// reports a failure rather than appearing to hang. A host with a slower deployment states its own.
    /// </remarks>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Initializes the options a deployment is reached under.</summary>
    /// <param name="clientId">The client identifier registered with the deployment's authorization server.</param>
    /// <param name="timeout">How long a single request may take, or <see langword="null" /> for <see cref="DefaultTimeout" />.</param>
    /// <exception cref="ArgumentException">Thrown when the client identifier is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the timeout is not positive.</exception>
    public DeploymentOptions(string clientId, TimeSpan? timeout = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);

        var resolvedTimeout = timeout ?? DefaultTimeout;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(resolvedTimeout, TimeSpan.Zero, nameof(timeout));

        this.ClientId = clientId;
        this.Timeout = resolvedTimeout;
    }

    /// <summary>Gets the client identifier presented to the authorization server.</summary>
    public string ClientId { get; }

    /// <summary>Gets how long a single request to the deployment may take.</summary>
    public TimeSpan Timeout { get; }
}

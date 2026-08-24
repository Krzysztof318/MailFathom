// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.Session;

/// <summary>Holds what the deployment reports about this client, and asks again when that answer stops describing it.</summary>
/// <remarks>
/// <para>
/// A state rather than a feed, because it is shared: a feed is read from the start by whoever subscribes, so five
/// screens reading one session would be five requests for one answer. The state is fetched on the first read and
/// replayed to everything after it, which is what makes "the session the whole application reads" one fetch.
/// </para>
/// <para>
/// It listens rather than being told. Two things end the answer's usefulness and neither is a screen's to remember:
/// the signed-in identity changing, because the deployment answers about the credential presented to it, and the
/// client being pointed at another deployment, because the answer described the one it was pointed at before. Both
/// are announced where they happen.
/// </para>
/// <para>
/// Nothing here is logged, stored, or exported. The answer carries a version and a grant and names no credential at
/// all, and the client keeps it in memory for the run exactly as it keeps the token that fetched it.
/// </para>
/// </remarks>
internal sealed class DeploymentClientSession : IClientSession, IDisposable
{
    private readonly DeploymentClient deployment;
    private readonly AccessTokenStore tokens;
    private readonly DeploymentAddress address;
    private readonly Signal refresh = new();

    /// <summary>Initializes the session over the client that fetches it and the two things that end its usefulness.</summary>
    /// <param name="deployment">Where the session document is asked for.</param>
    /// <param name="tokens">The credential this run signs in with, which announces when the identity changes.</param>
    /// <param name="address">Which deployment the client reaches, which announces when it is pointed at another.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeploymentClientSession(
        DeploymentClient deployment,
        AccessTokenStore tokens,
        DeploymentAddress address)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(address);

        this.deployment = deployment;
        this.tokens = tokens;
        this.address = address;

        this.tokens.SignedInChanged += this.AskAgain;
        this.address.Moved += this.AskAgain;

        this.Standing = State.Async(this, this.ReadStandingAsync, this.refresh);
    }

    /// <inheritdoc />
    public IFeed<SessionStanding> Standing { get; }

    /// <inheritdoc />
    public void Refresh() => this.refresh.Raise();

    /// <inheritdoc />
    public void Dispose()
    {
        this.tokens.SignedInChanged -= this.AskAgain;
        this.address.Moved -= this.AskAgain;
        this.refresh.Dispose();
    }

    private async ValueTask<SessionStanding> ReadStandingAsync(CancellationToken cancellationToken) =>
        SessionStanding.Of(
            await this.deployment.ReadSessionAsync(cancellationToken).ConfigureAwait(false));

    private void AskAgain(object? sender, EventArgs e) => this.refresh.Raise();
}

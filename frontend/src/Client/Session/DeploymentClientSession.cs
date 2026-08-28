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
/// The same fetch is where reaching the deployment is decided, which is why the bounded retry lives here rather than
/// in a watcher of its own. One exchange answers both questions — a deployment that answered was reached, whatever it
/// answered — so a second thing asking would be a second request per screen and a second opinion to disagree with this
/// one. A transport failure is retried on <see cref="DeploymentConnectionRetry" />'s curve and the attempt under way is
/// published on <see cref="Connection" /> as it goes, so a client whose connection dropped recovers by itself and says it
/// is trying instead of appearing frozen. A deployment that answered a refusal is not retried: nothing about asking
/// again would change it, and the answer belongs to the person rather than to a timer. Whatever ends the fetch ends
/// the standing with it, cancellation aside, because the frame reads that standing to decide which of its notices
/// speaks — one left mid-attempt would close every one of them and leave a screen that failed in silence.
/// </para>
/// <para>
/// A credential the deployment refuses is ended here rather than retried or reported as a screen that cannot load.
/// This is the one place a deployment's verdict on the running session is read, so it is the one place that can tell a
/// credential the deployment has stopped accepting from a request this caller was never granted — and the answer to
/// the first is to forget it, which clears what the head kept and puts the person in front of the sign-in through the
/// change that announces it.
/// </para>
/// <para>
/// Nothing here is logged, stored, or exported. The answer carries a version and a grant and names no credential at
/// all, and the client keeps it in memory for the run exactly as it keeps the credential that fetched it.
/// </para>
/// </remarks>
internal sealed class DeploymentClientSession : IClientSession, IDisposable
{
    private readonly DeploymentClient deployment;
    private readonly SignedInOwner owner;
    private readonly DeploymentSignIn signIn;
    private readonly DeploymentAddress address;
    private readonly DeploymentConnectionRetry retry;
    private readonly TimeProvider clock;
    private readonly IState<DeploymentConnection> connection;
    private readonly Signal refresh = new();
    private long revision;

    /// <summary>Initializes the session over the client that fetches it and the things that end its usefulness.</summary>
    /// <param name="deployment">Where the session document is asked for.</param>
    /// <param name="owner">Who is signed in during this run, which announces when the identity changes.</param>
    /// <param name="signIn">How a session is ended, which is what a credential the deployment has stopped accepting leads to.</param>
    /// <param name="address">Which deployment the client reaches, which announces when it is pointed at another.</param>
    /// <param name="retry">How many times the client asks again by itself, and how long it waits between attempts.</param>
    /// <param name="clock">What the wait between attempts is measured against, so a test spends none of it.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public DeploymentClientSession(
        DeploymentClient deployment,
        SignedInOwner owner,
        DeploymentSignIn signIn,
        DeploymentAddress address,
        DeploymentConnectionRetry retry,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(retry);
        ArgumentNullException.ThrowIfNull(clock);

        this.deployment = deployment;
        this.owner = owner;
        this.signIn = signIn;
        this.address = address;
        this.retry = retry;
        this.clock = clock;

        this.owner.SignedInChanged += this.AskAgain;
        this.address.Moved += this.AskAgain;

        // Seeded at the first attempt rather than at a state of its own for "nobody has asked yet". The shell reads
        // the session while it is being built, so the fetch is under way from the moment anything can observe this,
        // and a fourth value would be one no screen is ever shown.
        this.connection = State.Value(this, () => new DeploymentConnection(ConnectionStanding.Reaching, 1, retry.Attempts));
        this.Standing = State.Async(this, this.ReadStandingAsync, this.refresh);
        this.Revision = State.Async(this, this.ReadRevisionAsync, this.refresh);
    }

    /// <inheritdoc />
    public IFeed<SessionStanding> Standing { get; }

    /// <inheritdoc />
    public IFeed<long> Revision { get; }

    /// <inheritdoc />
    public IFeed<DeploymentConnection> Connection => this.connection;

    /// <inheritdoc />
    public void Refresh()
    {
        Interlocked.Increment(ref this.revision);
        this.refresh.Raise();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.owner.SignedInChanged -= this.AskAgain;
        this.address.Moved -= this.AskAgain;
        this.refresh.Dispose();
    }

    /// <summary>Fetches the session, asking again on its own for as long as nothing is answering.</summary>
    /// <remarks>
    /// The loop publishes what it is doing before each attempt and again once it stops, so the frame can say which
    /// attempt is under way. Every failure still reaches the caller: what the retry decides is when to give up, not
    /// whether a screen is told.
    /// </remarks>
    private async ValueTask<SessionStanding> ReadStandingAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            await this.WaitBeforeAsync(attempt, cancellationToken).ConfigureAwait(false);

            try
            {
                var reported = await this.deployment.ReadSessionAsync(cancellationToken).ConfigureAwait(false);

                await this.PublishAsync(ConnectionStanding.Reached, attempt, cancellationToken).ConfigureAwait(false);

                return SessionStanding.Of(reported);
            }
            catch (DeploymentFailure failure)
            {
                var lastAttempt = attempt >= this.retry.Attempts;

                if (!Recoverable(failure) || lastAttempt)
                {
                    // A deployment that answered a refusal was reached, and saying otherwise would send somebody after
                    // their network for a credential their operator has to widen.
                    var standing = Recoverable(failure) ? ConnectionStanding.Lost : ConnectionStanding.Reached;

                    await this.PublishAsync(standing, attempt, cancellationToken).ConfigureAwait(false);
                    await this.EndRefusedSessionAsync(failure, cancellationToken).ConfigureAwait(false);

                    throw;
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // Anything that is not this client's own reading of a failed exchange says nothing about the network,
                // so nothing here may blame it. Publishing rather than leaving the standing mid-attempt is what keeps
                // the frame's notice about the failed session open: a standing still reading "reaching" would close
                // both notices at once and leave somebody with a screen that failed silently.
                await this.PublishAsync(ConnectionStanding.Reached, attempt, cancellationToken).ConfigureAwait(false);

                throw;
            }
        }
    }

    /// <summary>Reports whether asking again could produce a different answer.</summary>
    /// <remarks>
    /// The two transport outcomes and neither of the two answers. A credential the deployment refused is refused on
    /// every attempt until somebody signs in again, and an answer this version does not understand is a defect rather
    /// than a moment to wait out; retrying either would spend a person's time proving what the first attempt said.
    /// </remarks>
    private static bool Recoverable(DeploymentFailure failure) =>
        failure.Reason is DeploymentFailureReason.Unreachable or DeploymentFailureReason.TimedOut;

    /// <summary>Ends the session where the deployment refused the credential it was holding.</summary>
    /// <remarks>
    /// <para>
    /// The credential is a copy of something the deployment owns, so the deployment's answer wins: a password an
    /// administrator has rotated, or a credential they have disabled, is refused on every request from here on, and
    /// leaving it in place would put somebody in front of a screen that fails again each time they touch it while a
    /// password nothing will ever accept sat in their keyring.
    /// </para>
    /// <para>
    /// Only where somebody was signed in. The same refusal reaches a caller who never signed in at all — that is what a
    /// guarded deployment answers — and forgetting nothing announces nothing, which is what keeps this from asking the
    /// session for itself again in a loop.
    /// </para>
    /// </remarks>
    private ValueTask EndRefusedSessionAsync(DeploymentFailure failure, CancellationToken cancellationToken) =>
        failure.Reason == DeploymentFailureReason.CredentialRefused && this.owner.IsSignedIn
            ? this.signIn.SignOutAsync(cancellationToken)
            : ValueTask.CompletedTask;

    private async ValueTask WaitBeforeAsync(int attempt, CancellationToken cancellationToken)
    {
        if (attempt > 1)
        {
            await this.PublishAsync(ConnectionStanding.Reaching, attempt, cancellationToken).ConfigureAwait(false);
            await Task.Delay(this.retry.WaitBefore(attempt), this.clock, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Publishes where the connection stands, replacing whatever the last attempt left there.</summary>
    /// <remarks>An update rather than a set, because the two <c>SetAsync</c> overloads are written for a value type and a string and neither takes a record.</remarks>
    private ValueTask PublishAsync(ConnectionStanding standing, int attempt, CancellationToken cancellationToken) =>
        this.connection.UpdateAsync(
            _ => new DeploymentConnection(standing, attempt, this.retry.Attempts),
            cancellationToken);

    private ValueTask<long> ReadRevisionAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(Interlocked.Read(ref this.revision));

    private void AskAgain(object? sender, EventArgs e) => this.Refresh();
}

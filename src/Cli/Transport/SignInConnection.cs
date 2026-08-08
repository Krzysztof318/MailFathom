// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Credentials;

namespace MailFathom.Cli.Transport;

/// <summary>The connection one sign-in makes to a deployment, settling what it accepts the first time it is used.</summary>
/// <remarks>
/// <para>
/// A certificate this machine does not trust is not a failure to report but a question to ask, and it can only be asked
/// once the deployment has presented one. So the first operation runs against ordinary chain validation; if the
/// handshake refuses a certificate, the operator is shown it, and an acceptance reopens the connection pinned to exactly
/// that certificate and runs the operation again.
/// </para>
/// <para>
/// The retry is bounded to the first operation and to the certificate question, which is what makes it safe. The first
/// thing every mode of <c>login</c> sends is a read — the deployment's OAuth metadata, or its session — so a repeat
/// changes nothing at the deployment, and nothing interactive has happened yet: no browser has been opened, no device
/// code has been printed, and the credential a person types is read before the connection is used at all.
/// </para>
/// </remarks>
internal sealed class SignInConnection : IDisposable
{
    /// <summary>The switch a sign-in with nobody at the terminal states the certificate answer with.</summary>
    internal const string AllowanceOption = "--trust-untrusted-certificate";

    private readonly ICliConsole console;

    private readonly Func<Uri, StoredTransportTrust, DeploymentTransport> openTransport;

    private readonly Uri address;

    private readonly bool trustedUpFront;

    private DeploymentTransport transport;

    private bool settled;

    /// <summary>Opens the connection this sign-in will use.</summary>
    /// <param name="console">The terminal the certificate question is asked on.</param>
    /// <param name="openTransport">Opens a transport aimed at one address; this connection disposes what it opens.</param>
    /// <param name="address">The deployment being signed in to.</param>
    /// <param name="acceptsClearText">Whether the operator has already accepted an unprotected connection to it.</param>
    /// <param name="trustedUpFront">Whether the invocation stated the certificate answer with <see cref="AllowanceOption" />.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    internal SignInConnection(
        ICliConsole console,
        Func<Uri, StoredTransportTrust, DeploymentTransport> openTransport,
        Uri address,
        bool acceptsClearText,
        bool trustedUpFront)
    {
        ArgumentNullException.ThrowIfNull(console);
        ArgumentNullException.ThrowIfNull(openTransport);
        ArgumentNullException.ThrowIfNull(address);

        this.console = console;
        this.openTransport = openTransport;
        this.address = address;
        this.trustedUpFront = trustedUpFront;
        this.Trust = new StoredTransportTrust(PinnedCertificateFingerprint: null, acceptsClearText);
        this.transport = openTransport(address, this.Trust);
    }

    /// <summary>Gets what this sign-in has accepted about the connection, which is what the profile records.</summary>
    internal StoredTransportTrust Trust { get; private set; }

    /// <summary>Runs one operation against the deployment, settling the certificate question if it is still open.</summary>
    /// <typeparam name="TResult">What the operation produces.</typeparam>
    /// <param name="operation">The operation, which must be safe to repeat.</param>
    /// <returns>What the operation produced.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="operation" /> is <see langword="null" />.</exception>
    /// <exception cref="CliFailure">Thrown when the operation failed for any other reason, and when the operator refused the certificate.</exception>
    internal async Task<TResult> RunAsync<TResult>(Func<DeploymentTransport, Task<TResult>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        try
        {
            var result = await operation(this.transport);

            this.settled = true;

            return result;
        }
        catch (CliFailure) when (!this.settled && this.transport.RefusedCertificate is not null)
        {
            // Swallowed deliberately: the transport refused a certificate rather than failing, and what that means is
            // a question for the operator rather than a message. Reporting it here would end the sign-in the feature
            // exists to complete.
        }

        this.PinPresentedCertificate();

        return await operation(this.transport);
    }

    /// <inheritdoc />
    public void Dispose() => this.transport.Dispose();

    /// <summary>Accepts the certificate the connection refused, and reopens the connection bound to it.</summary>
    private void PinPresentedCertificate()
    {
        var presented = this.transport.RefusedCertificate
            ?? throw new InvalidOperationException("The certificate question was settled without a certificate having been refused.");

        if (!this.Accepts(presented))
        {
            throw new CliFailure(
                "The deployment's certificate was refused, so nothing was signed in to and nothing was stored.");
        }

        this.Trust = this.Trust with { PinnedCertificateFingerprint = presented.Fingerprint };
        this.settled = true;

        this.transport.Dispose();
        this.transport = this.openTransport(this.address, this.Trust);
    }

    /// <summary>Asks whether this certificate may be trusted for this profile, or reads the answer the invocation stated.</summary>
    private bool Accepts(PresentedCertificate presented)
    {
        if (this.trustedUpFront)
        {
            return true;
        }

        if (!this.console.CanConfirm)
        {
            throw new CliFailure(
                $"{this.address.GetLeftPart(UriPartial.Authority)} presented a certificate this machine does not trust ({presented.ValidationFailure}), and there is no terminal to ask on. Pass {AllowanceOption} to accept whatever certificate this deployment presents and pin it to the profile.");
        }

        this.console.WriteError(string.Empty);
        this.console.WriteError($"{this.address.GetLeftPart(UriPartial.Authority)} presented a certificate this machine does not trust:");
        this.console.WriteError(string.Empty);

        foreach (var line in presented.Lines())
        {
            this.console.WriteError(line);
        }

        this.console.WriteError(string.Empty);
        this.console.WriteError("Accepting it stores this fingerprint on the profile. Every later command then accepts this certificate and refuses any other,");
        this.console.WriteError("so a deployment that renews or replaces its certificate is signed in to again rather than trusted silently.");
        this.console.WriteError(string.Empty);

        return this.console.Confirm("Trust this certificate for this profile? [y/N]: ");
    }
}

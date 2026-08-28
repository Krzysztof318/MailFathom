// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Authorization;

namespace MailFathom.Client.Session;

/// <summary>What became of a username and a password somebody offered.</summary>
/// <remarks>
/// One closed set rather than two, because a person signing in does not care which layer refused them: whether what
/// they typed was not a credential at all, whether the deployment would not accept it, whether the deployment offers no
/// password sign-in, or whether nothing answered, what they get is one sentence and another try. The screen maps each
/// of these to exactly one string, which is why a case added here is a string owed in every language rather than a
/// message composed at the point of failure. It is <see cref="Deployment.DeploymentChoiceOutcome" />'s shape, for the
/// screen beside it, for the same reason.
/// </remarks>
public enum SignInOutcome
{
    /// <summary>The deployment accepted the credential, and the client is signed in.</summary>
    Accepted = 0,

    /// <summary>What was typed is not a credential this client would present — one half blank, or a username carrying a colon.</summary>
    NotACredential = 1,

    /// <summary>The deployment did not accept this username and password.</summary>
    /// <remarks>One case rather than two, because that is what the deployment answers with: it refuses an unknown username, a wrong password, a disabled credential, and a caller that has spent its attempts identically, and a client that guessed which would be inventing a distinction the service deliberately does not make.</remarks>
    CredentialRefused = 2,

    /// <summary>This deployment does not offer signing in with a password at all.</summary>
    PasswordSignInNotOffered = 3,

    /// <summary>Nothing answered there.</summary>
    Unreachable = 4,

    /// <summary>Something is there and did not answer in time.</summary>
    TimedOut = 5,

    /// <summary>Something answered, and it is not a MailFathom deployment.</summary>
    NotADeployment = 6,
}

/// <summary>What one attempt produced: what became of the credential, and what became of keeping it.</summary>
/// <param name="Outcome">What the deployment, or this client, made of what was typed.</param>
/// <param name="Persistence">Whether the next start opens already signed in, and where it does not, why.</param>
public sealed record SignInAttemptOutcome(SignInOutcome Outcome, CredentialPersistence Persistence);

/// <summary>Signing in and out, as the screens above <c>Client.Backend</c> reach it.</summary>
/// <remarks>
/// <para>
/// The same seam <see cref="Deployment.DeploymentChoice" /> is for an address, and for the same reason:
/// <c>Client.Backend</c> raises a <see cref="DeploymentFailure" /> for everything that is not an answer, and every
/// screen would otherwise be catching one and mapping it to a sentence for itself. What is here is the mapping and
/// nothing else — the credential is composed, handed straight to the assembly that presents it, and never held.
/// </para>
/// <para>
/// Nothing here logs, records, or reports either half of what was typed. A refusal reaches the screen as a case of the
/// enum above and a message chosen from a table, which is what keeps a password out of every diagnostic the client
/// could ever produce.
/// </para>
/// </remarks>
public sealed class OwnerSignIn
{
    private readonly DeploymentSignIn signIn;
    private readonly SignedInOwner owner;

    /// <summary>Initializes the seam over what presents a credential and what holds the session.</summary>
    /// <param name="signIn">What offers a credential to the deployment.</param>
    /// <param name="owner">Who is signed in during this run.</param>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public OwnerSignIn(DeploymentSignIn signIn, SignedInOwner owner)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(owner);

        this.signIn = signIn;
        this.owner = owner;
    }

    /// <summary>Gets what this head does with a credential, which is what the sign-in screen says before anything is typed.</summary>
    public CredentialPersistence Persistence => this.owner.Persistence;

    /// <summary>Gets the username somebody is signed in under, or <see langword="null" /> where nobody is.</summary>
    public string? Username => this.owner.Username;

    /// <summary>Offers what somebody typed to the deployment.</summary>
    /// <param name="username">The username, as written.</param>
    /// <param name="password">The password, as written.</param>
    /// <param name="cancellationToken">Abandons the attempt.</param>
    /// <returns>What became of it, and what became of keeping it.</returns>
    public async ValueTask<SignInAttemptOutcome> SignInAsync(
        string? username,
        string? password,
        CancellationToken cancellationToken = default)
    {
        OwnerCredential credential;

        try
        {
            credential = new OwnerCredential(username ?? string.Empty, password ?? string.Empty);
        }
        catch (ArgumentException)
        {
            // Refused before anything is sent, so a blank half and a username carrying a colon cost the deployment
            // nothing and reach the person while they are still looking at what they typed.
            return new SignInAttemptOutcome(SignInOutcome.NotACredential, this.Persistence);
        }

        try
        {
            var attempt = await this.signIn.SignInAsync(credential, cancellationToken).ConfigureAwait(false);

            return new SignInAttemptOutcome(Answered(attempt.Result), attempt.Persistence);
        }
        catch (DeploymentFailure failure)
        {
            return new SignInAttemptOutcome(Unreached(failure.Reason), this.Persistence);
        }
    }

    /// <summary>Ends the session and clears whatever this head kept of it.</summary>
    /// <param name="cancellationToken">Abandons the removal, which does not un-end the session.</param>
    /// <returns>A task completing once nothing of the session is held.</returns>
    public ValueTask SignOutAsync(CancellationToken cancellationToken = default) =>
        this.signIn.SignOutAsync(cancellationToken);

    /// <summary>Says what the deployment's own answer is, in the terms the screen speaks.</summary>
    private static SignInOutcome Answered(SignInResult result) => result switch
    {
        SignInResult.Accepted => SignInOutcome.Accepted,
        SignInResult.PasswordSignInNotOffered => SignInOutcome.PasswordSignInNotOffered,
        _ => SignInOutcome.CredentialRefused,
    };

    /// <summary>Says what an exchange that produced no answer is, in the same terms.</summary>
    /// <remarks>A refused credential cannot arrive here — the sign-in reads a refusal as an answer rather than as a failure — so it falls in with the answer nothing can be made of, which is what it would be.</remarks>
    private static SignInOutcome Unreached(DeploymentFailureReason reason) => reason switch
    {
        DeploymentFailureReason.Unreachable => SignInOutcome.Unreachable,
        DeploymentFailureReason.TimedOut => SignInOutcome.TimedOut,
        _ => SignInOutcome.NotADeployment,
    };
}

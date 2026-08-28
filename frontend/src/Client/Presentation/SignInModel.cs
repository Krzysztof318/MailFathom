// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend.Authorization;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation;

/// <summary>
/// The model behind <see cref="SignInPage"/>: where somebody says who they are on the deployment this client is
/// pointed at.
/// </summary>
/// <remarks>
/// <para>
/// A username and a password and nothing else. There is no control offering to remember the sign-in, because
/// persistence is not a choice this screen presents: a head that can keep the credential keeps it, one that cannot
/// keeps nothing, and the case a "remember me" box would serve — two people sharing one operating-system account — is
/// one no store on any of these platforms could answer either, since both people are the same user to the operating
/// system. What answers it is signing out.
/// </para>
/// <para>
/// What the screen does say is what will happen next time. A head that keeps nothing, and a machine whose store cannot
/// be reached, are two different sentences and both are shown before anything is typed rather than discovered by being
/// asked again.
/// </para>
/// <para>
/// The password is held in this model's state only for as long as the screen is open, because that is what a two-way
/// binding to a box is. It is cleared the moment an attempt has been made, whichever way that attempt went, and it is
/// handed to <see cref="OwnerSignIn" /> rather than kept anywhere else — nothing here writes it to a setting, a log, or
/// a message.
/// </para>
/// </remarks>
internal sealed partial record SignInModel
{
    private readonly OwnerSignIn signIn;
    private readonly INavigator navigator;
    private readonly IStringLocalizer localizer;

    /// <summary>Initializes the model over what presents a credential and where the client goes once one is accepted.</summary>
    /// <param name="signIn">What offers a credential to the deployment, and what says whether one is kept.</param>
    /// <param name="navigator">Where the person goes once they are signed in.</param>
    /// <param name="localizer">Where the sentence explaining a refusal comes from.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public SignInModel(OwnerSignIn signIn, INavigator navigator, IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        ArgumentNullException.ThrowIfNull(navigator);
        ArgumentNullException.ThrowIfNull(localizer);

        this.signIn = signIn;
        this.navigator = navigator;
        this.localizer = localizer;
    }

    /// <summary>The username as it is being written.</summary>
    /// <remarks>Opens empty rather than on whoever was signed in before. A client is signed in as one person at a time, and offering the last name back would be publishing it to whoever opens the application next on a shared machine.</remarks>
    public IState<string> Username => State.Value(this, () => string.Empty);

    /// <summary>The password as it is being written.</summary>
    /// <remarks>Bound from a <c>PasswordBox</c>, so the characters are never on the screen. It lives here for as long as this screen does and is cleared as soon as an attempt has been made.</remarks>
    public IState<string> Password => State.Value(this, () => string.Empty);

    /// <summary>Why the last attempt was not accepted, or empty where nothing has been refused.</summary>
    /// <remarks>
    /// A sentence rather than the failure itself. Everything <c>Client.Backend</c> raises carries an English message
    /// written for a log, and one of them can carry text from a machine this process does not own, so the screen says
    /// what happened in the language being read in and the outcome is what chooses which sentence.
    /// </remarks>
    public IState<string> Refusal => State.Value(this, () => string.Empty);

    /// <summary>Whether anything was refused, which is what puts the sentence on the screen.</summary>
    /// <remarks>Derived from the sentence rather than kept beside it, for the reason <see cref="ConnectModel.IsRefused" /> gives: two values saying the same thing are two values that can disagree.</remarks>
    public IFeed<bool> IsRefused => this.Refusal.Select(refusal => !string.IsNullOrEmpty(refusal));

    /// <summary>What this head will do with the credential once it is accepted, said before anything is typed.</summary>
    /// <remarks>Empty where the credential is kept, because there is nothing to say: the next start opens already signed in, which is what somebody expects of a mail application.</remarks>
    public IFeed<string> Keeping =>
        Feed.Async(_ => ValueTask.FromResult(this.Explain(this.signIn.Persistence)));

    /// <summary>Whether this head has something to say about the next start.</summary>
    public IFeed<bool> SaysHowLongItLasts => this.Keeping.Select(keeping => !string.IsNullOrEmpty(keeping));

    /// <summary>Whether the deployment is being asked about what was typed.</summary>
    public IState<bool> IsAsking => State.Value(this, () => false);

    /// <summary>Whether a credential may be offered, which it may not be while the last one is still being judged.</summary>
    public IFeed<bool> CanSignIn => this.IsAsking.Select(asking => !asking);

    /// <summary>Offers what has been typed, and opens the application where the deployment accepted it.</summary>
    /// <param name="ct">Abandons the attempt.</param>
    /// <returns>A task completing once the credential has been accepted or refused.</returns>
    public async ValueTask SignIn(CancellationToken ct)
    {
        await this.IsAsking.SetAsync(true, ct).ConfigureAwait(false);
        await this.Refusal.SetAsync(string.Empty, ct).ConfigureAwait(false);

        try
        {
            var attempt = await this.signIn
                .SignInAsync(await this.Username, await this.Password, ct)
                .ConfigureAwait(false);

            // Cleared whichever way the attempt went, so a password does not sit in a screen's state while somebody
            // reads a refusal and goes to look their password up.
            await this.Password.SetAsync(string.Empty, ct).ConfigureAwait(false);

            if (attempt.Outcome != SignInOutcome.Accepted)
            {
                await this.Refusal.SetAsync(this.Explain(attempt.Outcome), ct).ConfigureAwait(false);

                return;
            }
        }
        finally
        {
            await this.IsAsking.SetAsync(false, ct).ConfigureAwait(false);
        }

        // The back stack is cleared for the reason ConnectModel clears it: what is behind this screen is either the
        // screen that asked for a deployment or a session that has just ended, and the system back gesture returning to
        // either would offer a way into an application that is not where it was.
        await this.navigator
            .NavigateAsync(
                new NavigationRequest(
                    this,
                    new Route(Qualifier: Qualifiers.ClearBackStack, Base: ClientRoutes.Workspace),
                    ct))
            .ConfigureAwait(false);
    }

    /// <summary>Says what became of a credential in the language the person is reading in.</summary>
    /// <remarks>The resource name is composed from the outcome, so a case added to that set is a missing string the resource-table test names rather than a message that quietly falls back to a key.</remarks>
    private string Explain(SignInOutcome outcome) => this.localizer[$"SignInPage.Refusal.{outcome}"];

    /// <summary>Says what this head does with a credential, or nothing at all where it keeps one.</summary>
    private string Explain(CredentialPersistence persistence) => persistence == CredentialPersistence.Kept
        ? string.Empty
        : this.localizer[$"SignInPage.Keeping.{persistence}"];
}

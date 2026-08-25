// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Backend;
using MailFathom.Client.Backend.Accounts;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Spaces.Mail;

/// <summary>
/// The model behind <see cref="MailPage"/>: the mailboxes this space reads, each beside a statement of how current the
/// copy being read actually is.
/// </summary>
/// <remarks>
/// <para>
/// The client reads a copy of a mailbox rather than the mailbox, which is what makes it fast and what lets it work
/// while a mail server is briefly unreachable. The cost of that is a question this screen has to answer rather than
/// leave to be assumed, so every account says when it last took mail in and whether the deployment is still refreshing
/// it. Nothing here calls an account stale: how old is too old is the reader's judgement.
/// </para>
/// <para>
/// The accounts are read off the session rather than beside it. That is one subscription and one act for the person:
/// the session already asks the deployment again when the signed-in identity changes, when the client is pointed
/// somewhere else, and when a lost connection comes back, and this follows every one of those without a second timer,
/// a second retry curve, or a second button. The root instructions refuse nested retry storms, and a screen that
/// retried on top of the session's own bounded attempts would be one.
/// </para>
/// <para>
/// Whether this space may be offered at all is the same session's answer, read here rather than derived from a request
/// the deployment refused — which is what keeps a credential that may not read mail from reaching a list that would
/// have failed on its own terms.
/// </para>
/// </remarks>
public partial record MailModel
{
    private readonly DeploymentClient deployment;
    private readonly IClientSession session;
    private readonly TimeProvider clock;
    private readonly IStringLocalizer localizer;
    private readonly IState<int> asked;

    /// <summary>Initializes the space over what serves its mailboxes and what decides whether it may be offered.</summary>
    /// <param name="deployment">Where the owner's accounts and their freshness are asked for.</param>
    /// <param name="session">What the deployment allows this caller, and whether it can be reached at all.</param>
    /// <param name="clock">What a freshness gap is measured against.</param>
    /// <param name="localizer">Where the words describing a standing and a gap come from, since both are composed rather than fixed per control.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public MailModel(
        DeploymentClient deployment,
        IClientSession session,
        TimeProvider clock,
        IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(localizer);

        this.deployment = deployment;
        this.session = session;
        this.clock = clock;
        this.localizer = localizer;

        // Held as states rather than handed on as the session's own feed, for the reason the frame holds one: a feed
        // is read from the start by whoever subscribes, and the projections below would otherwise each be a reader of
        // their own — which for the accounts would be a request per projection.
        var standing = State.FromFeed(this, session.Standing);

        // Two things make the mailboxes worth reading again, and the read follows both. The session, so a sign-in, a
        // deployment somebody pointed the client at, and a connection that came back all reach here without this
        // screen listening for any of them; and the count below, because a session that answers a second time with the
        // same grant is one message MVUX does not republish — so a person pressing the button on a read that failed
        // while the session was fine would otherwise press something that did nothing.
        this.asked = State.Value(this, () => 0);

        var accounts = State.FromFeed(
            this,
            Feed.Combine(standing, this.asked).SelectAsync(this.ReadAccountsAsync));

        this.WithholdsMail = standing.Select(session => session.Withholds(ClientCapability.Mail));
        this.Accounts = accounts.Select(this.Describe).AsListFeed();
        this.SynchronizationPaused = accounts.Select(answer => !answer.SynchronizationEnabled);
    }

    /// <summary>The owner's mailboxes, each with how current its copy is.</summary>
    /// <remarks>
    /// A list feed rather than a feed of a list, so the three states this screen genuinely has are the framework's
    /// rather than each one remembered: the fetch under way, the fetch that failed, and the owner who owns no account
    /// at all. The last of those is a state to render with what to do about it rather than an empty list somebody has
    /// to interpret.
    /// </remarks>
    public IListFeed<MailAccountLine> Accounts { get; }

    /// <summary>Whether this deployment has stopped refreshing these mailboxes at all.</summary>
    /// <remarks>
    /// The deployment's switch rather than the owner's, and it is beside the accounts rather than on them because no
    /// per-account value carries it: a copy that last moved a week ago means one thing where the deployment is still
    /// trying and another where somebody switched synchronization off, and a screen that could not tell the two apart
    /// would report every account as failing or none of them.
    /// </remarks>
    public IFeed<bool> SynchronizationPaused { get; }

    /// <summary>Whether this session keeps the space correspondence is read in from being put in front of this caller.</summary>
    /// <remarks>
    /// The space's own reading of the session the frame reads, stated as an affirmative for the reason
    /// <see cref="SessionStanding.Withholds" /> gives: a control shown on the absence of an offer would be on the
    /// screen before the session had answered.
    /// </remarks>
    public IFeed<bool> WithholdsMail { get; }

    /// <summary>Asks the deployment again, which is what a person presses when a read did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once both asks have been made.</returns>
    /// <remarks>
    /// One press, two things asked, because they are one question to the person: whether their deployment is there.
    /// The session is asked because it decides what may be offered and because it is where a lost connection is
    /// recovered; the count is raised because a session answering a second time with the same grant is a message MVUX
    /// does not republish, and a button that quietly did nothing on the commonest case — the accounts read alone
    /// having failed — would be worse than no button.
    /// </remarks>
    public async ValueTask RetryAccounts(CancellationToken cancellationToken)
    {
        this.session.Refresh();

        await this.asked.UpdateAsync(asked => asked + 1, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the owner's accounts, once the session that decides whether they may be read has arrived.</summary>
    /// <remarks>Neither part of the trigger is read: what they are here for is when this runs, rather than what it asks for.</remarks>
    private async ValueTask<DeploymentMailAccounts> ReadAccountsAsync(
        (SessionStanding Standing, int Asked) trigger,
        CancellationToken cancellationToken) =>
        await this.deployment.ReadMailAccountsAsync(cancellationToken).ConfigureAwait(false);

    private IImmutableList<MailAccountLine> Describe(DeploymentMailAccounts answered)
    {
        // One instant for the whole list rather than one per row, so two accounts refreshed together are never
        // described as having been refreshed at different times.
        var now = this.clock.GetUtcNow();

        return [.. answered.Owned.Select(account => MailAccountLine.Of(account, now, this.localizer))];
    }
}

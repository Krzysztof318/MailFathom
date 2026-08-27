// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Session;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Workspace;

/// <summary>
/// The model behind <see cref="WorkspacePage"/>, which is the frame the three spaces are shown inside rather than a
/// space of its own. It owns what sits above them: the question being composed, what that question would be asked
/// against, and which of the spaces this session may be offered at all.
/// </summary>
/// <remarks>
/// Neither the question nor the scope is answered here. Running a question is Discover's work, and narrowing a scope is
/// done by the space somebody is narrowing in — the frame's part is that both survive moving between spaces, which is
/// why it reads them from <see cref="IWorkspace"/> rather than holding them itself. What it does decide is what to put
/// in front of somebody, and it decides that from <see cref="IClientSession"/> rather than from a request the
/// deployment refused.
/// </remarks>
public partial record WorkspaceModel
{
    private const string EverythingKey = "WorkspaceScope.Everything";
    private const string AccountFolderKey = "WorkspaceScope.AccountFolder";
    private const string RoleKey = "WorkspaceScope.Role";
    private const string SelectedKey = "WorkspaceScope.Selected";
    private const string ConnectionAttemptKey = "Workspace.Connection.Attempt";

    private readonly IWorkspace workspace;
    private readonly IClientSession session;
    private readonly IMailboxTree mailboxes;
    private readonly IStringLocalizer localizer;

    /// <summary>Initializes the frame over the workspace its spaces share, the session that decides what it offers, and the tree that narrows it.</summary>
    /// <param name="workspace">The question and the scope every space reads and writes.</param>
    /// <param name="session">What the deployment allows this caller, which is what the frame offers from.</param>
    /// <param name="mailboxes">The tree somebody chooses the scope in, which the frame renders because every space reads what it chose.</param>
    /// <param name="localizer">Where the words describing a scope come from, since they are composed rather than per control.</param>
    /// <exception cref="ArgumentNullException">Thrown when any argument is <see langword="null" />.</exception>
    public WorkspaceModel(
        IWorkspace workspace,
        IClientSession session,
        IMailboxTree mailboxes,
        IStringLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(mailboxes);
        ArgumentNullException.ThrowIfNull(localizer);

        this.workspace = workspace;
        this.session = session;
        this.mailboxes = mailboxes;
        this.localizer = localizer;

        this.ScopeDescription = workspace.Scope.Select(this.Describe);

        // Held as a state rather than handed on as the session's own feed, so the frame and every projection below it
        // read one subscription: a feed is read from the start by whoever subscribes, and the four projections here
        // would otherwise each be a reader of their own.
        this.Session = State.FromFeed(this, session.Standing);

        this.OffersDiscover = this.Offers(ClientCapability.Discover);
        this.OffersMail = this.Offers(ClientCapability.Mail);
        this.OffersCases = this.Offers(ClientCapability.Cases);
        this.WithholdsDiscover = this.Withholds(ClientCapability.Discover);
        this.WithholdsMail = this.Withholds(ClientCapability.Mail);
        this.WithholdsCases = this.Withholds(ClientCapability.Cases);
        this.OffersNothing = this.Session.Select(standing => standing.OffersNothing);
        this.AnythingUngranted = this.Any(CapabilityStanding.Ungranted);
        this.AnythingUnavailable = this.Any(CapabilityStanding.Unavailable);

        // Held as a state for the reason the session above is: the three projections below would otherwise each be a
        // reader of their own of what is one answer about one connection.
        var connection = State.FromFeed(this, session.Connection);

        this.IsRetryingDeployment = connection.Select(standing => standing.IsRetrying);
        this.HasLostDeployment = connection.Select(standing => standing.IsLost);
        this.HasReachedDeployment = connection.Select(standing => standing.IsReached);
        this.ConnectionAttempt = connection.Select(this.DescribeAttempt);
    }

    /// <summary>What somebody is about to ask, held for the run rather than for the screen they typed it on.</summary>
    public IState<string> Intent => this.workspace.Intent;

    /// <summary>The mailboxes and their folders as one tree, which is where the scope above is chosen.</summary>
    /// <remarks>
    /// Rendered by the frame rather than by the Mail space, because what it narrows is read by all three of them: the
    /// list, the search, and the field a question is composed in are each about wherever this says somebody is. It is
    /// the run's own tree read through this model rather than one built here, so moving between spaces keeps what was
    /// open and costs no second read of the deployment.
    /// </remarks>
    public IListFeed<MailboxRow> Mailboxes => this.mailboxes.Rows;

    /// <summary>Whether this deployment has stopped refreshing the mailboxes the tree draws.</summary>
    public IFeed<bool> MailboxesPaused => this.mailboxes.SynchronizationPaused;

    /// <summary>The scope the question would be asked against, as the words the indicator beside the field shows.</summary>
    /// <remarks>
    /// Built once rather than per read: the indicator is bound to it, and a second feed per read would leave the view
    /// and anything the model asked subscribed to two descriptions of one scope.
    /// </remarks>
    public IFeed<string> ScopeDescription { get; }

    /// <summary>What the deployment allows this caller, which the frame both offers from and renders the state of.</summary>
    /// <remarks>
    /// Shown through a <c>FeedView</c> rather than read for a value, because fetching it can be under way and can
    /// fail: a shell that rendered nothing while the answer was unknown would be an empty frame, and one that rendered
    /// nothing when it failed would be an empty frame that never fills.
    /// </remarks>
    public IFeed<SessionStanding> Session { get; }

    /// <summary>Whether the space a question is asked in may be put in front of this caller.</summary>
    /// <remarks>The field the question is composed in follows it, because the field is that space's own act reached from the frame rather than something the frame does.</remarks>
    public IFeed<bool> OffersDiscover { get; }

    /// <summary>Whether the space correspondence is read in may be put in front of this caller.</summary>
    public IFeed<bool> OffersMail { get; }

    /// <summary>Whether the space a thread of work is followed in may be put in front of this caller.</summary>
    public IFeed<bool> OffersCases { get; }

    /// <summary>Whether this session keeps the space a question is asked in from being put in front of this caller.</summary>
    /// <remarks>
    /// Stated beside its opposite rather than derived from it in the view, because a space says what it is instead
    /// only where the session outright withheld it. A feed carrying no value yet reaches a binding as the type's
    /// default, so a view that showed the withheld state on the absence of an offer would announce it before the
    /// session had answered — see <see cref="SessionStanding.Withholds" />.
    /// </remarks>
    public IFeed<bool> WithholdsDiscover { get; }

    /// <summary>Whether this session keeps the space correspondence is read in from being put in front of this caller.</summary>
    public IFeed<bool> WithholdsMail { get; }

    /// <summary>Whether this session keeps the space a thread of work is followed in from being put in front of this caller.</summary>
    public IFeed<bool> WithholdsCases { get; }

    /// <summary>Whether this session leaves the frame with no space at all to open on.</summary>
    /// <remarks>
    /// Read from the frame rather than from inside the state's own template, so the sentence stays reachable however
    /// the three spaces above resolve: a shell offering nothing has to say so once rather than showing three withheld
    /// spaces and no reason for any of them.
    /// </remarks>
    public IFeed<bool> OffersNothing { get; }

    /// <summary>Whether something this client can show is withheld because this caller's credential does not carry it.</summary>
    /// <remarks>
    /// Kept apart from the one below because conflating them produces the wrong message. This is the case somebody
    /// acts on by asking whoever runs their MailFathom to widen what their credential may do.
    /// </remarks>
    public IFeed<bool> AnythingUngranted { get; }

    /// <summary>Whether something this client can show is withheld because this deployment does not provide it.</summary>
    /// <remarks>
    /// The case no permission would change. Saying <em>you may not</em> here would send somebody looking for a grant
    /// nobody can give them, which is exactly what the two notices exist to keep apart.
    /// </remarks>
    public IFeed<bool> AnythingUnavailable { get; }

    /// <summary>Whether the client is asking its deployment again after an attempt that did not arrive.</summary>
    /// <remarks>
    /// The first attempt is deliberately not one of these, so an ordinary start shows no banner. What this is for is
    /// the case a person would otherwise read as the application having frozen: a connection that dropped, and a
    /// client working its way back to it without being restarted.
    /// </remarks>
    public IFeed<bool> IsRetryingDeployment { get; }

    /// <summary>Whether the client has stopped asking on its own, which is the point the next ask becomes a person's.</summary>
    /// <remarks>
    /// Kept apart from a session that could not be had for the reason the two withholding notices are kept apart: a
    /// deployment nothing answered from is a connection to look at, and a deployment that answered a refusal is a
    /// sign-in to repeat. Telling somebody to check their network about a credential their operator has to widen sends
    /// them nowhere.
    /// </remarks>
    public IFeed<bool> HasLostDeployment { get; }

    /// <summary>Whether the deployment answered at all, whatever it answered.</summary>
    /// <remarks>
    /// What the failed-session notice is shown on, so the two never speak at once. A session that failed while nothing
    /// was answering is the lost connection above and is said there; this is what is left — the deployment is there
    /// and would not give this client a session.
    /// </remarks>
    public IFeed<bool> HasReachedDeployment { get; }

    /// <summary>Which attempt at the deployment is under way, as the words shown beside the notice.</summary>
    /// <remarks>Composed rather than fixed per control, because the numbers are the whole point of showing it: a client that says which attempt it is on is one somebody waits for rather than restarts.</remarks>
    public IFeed<string> ConnectionAttempt { get; }

    /// <summary>
    /// The keys the words above are resolved by, in the one place they are written.
    /// </summary>
    /// <remarks>
    /// A scope is described by composing words rather than by a <c>x:Uid</c> on a control, so these keys are asked
    /// for from code — which makes a typo in one of them the single way a reader would meet the key itself instead of
    /// a sentence. The unit suite holds every authored table to naming all four.
    /// </remarks>
    internal static IReadOnlyList<string> ScopeResourceKeys { get; } =
        [EverythingKey, AccountFolderKey, RoleKey, SelectedKey];

    /// <summary>The keys the connection notice's own words are resolved by, on the same terms as the scope's.</summary>
    internal static IReadOnlyList<string> ConnectionResourceKeys { get; } = [ConnectionAttemptKey];

    /// <summary>Asks the deployment again for what this caller may do.</summary>
    /// <remarks>What the button on the failed state presses. It says nothing about what went wrong — the answer, or the next failure, arrives on <see cref="Session" /> exactly as the first one did.</remarks>
    public void RetrySession() => this.session.Refresh();

    /// <summary>Shows or hides what is nested under one row of the tree.</summary>
    /// <param name="key">The row's key, which the view hands over from the row it drew.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the tree says so.</returns>
    /// <remarks>
    /// It carries a parameter, so the command it is generated from runs in parallel for different rows and reports no
    /// progress of its own — which is right here, because opening a row is local work over an answer the client already
    /// holds and there is nothing to wait for.
    /// </remarks>
    public ValueTask ToggleMailbox(string key, CancellationToken cancellationToken) =>
        this.mailboxes.ToggleAsync(key, cancellationToken);

    /// <summary>Narrows the workspace to what one row of the tree stands for.</summary>
    /// <param name="row">The row somebody chose.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the scope says so.</returns>
    public ValueTask SelectMailbox(MailboxRow row, CancellationToken cancellationToken) =>
        this.mailboxes.SelectAsync(row, cancellationToken);

    /// <summary>Asks the deployment for the mailboxes again, which is what a person presses when the tree did not arrive.</summary>
    /// <param name="cancellationToken">Abandons the ask.</param>
    /// <returns>A task completing once the ask has been made.</returns>
    public ValueTask RetryMailboxes(CancellationToken cancellationToken) =>
        this.mailboxes.AskAgainAsync(cancellationToken);

    private IFeed<bool> Offers(ClientCapability capability) =>
        this.Session.Select(standing => standing.Offers(capability));

    private IFeed<bool> Withholds(ClientCapability capability) =>
        this.Session.Select(standing => standing.Withholds(capability));

    private IFeed<bool> Any(CapabilityStanding standing) =>
        this.Session.Select(session => session.Any(standing));

    private string DescribeAttempt(DeploymentConnection reach) =>
        this.localizer[ConnectionAttemptKey, reach.Attempt, reach.Attempts].Value;

    private string Describe(WorkspaceScope scope)
    {
        var where = scope switch
        {
            { Account: { } account, Folder: { } folder } => this.localizer[AccountFolderKey, account, folder].Value,
            { Account: { } account } => account,

            // One special-use folder taken across every mailbox, which is the one narrowing that is not a place in a
            // single mailbox. The role is named in the language the application is being read in rather than by the
            // word the deployment published it as.
            { Role: { } role } => this.localizer[RoleKey, this.localizer[MailboxWords.RoleResourceKeyFor(role)].Value].Value,

            // A folder without an account is not a scope anything here builds, and the type refuses nothing, so it is
            // named rather than dropped: falling through to "everything" would say the opposite of what is in force.
            { Folder: { } folder } => folder,
            _ => this.localizer[EverythingKey].Value,
        };

        return scope.Selection.Count is 0
            ? where
            : this.localizer[SelectedKey, where, scope.Selection.Count].Value;
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using System.Reflection;
using MailFathom.Client.Backend;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Workspace;

/// <summary>
/// The frame's own model: the field a question is composed in, and the indicator saying what it would be asked
/// against.
/// </summary>
public sealed class WorkspaceModelTests : IDisposable
{
    /// <summary>A session offering everything, for the tests whose subject is not what the deployment allows.</summary>
    private readonly StubClientSession offeringEverything =
        SessionOffering("mailfathom.mail.ask", "mailfathom.mail.read");

    /// <inheritdoc />
    public void Dispose() => this.offeringEverything.Dispose();

    /// <summary>
    /// The field holds the workspace's question rather than one of its own, which is what makes the question survive
    /// a move between spaces instead of ending with the screen it was typed on.
    /// </summary>
    [Fact]
    public async Task Intent_TypedIntoTheField_IsTheWorkspacesOwnQuestion()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await model.Intent.SetAsync("which invoices are still unpaid", TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(workspace.Intent, model.Intent);
        Assert.Equal("which invoices are still unpaid", await workspace.Intent);
    }

    /// <summary>Nothing narrowed reads as everything, rather than as a blank where a scope should be.</summary>
    [Fact]
    public async Task ScopeDescription_AWorkspaceNothingHasNarrowed_SaysEverything()
    {
        // Arrange
        await using var model = this.ModelOver(new SharedWorkspace(new StubMailboxTreeMemory()));

        // Act
        var description = await model.ScopeDescription;

        // Assert
        Assert.Equal("All your mail", description);
    }

    /// <summary>An account alone is named as itself: an account name is not a word to translate.</summary>
    [Fact]
    public async Task ScopeDescription_AnAccountAlone_IsNamedAsItself()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await workspace.Scope.UpdateAsync(
            _ => WorkspaceScope.Everything with { Account = "work@example.test" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("work@example.test", await model.ScopeDescription);
    }

    /// <summary>A folder is read within its account, so the indicator names both rather than the narrower of the two.</summary>
    [Fact]
    public async Task ScopeDescription_AFolderWithinAnAccount_NamesBoth()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope { Account = "work@example.test", Folder = "Inbox" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("work@example.test · Inbox", await model.ScopeDescription);
    }

    /// <summary>
    /// A folder named without an account is not a scope anything here builds and is not one the type refuses either,
    /// so the indicator names it rather than reading as everything, which would say the opposite of what is in force.
    /// </summary>
    [Fact]
    public async Task ScopeDescription_AFolderWithoutAnAccount_IsNamedRatherThanDropped()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await workspace.Scope.UpdateAsync(
            _ => WorkspaceScope.Everything with { Folder = "Inbox" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Inbox", await model.ScopeDescription);
    }

    /// <summary>A selection narrows the scope further, so the indicator says how much of it is in hand.</summary>
    [Fact]
    public async Task ScopeDescription_ASelectionWithinAFolder_CountsIt()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope
            {
                Account = "work@example.test",
                Folder = "Inbox",
                Selection = ImmutableArray.Create("117", "118"),
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("work@example.test · Inbox · 2 selected", await model.ScopeDescription);
    }

    [Fact]
    public async Task ScopeDescription_APassageWithinOneMessage_SaysTheScopeIsTheSelectedPassage()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope
            {
                Account = "work@example.test",
                Folder = "Inbox",
                Selection = ImmutableArray.Create("117"),
                BodySelection = "the selected passage",
            },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("work@example.test · Inbox · selected passage", await model.ScopeDescription);
    }

    /// <summary>
    /// A frame built again over the same workspace — which is what a reload of the client's route is — reads the scope
    /// a space narrowed to rather than starting over at everything.
    /// </summary>
    [Fact]
    public async Task ScopeDescription_AFrameBuiltAgainOverTheSameWorkspace_ReadsWhatWasNarrowedBefore()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var narrowing = this.ModelOver(workspace);
        await workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope { Account = "work@example.test", Folder = "Archive" },
            TestContext.Current.CancellationToken);

        // Act
        await using var rebuilt = this.ModelOver(workspace);

        // Assert
        Assert.Equal(await narrowing.ScopeDescription, await rebuilt.ScopeDescription);
        Assert.Equal("work@example.test · Archive", await rebuilt.ScopeDescription);
    }

    /// <summary>
    /// A role taken across mailboxes narrows no single account, so the indicator names the role in the reader's own
    /// language rather than falling back to everything, which would say the opposite of what is in force.
    /// </summary>
    [Fact]
    public async Task ScopeDescription_ARoleTakenAcrossMailboxes_NamesTheRoleRatherThanAnAccount()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        await using var model = this.ModelOver(workspace);

        // Act
        await workspace.Scope.UpdateAsync(
            _ => new WorkspaceScope { Role = "Sent" },
            TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("Sent, every mailbox", await model.ScopeDescription);
    }

    /// <summary>
    /// The tree is the client's scope selector, and the frame holds it rather than owning one of its own — so the rows
    /// the pane binds to are the tree's, and every act on a row is handed straight on to it.
    /// </summary>
    [Fact]
    public async Task Mailboxes_TheTreeInTheFramesPane_IsTheTreeRatherThanACopyOfIt()
    {
        // Arrange
        var mailboxes = new StubMailboxTree { Paused = true };
        await using var model = this.ModelOver(mailboxes: mailboxes);

        // Act
        await model.ToggleMailbox(MailboxTreeShape.AccountKey("work"), TestContext.Current.CancellationToken);
        await model.RetryMailboxes(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(mailboxes.Rows, model.Mailboxes);
        Assert.True(await model.MailboxesPaused);
        Assert.Equal([MailboxTreeShape.AccountKey("work")], mailboxes.Toggled);
        Assert.Equal(1, mailboxes.Asks);
    }

    /// <summary>Choosing a row is the tree's act, so the frame passes the row on rather than narrowing the scope itself.</summary>
    [Fact]
    public async Task SelectMailbox_ARowSomebodyChose_IsHandedToTheTree()
    {
        // Arrange
        var mailboxes = new StubMailboxTree();
        await using var model = this.ModelOver(mailboxes: mailboxes);
        var row = new MailboxRow(
            MailboxTreeShape.AccountKey("work"),
            MailboxRowKind.Account,
            Depth: 0,
            "Work mail",
            UnreadCount: 3,
            StoredCount: 40,
            Standing: "being refreshed",
            Freshness: "updated within the last hour",
            IsUnreachable: false,
            IsFailing: false,
            IsBehind: false,
            IsExpandable: true,
            IsExpanded: false,
            IsSelected: false,
            new WorkspaceScope { Account = "work" });

        // Act
        await model.SelectMailbox(row, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([row], mailboxes.Selected);
    }

    /// <summary>
    /// The description is built once rather than per read, because the indicator is bound to it and a second feed per
    /// read would leave the view and anything the model asked subscribed to two descriptions of one scope.
    /// </summary>
    [Fact]
    public async Task ScopeDescription_ReadTwice_IsOneFeed()
    {
        // Arrange
        await using var model = this.ModelOver(new SharedWorkspace(new StubMailboxTreeMemory()));

        // Act
        var (first, second) = (model.ScopeDescription, model.ScopeDescription);

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>What the credential carries is what the frame puts in front of somebody, and nothing beside it.</summary>
    [Fact]
    public async Task OffersDiscover_AGrantCarryingAsking_OffersTheSpaceAndTheFieldWithIt()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.ask");
        await using var model = this.ModelOver(session: session);

        // Act, Assert
        Assert.True(await model.OffersDiscover);
        Assert.False(await model.OffersMail);
    }

    /// <summary>A space the credential does not permit is absent rather than present and refused when it is pressed.</summary>
    [Fact]
    public async Task OffersMail_AGrantNotCarryingReading_WithholdsTheSpace()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.ask");
        await using var model = this.ModelOver(session: session);

        // Act, Assert
        Assert.False(await model.OffersMail);
        Assert.False(await model.OffersCases);
    }

    /// <summary>
    /// A withheld space renders what it is instead, so the frame states the withholding as its own affirmative rather
    /// than leaving a view to read the offer backwards. A control shown on the absence of an offer would be on the
    /// screen before the session had answered, which would announce a fetch still under way as a space taken away.
    /// </summary>
    [Fact]
    public async Task WithholdsMail_AGrantNotCarryingReading_SaysSoRatherThanLeavingTheOfferToBeInverted()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.ask");
        await using var model = this.ModelOver(session: session);

        // Act, Assert
        Assert.True(await model.WithholdsMail);
        Assert.True(await model.WithholdsCases);
        Assert.False(await model.WithholdsDiscover);
    }

    /// <summary>
    /// The two reasons a thing is withheld reach the frame separately, because a credential is widened by whoever
    /// runs the deployment and a deployment that does not have something is widened by no credential at all.
    /// </summary>
    [Fact]
    public async Task AnythingUngranted_ASessionWithholdingForBothReasons_KeepsTheTwoReasonsApart()
    {
        // Arrange
        using var withheldByTheGrant = SessionOffering("mailfathom.mail.read");
        await using var granted = this.ModelOver(session: withheldByTheGrant);

        using var withheldByTheDeployment = new StubClientSession(
            SessionStanding.Of(
                new DeploymentSession("MailFathom", "0.8.0", ["mailfathom.mail.ask", "mailfathom.mail.read"]),
                SessionStanding.EveryCapability.Remove(ClientCapability.Discover)));
        await using var deployed = this.ModelOver(session: withheldByTheDeployment);

        // Act, Assert
        Assert.True(await granted.AnythingUngranted);
        Assert.False(await granted.AnythingUnavailable);

        Assert.True(await deployed.AnythingUnavailable);
        Assert.False(await deployed.AnythingUngranted);
    }

    /// <summary>A session that leaves the shell with nothing to open on says so once rather than showing three withheld spaces.</summary>
    [Fact]
    public async Task OffersNothing_ACallerGrantedNothing_IsSaidOnce()
    {
        // Arrange
        using var session = SessionOffering();
        await using var model = this.ModelOver(session: session);

        // Act, Assert
        Assert.True(await model.OffersNothing);
    }

    /// <summary>
    /// A first attempt is not a retry, so an ordinary start shows no connection notice at all — a banner on every
    /// launch would be one nobody reads by the time it means something.
    /// </summary>
    [Fact]
    public async Task IsRetryingDeployment_ADeploymentThatAnswered_SaysNothingAboutTheConnection()
    {
        // Arrange
        await using var model = this.ModelOver();

        // Act, Assert
        Assert.False(await model.IsRetryingDeployment);
        Assert.False(await model.HasLostDeployment);
        Assert.True(await model.HasReachedDeployment);
    }

    /// <summary>
    /// A client working its way back to a deployment says which attempt it is on, because seeing the count move is
    /// what makes it something somebody waits for rather than restarts.
    /// </summary>
    [Fact]
    public async Task ConnectionAttempt_AClientTryingAgain_SaysWhichAttemptIsUnderWay()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        session.Answered = new DeploymentConnection(ConnectionStanding.Reaching, Attempt: 2, Attempts: 5);
        await using var model = this.ModelOver(session: session);

        // Act, Assert
        Assert.True(await model.IsRetryingDeployment);
        Assert.False(await model.HasReachedDeployment);
        Assert.Equal("Attempt 2 of 5", await model.ConnectionAttempt);
    }

    /// <summary>
    /// A deployment nothing answered from and a deployment that refused this credential lead to different acts, so
    /// the frame never says both at once: the failed-session notice is shown only where something answered.
    /// </summary>
    [Fact]
    public async Task HasLostDeployment_AClientThatStoppedTrying_IsKeptApartFromASessionThatWasRefused()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        session.Answered = new DeploymentConnection(ConnectionStanding.Lost, Attempt: 5, Attempts: 5);
        await using var model = this.ModelOver(session: session);

        // Act, Assert
        Assert.True(await model.HasLostDeployment);
        Assert.False(await model.HasReachedDeployment);
        Assert.False(await model.IsRetryingDeployment);
    }

    /// <summary>Asking the deployment again is the session's act, so the frame hands it on rather than fetching for itself.</summary>
    [Fact]
    public async Task RetrySession_PressedOnAFailedSession_AsksTheSessionAgain()
    {
        // Arrange
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = this.ModelOver(session: session);

        // Act
        model.RetrySession();

        // Assert
        Assert.Equal(1, session.Refreshes);
    }

    /// <summary>A frame that could be built without the workspace would be a frame whose spaces shared nothing.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        var workspace = new SharedWorkspace(new StubMailboxTreeMemory());
        using var session = SessionOffering();
        var mailboxes = new StubMailboxTree();
        var words = ScopeWords();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WorkspaceModel(null!, session, mailboxes, words));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceModel(workspace, null!, mailboxes, words));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceModel(workspace, session, null!, words));
        Assert.Throws<ArgumentNullException>(() => new WorkspaceModel(workspace, session, mailboxes, null!));
    }

    /// <summary>
    /// The frame's model is a plain record reachable without a visual tree, and nothing on its surface or in its
    /// fields is a WinUI type — which is what keeps the composition above it a view's decision rather than a model's.
    /// </summary>
    [Fact]
    public void WorkspaceModel_TheFramesModel_NamesNoXamlType()
    {
        // Arrange
        const BindingFlags Instance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var model = typeof(WorkspaceModel);

        // Act
        var named = model.GetConstructors().SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType)
            .Concat(model.GetProperties(Instance).Select(property => property.PropertyType))
            .Concat(model.GetFields(Instance).Select(field => field.FieldType))
            .Concat(model.GetMethods(Instance).Where(method => method.DeclaringType == model).SelectMany(WrittenBy))
            .SelectMany(Unwrap)
            .Where(type => type.Namespace?.StartsWith("Microsoft.UI.Xaml", StringComparison.Ordinal) is true)
            .Select(type => type.FullName)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Assert
        Assert.Empty(named);

        static IEnumerable<Type> WrittenBy(MethodInfo method) =>
            method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType);

        static IEnumerable<Type> Unwrap(Type type) =>
            type.GetGenericArguments().SelectMany(Unwrap).Append(type);
    }

    private WorkspaceModel ModelOver(
        IWorkspace? workspace = null,
        StubClientSession? session = null,
        StubMailboxTree? mailboxes = null) =>
        new(
            workspace ?? new SharedWorkspace(new StubMailboxTreeMemory()),
            session ?? this.offeringEverything,
            mailboxes ?? new StubMailboxTree(),
            ScopeWords());

    private static StubClientSession SessionOffering(params string[] permissions) =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", permissions)));

    private static StubStringLocalizer ScopeWords() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["WorkspaceScope.Everything"] = "All your mail",
        ["WorkspaceScope.AccountFolder"] = "{0} · {1}",
        ["WorkspaceScope.Role"] = "{0}, every mailbox",
        ["WorkspaceScope.Selected"] = "{0} · {1} selected",
        ["WorkspaceScope.BodySelection"] = "{0} · selected passage",
        ["Mailboxes.Role.Sent"] = "Sent",
        ["Workspace.Connection.Attempt"] = "Attempt {0} of {1}",
    });
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using MailFathom.Client.Presentation.Spaces.Mail;
using MailFathom.Client.Session;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Spaces.Mail;

/// <summary>The Mail space: which mailboxes it reads, and what it says about how current each copy is.</summary>
public sealed class MailModelTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 25, 12, 0, 0, TimeSpan.Zero);

    private const string TwoAccounts =
        """
        {
          "synchronizationEnabled": true,
          "accounts": [
            { "id": "work", "displayName": "Work mail", "synchronizationState": "Synchronized",
              "lastSynchronizedAt": "2026-08-25T11:50:00+00:00" },
            { "id": "home", "displayName": "Home mail", "synchronizationState": "Failing",
              "lastSynchronizedAt": "2026-08-18T09:00:00+00:00" }
          ]
        }
        """;

    /// <summary>Each mailbox is listed with the gap it is being read at, said as words rather than as an instant.</summary>
    [Fact]
    public async Task Accounts_ADeploymentAnswering_DescribesEachMailboxAndItsGap()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(TwoAccounts));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);

        // Act
        var accounts = await model.Accounts;

        // Assert
        Assert.NotNull(accounts);
        Assert.Equal(2, accounts.Count);

        Assert.Equal("Work mail", accounts[0].DisplayName);
        Assert.Equal("updated within the last hour", accounts[0].Freshness);
        Assert.False(accounts[0].IsFailing);

        Assert.Equal("Home mail", accounts[1].DisplayName);
        Assert.Equal("nothing taken in for over a week", accounts[1].Freshness);
        Assert.True(accounts[1].IsFailing);
    }

    /// <summary>
    /// An owner who owns no mailbox is a state the screen renders rather than a failure it reports, so the list is
    /// empty and nothing raised.
    /// </summary>
    [Fact]
    public async Task Accounts_AnOwnerWhoOwnsNoMailbox_IsEmptyRatherThanAFailure()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("""{"synchronizationEnabled":true,"accounts":[]}"""));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);

        // Act
        var accounts = await model.Accounts;

        // Assert
        Assert.True(accounts is null || accounts.Count is 0);
    }

    /// <summary>
    /// A deployment that stopped refreshing changes what every gap means, and no per-account value carries it, so the
    /// space says it beside them.
    /// </summary>
    [Fact]
    public async Task SynchronizationPaused_ADeploymentThatStoppedRefreshing_SaysSoBesideTheMailboxes()
    {
        // Arrange
        using var harness = new DeploymentHarness(
            _ => StubTransport.JsonResponse("""{"synchronizationEnabled":false,"accounts":[]}"""));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);

        // Act, Assert
        Assert.True(await model.SynchronizationPaused);
    }

    /// <summary>A deployment that is still refreshing says nothing, so the notice is absent rather than reassuring.</summary>
    [Fact]
    public async Task SynchronizationPaused_ADeploymentStillRefreshing_SaysNothing()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(TwoAccounts));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);

        // Act, Assert
        Assert.False(await model.SynchronizationPaused);
    }

    /// <summary>
    /// The space reads whether it may be offered from the session rather than from a request the deployment refused,
    /// which is what keeps a credential that may not read mail off a list that would have failed on its own terms.
    /// </summary>
    [Fact]
    public async Task WithholdsMail_AGrantNotCarryingReading_SaysSoRatherThanLeavingTheOfferToBeInverted()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(TwoAccounts));
        using var withheld = SessionOffering("mailfathom.mail.ask");
        await using var withheldModel = ModelOver(harness, withheld);

        using var offered = SessionOffering("mailfathom.mail.read");
        await using var offeredModel = ModelOver(harness, offered);

        // Act, Assert
        Assert.True(await withheldModel.WithholdsMail);
        Assert.False(await offeredModel.WithholdsMail);
    }

    /// <summary>
    /// One read for the whole screen. The projections are built over one state, so a space showing a list and a
    /// notice beside it is one request rather than one per thing shown.
    /// </summary>
    [Fact]
    public async Task Accounts_ReadBesideTheNoticeAboutThem_AsksTheDeploymentOnce()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(TwoAccounts));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);

        // Act
        await model.Accounts;
        await model.SynchronizationPaused;
        await model.Accounts;

        // Assert
        Assert.Single(harness.Deployment.Requests);
    }

    /// <summary>
    /// Asking again is the session's act rather than this screen's, which is what keeps the two from disagreeing
    /// about whether the deployment is there — and what makes the mailboxes follow a connection that came back.
    /// </summary>
    [Fact]
    public async Task RetryAccounts_PressedOnAFailedRead_AsksTheSessionAgainAndReadsTheMailboxesWithIt()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(TwoAccounts));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);
        await model.Accounts;

        // Act
        await model.RetryAccounts(TestContext.Current.CancellationToken);
        await model.Accounts;

        // Assert
        Assert.Equal(1, session.Refreshes);
        Assert.Equal(2, harness.Deployment.Requests.Count);
    }

    /// <summary>A read that did not arrive reaches the screen as the feed's error axis rather than as no mailboxes.</summary>
    [Fact]
    public async Task Accounts_ADeploymentThatDidNotAnswer_ReachesTheScreenAsAFailure()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => throw new HttpRequestException("nothing is answering"));
        using var session = SessionOffering("mailfathom.mail.read");
        await using var model = ModelOver(harness, session);

        // Act
        var failure = await Assert.ThrowsAsync<DeploymentFailure>(async () => await model.Accounts);

        // Assert
        Assert.Equal(DeploymentFailureReason.Unreachable, failure.Reason);
    }

    /// <summary>A space that could be built without one of its collaborators would be one describing no mailbox.</summary>
    [Fact]
    public void Constructor_AMissingService_IsRefused()
    {
        // Arrange
        using var harness = new DeploymentHarness(_ => StubTransport.JsonResponse(TwoAccounts));
        using var session = SessionOffering("mailfathom.mail.read");
        var clock = new StubClock(Now);
        var words = Words();

        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => new MailModel(null!, session, clock, words));
        Assert.Throws<ArgumentNullException>(() => new MailModel(harness.Client, null!, clock, words));
        Assert.Throws<ArgumentNullException>(() => new MailModel(harness.Client, session, null!, words));
        Assert.Throws<ArgumentNullException>(() => new MailModel(harness.Client, session, clock, null!));
    }

    private static MailModel ModelOver(DeploymentHarness harness, StubClientSession session) =>
        new(harness.Client, session, new StubClock(Now), Words());

    private static StubClientSession SessionOffering(params string[] permissions) =>
        new(SessionStanding.Of(new DeploymentSession("MailFathom", "0.8.0", permissions)));

    private static StubStringLocalizer Words() => new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MailPage.Account.Standing.Unrecognized"] = "state not recognized",
        ["MailPage.Account.Standing.NeverSynchronized"] = "not synchronized yet",
        ["MailPage.Account.Standing.Synchronized"] = "being refreshed",
        ["MailPage.Account.Standing.Failing"] = "not reachable",
        ["MailPage.Account.Freshness.Never"] = "no mail taken in yet",
        ["MailPage.Account.Freshness.WithinTheHour"] = "updated within the last hour",
        ["MailPage.Account.Freshness.Today"] = "updated within the last day",
        ["MailPage.Account.Freshness.WithinTheWeek"] = "updated within the last week",
        ["MailPage.Account.Freshness.LongerAgo"] = "nothing taken in for over a week",
    });
}

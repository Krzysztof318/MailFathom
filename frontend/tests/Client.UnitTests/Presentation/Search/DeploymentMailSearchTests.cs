// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;
using MailFathom.Client.Presentation.Mailboxes;
using MailFathom.Client.Presentation.Messages;
using MailFathom.Client.Presentation.Search;
using MailFathom.Client.Presentation.Workspace;
using MailFathom.Client.UnitTests.TestDoubles;

namespace MailFathom.Client.UnitTests.Presentation.Search;

/// <summary>The ranked mail one run keeps while the reader opens and leaves its conversations.</summary>
public sealed class DeploymentMailSearchTests
{
    /// <summary>The search starts where the mailbox tree left the workspace and sends every filter as one request.</summary>
    [Fact]
    public async Task SearchAsync_AQueryUnderTheCurrentScope_ReadsAndExplainsItsResults()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(Page(1, nextCursor: null, "LexicalRanking", "Inactive")),
            cancellationToken: TestContext.Current.CancellationToken);
        var workspace = Workspace(new WorkspaceScope { Account = "work", Role = "Inbox" });
        var search = Search(harness, workspace, new StubMailThread());

        await search.OpenAsync(TestContext.Current.CancellationToken);
        await search.Query.SetAsync("quarter", TestContext.Current.CancellationToken);
        await search.Sender.SetAsync("someone@example.test", TestContext.Current.CancellationToken);
        await search.ReceivedOnOrAfter.SetAsync(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken);
        await search.SetUnreadAsync(true, TestContext.Current.CancellationToken);

        // Act
        await search.SearchAsync(TestContext.Current.CancellationToken);

        // Assert
        var row = Assert.Single((await search.Results)!);
        var asked = Assert.Single(harness.Deployment.Requests).RequestUri;
        Assert.NotNull(asked);
        Assert.Contains("query=quarter", asked.Query, StringComparison.Ordinal);
        Assert.Contains("account=work", asked.Query, StringComparison.Ordinal);
        Assert.Contains("folder=role%3AInbox", asked.Query, StringComparison.Ordinal);
        Assert.Contains("sender=someone%40example.test", asked.Query, StringComparison.Ordinal);
        Assert.Contains("unread=true", asked.Query, StringComparison.Ordinal);

        Assert.Equal(MailMessages.Key(1), row.Key);
        Assert.Equal("Matched by words", row.MatchReason);
        Assert.Equal("The **quarter** closed well", row.MatchExtract);
        Assert.Contains("Matched by words", row.Announcement, StringComparison.Ordinal);
        Assert.Contains("The **quarter** closed well", row.Announcement, StringComparison.Ordinal);

        var reading = await search.Reading;
        Assert.True(reading!.HasSearched);
        Assert.True(reading.SemanticSearchInactive);
        Assert.False(reading.SemanticSearchDegraded);
        Assert.Equal("work / Inbox", reading.Scope);

        var recent = Assert.Single((await search.Recent)!);
        Assert.Equal("quarter", recent.Query);
    }

    /// <summary>A following page extends the list, and opening one row leaves both pages in place.</summary>
    [Fact]
    public async Task ShowMoreAsync_AResultOpenedAtItsMessage_PreservesTheRankedList()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            request => StubTransport.JsonResponse(
                request.RequestUri?.Query.Contains("cursor=next", StringComparison.Ordinal) is true
                    ? Page(2, nextCursor: null, "LexicalRanking", "Available")
                    : Page(1, "next", "LexicalRanking", "Available")),
            cancellationToken: TestContext.Current.CancellationToken);
        var thread = new StubMailThread();
        var search = Search(harness, Workspace(WorkspaceScope.Everything), thread);

        await search.OpenAsync(TestContext.Current.CancellationToken);
        await search.Query.SetAsync("quarter", TestContext.Current.CancellationToken);
        await search.SearchAsync(TestContext.Current.CancellationToken);

        // Act
        await search.ShowMoreAsync(TestContext.Current.CancellationToken);
        var rows = (await search.Results)!;
        await search.OpenResultAsync(rows[1], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], rows.Select(row => row.Key));
        Assert.Equal([(MailThreads.Identity, MailMessages.Identity(2))], thread.Opened);
        Assert.Equal([MailMessages.Key(1), MailMessages.Key(2)], (await search.Results)!.Select(row => row.Key));
    }

    /// <summary>A result found only by meaning explains itself without inventing an extract containing the query words.</summary>
    [Fact]
    public async Task Results_ASemanticMatchWithoutAnExtract_SaysWhyItIsPresent()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(Page(1, nextCursor: null, "SemanticRanking", "Available")),
            cancellationToken: TestContext.Current.CancellationToken);
        var search = Search(harness, Workspace(WorkspaceScope.Everything), new StubMailThread());
        await search.Query.SetAsync("roof leak", TestContext.Current.CancellationToken);

        // Act
        await search.SearchAsync(TestContext.Current.CancellationToken);
        var row = Assert.Single((await search.Results)!);

        // Assert
        Assert.Equal("Matched by meaning", row.MatchReason);
        Assert.False(row.HasMatchExtract);
    }

    /// <summary>A unified sent search draws recipients like the sent timeline rather than repeating the owner's name.</summary>
    [Fact]
    public async Task Results_ASearchAcrossTheSentRole_DrawsTheRecipientsAsTheCorrespondent()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(Page(1, nextCursor: null, "LexicalRanking", "Available")),
            cancellationToken: TestContext.Current.CancellationToken);
        var search = Search(
            harness,
            Workspace(new WorkspaceScope { Role = "Sent" }),
            new StubMailThread());
        await search.OpenAsync(TestContext.Current.CancellationToken);
        await search.Query.SetAsync("quarter", TestContext.Current.CancellationToken);

        // Act
        await search.SearchAsync(TestContext.Current.CancellationToken);
        var row = Assert.Single((await search.Results)!);

        // Assert
        Assert.Equal("owner@example.test", row.Correspondent);
    }

    /// <summary>Every filter can be removed alone, leaving its neighbours and the query where they were.</summary>
    [Fact]
    public async Task ClearFilterAsync_OneFilterInForce_RemovesOnlyThatFilter()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(Page(1, nextCursor: null, "BothRankings", "Available")),
            cancellationToken: TestContext.Current.CancellationToken);
        var search = Search(harness, Workspace(WorkspaceScope.Everything), new StubMailThread());

        await search.Query.SetAsync("quarter", TestContext.Current.CancellationToken);
        await search.Sender.SetAsync("someone@example.test", TestContext.Current.CancellationToken);
        await search.Recipient.SetAsync("owner@example.test", TestContext.Current.CancellationToken);
        await search.SetFlaggedAsync(false, TestContext.Current.CancellationToken);

        // Act
        await search.ClearFilterAsync(MailSearchFilter.Sender, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(string.IsNullOrEmpty(await search.Sender));
        Assert.Equal("owner@example.test", await search.Recipient);
        Assert.False(await search.Flagged);
        Assert.Equal("quarter", await search.Query);
    }

    /// <summary>Repeating a query moves it to the front rather than filling the recent list with duplicates.</summary>
    [Fact]
    public async Task SearchAsync_TheSameQueryAskedAgain_KeepsOneRecentEntry()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(Page(1, nextCursor: null, "BothRankings", "Available")),
            cancellationToken: TestContext.Current.CancellationToken);
        var search = Search(harness, Workspace(WorkspaceScope.Everything), new StubMailThread());
        await search.Query.SetAsync("quarter", TestContext.Current.CancellationToken);

        // Act
        await search.SearchAsync(TestContext.Current.CancellationToken);
        await search.SearchAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Single((await search.Recent)!);
    }

    /// <summary>Widening an empty result removes the place constraints and immediately asks the broader question.</summary>
    [Fact]
    public async Task WidenAsync_AFolderScopedSearch_SearchesAllMailUnderTheRemainingFilters()
    {
        // Arrange
        using var harness = await DeploymentHarness.CreateAsync(
            _ => StubTransport.JsonResponse(Page(1, nextCursor: null, "BothRankings", "Available")),
            cancellationToken: TestContext.Current.CancellationToken);
        var search = Search(harness, Workspace(WorkspaceScope.Everything), new StubMailThread());
        await search.Query.SetAsync("quarter", TestContext.Current.CancellationToken);
        await search.Account.SetAsync("work", TestContext.Current.CancellationToken);
        await search.Folder.SetAsync("INBOX", TestContext.Current.CancellationToken);

        // Act
        await search.WidenAsync(TestContext.Current.CancellationToken);
        _ = await search.Results;

        // Assert
        var asked = Assert.Single(harness.Deployment.Requests).RequestUri;
        Assert.NotNull(asked);
        Assert.DoesNotContain("account=", asked.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("folder=", asked.Query, StringComparison.Ordinal);
        Assert.Contains("query=quarter", asked.Query, StringComparison.Ordinal);
    }

    private static DeploymentMailSearch Search(
        DeploymentHarness harness,
        IWorkspace workspace,
        StubMailThread thread) =>
        new(
            harness.Client,
            workspace,
            thread,
            new StubClock(new DateTimeOffset(2026, 8, 25, 12, 0, 0, TimeSpan.Zero)),
            Words());

    private static SharedWorkspace Workspace(WorkspaceScope scope) =>
        new(
            new StubMailboxTreeMemory(
                new RememberedMailboxes(scope, ImmutableHashSet<string>.Empty)));

    private static StubStringLocalizer Words() => new(
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [MessageWords.NoSenderKey] = "Unknown sender",
            [MessageWords.NoSubjectKey] = "No subject",
            [MessageWords.NoDateKey] = "No date",
            [MessageWords.MoreRecipientsKey] = "{0} and {1} more",
            [MessageWords.AnnouncementKey] = "{0}, {1}, {2}.",
            [MessageWords.UnreadKey] = "Unread.",
            [MessageWords.FlaggedKey] = "Flagged.",
            [MessageWords.AnsweredKey] = "Answered.",
            [MessageWords.AttachmentsKey] = "Has attachments.",
            [MailSearchWords.LexicalKey] = "Matched by words",
            [MailSearchWords.SemanticKey] = "Matched by meaning",
            [MailSearchWords.BothKey] = "Matched by words and meaning",
            [MailSearchWords.ScopeEverythingKey] = "All mail",
            [MailSearchWords.ScopeAccountKey] = "{0}",
            [MailSearchWords.ScopeFolderKey] = "{0} / {1}",
        });

    private static string Page(int number, string? nextCursor, string matchedBy, string semanticSearch) =>
        $$"""
        {
          "results": [
            {
              "id": "{{MailMessages.Identity(number):D}}",
              "account": "work",
              "folder": "INBOX",
              "threadId": "{{MailThreads.Identity:D}}",
              "subject": "Quarterly review",
              "receivedAt": "2026-08-25T11:50:00+00:00",
              "sentAt": "2026-08-25T11:49:00+00:00",
              "senderAddress": "someone@example.test",
              "senderDisplayName": "Someone",
              "toAddresses": [ "owner@example.test" ],
              "unread": false,
              "flagged": false,
              "answered": false,
              "hasAttachments": false,
              "attachmentCount": 0,
              "sizeOctets": 1024,
              "preview": "The quarter closed well",
              "snippets": {{(matchedBy is "SemanticRanking" ? "[]" : "[ \"The **quarter** closed well\" ]")}},
              "matchedBy": "{{matchedBy}}"
            }
          ],
          "nextCursor": {{(nextCursor is null ? "null" : $"\"{nextCursor}\"")}},
          "pageSize": 20,
          "retrievalMode": "{{(semanticSearch is "Available" ? "Hybrid" : "Lexical")}}",
          "semanticSearch": "{{semanticSearch}}",
          "includedJunkMail": false
        }
        """;
}

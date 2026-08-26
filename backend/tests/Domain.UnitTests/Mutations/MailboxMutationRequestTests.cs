// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class MailboxMutationRequestTests
{
    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create("Archive", '/');

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("file-newsletters", "3");

    /// <summary>The parameter names a wrongly shaped request can be refused against, whichever is checked first.</summary>
    private static readonly string[] RejectedParameterNames =
        ["destinationPath", "desiredSeenState", "localDisposition"];

    /// <summary>Each mutation carries exactly the parameters it takes, which is what the factories exist to guarantee.</summary>
    [Fact]
    public void Factories_ForEachMutation_CarryOnlyTheParametersThatMutationTakes()
    {
        // Act
        var relocate = MailboxMutationRequest.Relocate(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, Archive);
        var delete = MailboxMutationRequest.Delete(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            AuthoredDeleteEmailDisposition.EraseLocalCopy);
        var setSeen = MailboxMutationRequest.SetSeen(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, isSeen: true);
        var copy = MailboxMutationRequest.Copy(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, Archive);

        // Assert
        Assert.Equal(Archive, relocate.DestinationPath);
        Assert.Null(relocate.DesiredSeenState);
        Assert.Null(relocate.LocalDisposition);
        Assert.Null(delete.DestinationPath);
        Assert.Null(delete.DesiredSeenState);
        Assert.Equal(AuthoredDeleteEmailDisposition.EraseLocalCopy, delete.LocalDisposition);
        Assert.Null(setSeen.DestinationPath);
        Assert.True(setSeen.DesiredSeenState);
        Assert.Null(setSeen.LocalDisposition);
        Assert.Equal(Archive, copy.DestinationPath);
        Assert.Equal(MailboxMutation.Copy, copy.Mutation);
        Assert.Null(copy.LocalDisposition);
    }

    /// <summary>A stored row hands the parameters back loose, so a combination no mutation has is rejected on the way in.</summary>
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Create_RelocationWithTheWrongParameters_IsRefused(bool withDestination, bool withSeenState)
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.Relocate,
            Requester,
            withDestination ? Archive : null,
            withSeenState ? true : null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition: null));

        // Assert
        Assert.Contains(refusal.ParamName, RejectedParameterNames, StringComparer.Ordinal);
    }

    /// <summary>A delete names only a local disposition, so a destination on one is a request nobody could have meant.</summary>
    [Fact]
    public void Create_DeleteNamingADestination_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.Delete,
            Requester,
            Archive,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            AuthoredDeleteEmailDisposition.RetainLocalCopy));

        // Assert
        Assert.Equal("destinationPath", refusal.ParamName);
    }

    /// <summary>A delete decides what becomes of the local copy, so a stored row that decided nothing is not acted on.</summary>
    [Fact]
    public void Create_DeleteNamingNoLocalDisposition_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.Delete,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition: null));

        // Assert
        Assert.Equal("localDisposition", refusal.ParamName);
    }

    /// <summary>A copy adds an occurrence and removes none, so it never disposes of a local copy and a disposition on one names nothing.</summary>
    [Fact]
    public void Create_CopyNamingALocalDisposition_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.Copy,
            Requester,
            Archive,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            AuthoredDeleteEmailDisposition.EraseLocalCopy));

        // Assert
        Assert.Equal("localDisposition", refusal.ParamName);
    }

    /// <summary>A relocation into a folder MailFathom does not mirror loses the occurrence for good, so it disposes of the local copy exactly as a delete does.</summary>
    [Theory]
    [InlineData(AuthoredDeleteEmailDisposition.EraseLocalCopy)]
    [InlineData(AuthoredDeleteEmailDisposition.RetainLocalCopy)]
    public void Relocate_ADestinationNothingMirrors_CarriesTheDispositionItWasAuthoredUnder(
        AuthoredDeleteEmailDisposition disposition)
    {
        // Act
        var request = MailboxMutationRequest.Relocate(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, Archive, disposition);

        // Assert
        Assert.Equal(disposition, request.LocalDisposition);
        Assert.Equal(Archive, request.DestinationPath);
    }

    /// <summary>A destination MailFathom mirrors carries the row across instead, so the relocation decides nothing about the local copy.</summary>
    [Fact]
    public void Relocate_AMirroredDestination_CarriesNoLocalDisposition()
    {
        // Act
        var request = MailboxMutationRequest.Relocate(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, Archive);

        // Assert
        Assert.Null(request.LocalDisposition);
    }

    /// <summary>A disposition outside the declared set is refused wherever it arrives, so a relocation cannot write one a record could not be read back from.</summary>
    [Fact]
    public void Relocate_AnUndeclaredDisposition_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => MailboxMutationRequest.Relocate(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            Archive,
            (AuthoredDeleteEmailDisposition)99));

        // Assert
        Assert.Equal("localDisposition", refusal.ParamName);
    }

    /// <summary>A disposition outside the declared set names no decision, so it never reaches the durable record.</summary>
    [Fact]
    public void Create_DeleteNamingAnUndeclaredDisposition_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.Delete,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            (AuthoredDeleteEmailDisposition)97));

        // Assert
        Assert.Equal("localDisposition", refusal.ParamName);
    }

    /// <summary>The struct default names no mutation, so a row that somehow carried one is not reconstructed.</summary>
    [Fact]
    public void Create_UnspecifiedMutation_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            default,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition: null));

        // Assert
        Assert.Equal("mutation", refusal.ParamName);
    }

    /// <summary>Each flag change names its own direction and nothing else, which is what keeps the two flags separate answers.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SetFlagged_EitherDirection_CarriesTheFlaggedStateAndNoSeenState(bool isFlagged)
    {
        // Act
        var request = MailboxMutationRequest.SetFlagged(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, isFlagged);

        // Assert
        Assert.Equal(MailboxMutation.SetFlagged, request.Mutation);
        Assert.Equal(isFlagged, request.DesiredFlaggedState);
        Assert.Null(request.DesiredSeenState);
        Assert.Null(request.Keywords);
        Assert.Null(request.DestinationPath);
    }

    /// <summary>The three keyword mutations differ in what the server is asked to do, never in what they carry.</summary>
    [Fact]
    public void KeywordFactories_Always_CarryTheKeywordsAndNoFlagDirection()
    {
        // Arrange
        var keywords = AuthoredMailKeywords.Create(["$Todo"]);

        // Act
        var added = MailboxMutationRequest.AddKeywords(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, keywords);
        var removed = MailboxMutationRequest.RemoveKeywords(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, keywords);
        var replaced = MailboxMutationRequest.SetKeywords(LocalEmail, SyntheticMailOwner.Deployment, Occurrence(), Requester, keywords);

        // Assert
        Assert.Equal(
            [MailboxMutation.AddKeywords, MailboxMutation.RemoveKeywords, MailboxMutation.SetKeywords],
            new[] { added, removed, replaced }.Select(request => request.Mutation));
        Assert.Equal(
            [keywords, keywords, keywords],
            new[] { added, removed, replaced }.Select(request => request.Keywords));
        Assert.All(
            new[] { added, removed, replaced },
            request => Assert.Null(request.DesiredFlaggedState));
    }

    /// <summary>Clearing every keyword is something to ask for, and it is the one keyword mutation that can say it.</summary>
    [Fact]
    public void SetKeywords_NamingNone_IsTheRequestThatClearsThemAll()
    {
        // Act
        var request = MailboxMutationRequest.SetKeywords(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            AuthoredMailKeywords.None);

        // Assert
        Assert.Equal(MailboxMutation.SetKeywords, request.Mutation);
        Assert.True(request.Keywords?.IsEmpty);
    }

    /// <summary>Adding or removing nothing asks the server for nothing, which is a mistyped list rather than an intent.</summary>
    /// <remarks>
    /// Both are here rather than only the addition, because one condition refuses them together — it names the
    /// replacement as the exception rather than naming the two it applies to. A regression narrowing it to the addition
    /// would leave an empty removal reaching a mail server, and nothing else in this suite would say so.
    /// </remarks>
    [Fact]
    public void AddKeywords_NamingNone_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.AddKeywords(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            AuthoredMailKeywords.None));

        // Assert
        Assert.Equal("keywords", refusal.ParamName);
    }

    /// <inheritdoc cref="AddKeywords_NamingNone_IsRefused" />
    [Fact]
    public void RemoveKeywords_NamingNone_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.RemoveKeywords(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            AuthoredMailKeywords.None));

        // Assert
        Assert.Equal("keywords", refusal.ParamName);
    }

    /// <summary>
    /// A missing keyword set is a missing argument rather than a mutation that names none, so it fails as the first.
    /// Without the guard it would reach the parameter check and be refused as the wrong mutation, which sends whoever
    /// reads the message looking at the mutation instead of at the argument they did not pass.
    /// </summary>
    [Fact]
    public void KeywordFactories_HandedNoKeywordSetAtAll_RefuseTheMissingArgument()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => MailboxMutationRequest.AddKeywords(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            keywords: null!));
        Assert.Throws<ArgumentNullException>(() => MailboxMutationRequest.RemoveKeywords(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            keywords: null!));
        Assert.Throws<ArgumentNullException>(() => MailboxMutationRequest.SetKeywords(
            LocalEmail, SyntheticMailOwner.Deployment,
            Occurrence(),
            Requester,
            keywords: null!));
    }

    /// <summary>A stored row hands the parameters back loose, so a flag direction on a keyword mutation is rejected on the way in.</summary>
    [Fact]
    public void Create_AKeywordMutationNamingAFlagDirection_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.AddKeywords,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: true,
            AuthoredMailKeywords.Create(["$Todo"]),
            localDisposition: null));

        // Assert
        Assert.Equal("desiredFlaggedState", refusal.ParamName);
    }

    /// <summary>A flag change names a direction and nothing else, so a stored row carrying keywords beside one is refused.</summary>
    [Fact]
    public void Create_AFlagChangeNamingKeywords_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.SetFlagged,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: true,
            AuthoredMailKeywords.Create(["$Todo"]),
            localDisposition: null));

        // Assert
        Assert.Equal("keywords", refusal.ParamName);
    }

    /// <summary>A keyword mutation with no keywords at all could not be performed, so a row that carries none is refused.</summary>
    [Fact]
    public void Create_AKeywordMutationCarryingNoKeywordsColumn_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentException>(() => MailboxMutationRequest.Create(
            LocalEmail,
            SyntheticMailOwner.Deployment,
            Occurrence(),
            MailboxMutation.SetKeywords,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            desiredFlaggedState: null,
            keywords: null,
            localDisposition: null));

        // Assert
        Assert.Equal("keywords", refusal.ParamName);
    }

    private static EmailOccurrenceId Occurrence() => EmailOccurrenceId.Create(
        MailAccountId.Create("personal"),
        MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create("inbox"),
            RemoteFolderPath.Create("INBOX", '/')).Id,
        ImapUidValidity.Create(7U),
        ImapUid.Create(42U));
}

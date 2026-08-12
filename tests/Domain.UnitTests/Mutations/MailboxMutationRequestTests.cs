// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Accounts;
using MailFathom.Domain.Emails;
using MailFathom.Domain.Folders;
using MailFathom.Domain.Mutations;
using Xunit;

namespace MailFathom.Domain.UnitTests.Mutations;

public sealed class MailboxMutationRequestTests
{
    private static readonly RemoteFolderPath Archive = RemoteFolderPath.Create("Archive", '/');

    private static readonly StoredEmailId LocalEmail = StoredEmailId.Create(Guid.CreateVersion7());

    private static readonly MailboxMutationRequester Requester =
        MailboxMutationRequester.Rule("file-newsletters", 3);

    /// <summary>The parameter names a wrongly shaped request can be refused against, whichever is checked first.</summary>
    private static readonly string[] RejectedParameterNames =
        ["destinationPath", "desiredSeenState", "localDisposition"];

    /// <summary>Each mutation carries exactly the parameters it takes, which is what the factories exist to guarantee.</summary>
    [Fact]
    public void Factories_ForEachMutation_CarryOnlyTheParametersThatMutationTakes()
    {
        // Act
        var relocate = MailboxMutationRequest.Relocate(LocalEmail, Occurrence(), Requester, Archive);
        var delete = MailboxMutationRequest.Delete(
            LocalEmail,
            Occurrence(),
            Requester,
            AuthoredDeleteEmailDisposition.EraseLocalCopy);
        var setSeen = MailboxMutationRequest.SetSeen(LocalEmail, Occurrence(), Requester, isSeen: true);
        var copy = MailboxMutationRequest.Copy(LocalEmail, Occurrence(), Requester, Archive);

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
            Occurrence(),
            MailboxMutation.Relocate,
            Requester,
            withDestination ? Archive : null,
            withSeenState ? true : null,
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
            Occurrence(),
            MailboxMutation.Delete,
            Requester,
            Archive,
            desiredSeenState: null,
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
            Occurrence(),
            MailboxMutation.Delete,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
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
            Occurrence(),
            MailboxMutation.Copy,
            Requester,
            Archive,
            desiredSeenState: null,
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
        var request = MailboxMutationRequest.Relocate(LocalEmail, Occurrence(), Requester, Archive, disposition);

        // Assert
        Assert.Equal(disposition, request.LocalDisposition);
        Assert.Equal(Archive, request.DestinationPath);
    }

    /// <summary>A destination MailFathom mirrors carries the row across instead, so the relocation decides nothing about the local copy.</summary>
    [Fact]
    public void Relocate_AMirroredDestination_CarriesNoLocalDisposition()
    {
        // Act
        var request = MailboxMutationRequest.Relocate(LocalEmail, Occurrence(), Requester, Archive);

        // Assert
        Assert.Null(request.LocalDisposition);
    }

    /// <summary>A disposition outside the declared set is refused wherever it arrives, so a relocation cannot write one a record could not be read back from.</summary>
    [Fact]
    public void Relocate_AnUndeclaredDisposition_IsRefused()
    {
        // Act
        var refusal = Assert.Throws<ArgumentOutOfRangeException>(() => MailboxMutationRequest.Relocate(
            LocalEmail,
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
            Occurrence(),
            MailboxMutation.Delete,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
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
            Occurrence(),
            default,
            Requester,
            destinationPath: null,
            desiredSeenState: null,
            localDisposition: null));

        // Assert
        Assert.Equal("mutation", refusal.ParamName);
    }

    private static EmailOccurrenceId Occurrence() => EmailOccurrenceId.Create(
        MailAccountId.Create("personal"),
        MailFolderResolution.FirstBindingOf(
            MailFolderAlias.Create("inbox"),
            RemoteFolderPath.Create("INBOX", '/')).Id,
        ImapUidValidity.Create(7U),
        ImapUid.Create(42U));
}

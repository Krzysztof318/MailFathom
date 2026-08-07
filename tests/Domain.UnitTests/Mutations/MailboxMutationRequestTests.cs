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

    /// <summary>The two parameter names a wrongly shaped request can be refused against, whichever is checked first.</summary>
    private static readonly string[] RejectedParameterNames = ["destinationPath", "desiredSeenState"];

    /// <summary>Each mutation carries exactly the parameters it takes, which is what the factories exist to guarantee.</summary>
    [Fact]
    public void Factories_ForEachMutation_CarryOnlyTheParametersThatMutationTakes()
    {
        // Act
        var relocate = MailboxMutationRequest.Relocate(LocalEmail, Occurrence(), Requester, Archive);
        var delete = MailboxMutationRequest.Delete(LocalEmail, Occurrence(), Requester);
        var setSeen = MailboxMutationRequest.SetSeen(LocalEmail, Occurrence(), Requester, isSeen: true);
        var copy = MailboxMutationRequest.Copy(LocalEmail, Occurrence(), Requester, Archive);

        // Assert
        Assert.Equal(Archive, relocate.DestinationPath);
        Assert.Null(relocate.DesiredSeenState);
        Assert.Null(delete.DestinationPath);
        Assert.Null(delete.DesiredSeenState);
        Assert.Null(setSeen.DestinationPath);
        Assert.True(setSeen.DesiredSeenState);
        Assert.Equal(Archive, copy.DestinationPath);
        Assert.Equal(MailboxMutation.Copy, copy.Mutation);
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
            withSeenState ? true : null));

        // Assert
        Assert.Contains(refusal.ParamName, RejectedParameterNames, StringComparer.Ordinal);
    }

    /// <summary>A delete names nothing, so a destination on one is a request nobody could have meant.</summary>
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
            desiredSeenState: null));

        // Assert
        Assert.Equal("destinationPath", refusal.ParamName);
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
            desiredSeenState: null));

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

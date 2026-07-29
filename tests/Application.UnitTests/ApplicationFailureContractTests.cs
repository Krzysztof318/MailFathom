// Copyright © 2026 Krzysztof Kasprowicz

using MailMcp.Application.EmailContent;
using MailMcp.Application.Persistence;
using MailMcp.Application.Synchronization;
using MailMcp.Domain.Accounts;
using MailMcp.Domain.Emails;
using MailMcp.Domain.Failures;
using MailMcp.Domain.Folders;
using MailMcp.TestSupport;
using Xunit;

namespace MailMcp.Application.UnitTests;

/// <summary>Covers the failure contract the application boundary raises and the result that replaced one of its exceptions.</summary>
public sealed class ApplicationFailureContractTests
{
    /// <summary>A failure outside the hierarchy carries no code a boundary can report and obeys no stated message contract.</summary>
    [Fact]
    public void ApplicationAssembly_EveryDeclaredException_DerivesFromMailMcpException()
    {
        // Arrange
        var applicationAssembly = typeof(MailboxSynchronizer).Assembly;

        // Act, Assert
        ExceptionHierarchyAssertion.AssertEveryDeclaredExceptionDerivesFrom(applicationAssembly, typeof(MailMcpException));
    }

    [Fact]
    public void ErrorCode_PersistenceConcurrencyConflict_IsTheCodeForThatFailure()
    {
        // Act
        var failure = new PersistenceConcurrencyConflictException("A competing writer changed the same rows.");

        // Assert
        Assert.Equal(MailMcpErrorCode.PersistenceConcurrencyConflict, failure.ErrorCode);
        Assert.Equal("A competing writer changed the same rows.", failure.Message);
    }

    [Fact]
    public void ErrorCode_MailboxUnavailable_IsTheCodeForThatFailure()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var rejection = new InvalidOperationException("pipeline rejection");

        // Act
        var failure = new MailboxUnavailableException(accountId, rejection);

        // Assert
        Assert.Equal(MailMcpErrorCode.MailboxUnavailable, failure.ErrorCode);
        Assert.Same(rejection, failure.InnerException);
    }

    /// <summary>Folder discovery reaches the server for the account and no folder exists to name, which is what an absent alias states.</summary>
    [Fact]
    public void MailboxUnavailableException_AccountWideOperation_ReportsNoFolderAlias()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");

        // Act
        var failure = new MailboxUnavailableException(accountId, new InvalidOperationException("pipeline rejection"));

        // Assert
        Assert.Equal(accountId, failure.AccountId);
        Assert.Null(failure.FolderAlias);
    }

    [Fact]
    public void MailboxUnavailableException_FolderOperation_NamesTheAliasRatherThanTheRemotePath()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var alias = MailFolderAlias.Create("INBOX");

        // Act
        var failure = new MailboxUnavailableException(accountId, alias, new InvalidOperationException("pipeline rejection"));

        // Assert
        Assert.Equal(alias, failure.FolderAlias);
        Assert.Contains("primary/INBOX", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ErrorCode_MailboxFolderRecreated_IsTheCodeAndKeepsBothObservedUidValidityValues()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var alias = MailFolderAlias.Create("INBOX");
        var sessionUidValidity = ImapUidValidity.Create(5);
        var reselectedUidValidity = ImapUidValidity.Create(9);

        // Act
        var failure = new MailboxFolderRecreatedException(accountId, alias, sessionUidValidity, reselectedUidValidity);

        // Assert
        Assert.Equal(MailMcpErrorCode.MailboxFolderRecreated, failure.ErrorCode);
        Assert.Equal(sessionUidValidity, failure.SessionUidValidity);
        Assert.Equal(reselectedUidValidity, failure.ReselectedUidValidity);
    }

    /// <summary>The message names the configured alias, never the remote path, which can carry personal information.</summary>
    [Fact]
    public void MailboxFolderRecreatedException_Message_NamesTheAliasAndBothUidValidityValues()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var alias = MailFolderAlias.Create("Archive");

        // Act
        var failure = new MailboxFolderRecreatedException(
            accountId,
            alias,
            ImapUidValidity.Create(5),
            ImapUidValidity.Create(9));

        // Assert
        Assert.Contains($"{accountId.Value}/{alias.Value}", failure.Message, StringComparison.Ordinal);
        Assert.Contains("5", failure.Message, StringComparison.Ordinal);
        Assert.Contains("9", failure.Message, StringComparison.Ordinal);
    }

    /// <summary>An inner exception is diagnostic detail for a log; copying its text would put a provider payload into an operator-facing message.</summary>
    [Fact]
    public void MailboxUnavailableException_Message_DoesNotRepeatTheInnerFailureText()
    {
        // Arrange
        var accountId = MailAccountId.Create("primary");
        var rejection = new InvalidOperationException("host mail.example.test rejected user@example.test");

        // Act
        var failure = new MailboxUnavailableException(accountId, MailFolderAlias.Create("INBOX"), rejection);

        // Assert
        Assert.DoesNotContain(rejection.Message, failure.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("mail.example.test", failure.Message, StringComparison.Ordinal);
        Assert.Same(rejection, failure.InnerException);
    }

    [Fact]
    public void RemoteEmailContentFetchResult_Retrieved_CarriesTheContent()
    {
        // Arrange
        var occurrenceId = EmailOccurrenceId.Create(
            MailAccountId.Create("primary"),
            new MailFolderResolutionId(MailFolderAlias.Create("INBOX"), MailFolderResolutionGeneration.First),
            ImapUidValidity.Create(5),
            ImapUid.Create(10));
        var content = new RemoteEmailContent(occurrenceId, new ReadOnlyMemory<byte>([1, 2, 3]));

        // Act
        var result = RemoteEmailContentFetchResult.Retrieved(content);

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.Retrieved, result.Outcome);
        Assert.Same(content, result.Content);
    }

    [Fact]
    public void RemoteEmailContentFetchResult_ExceededSizeLimit_CarriesNoContent()
    {
        // Act
        var result = RemoteEmailContentFetchResult.ExceededSizeLimit();

        // Assert
        Assert.Equal(RemoteEmailContentFetchOutcome.ExceededSizeLimit, result.Outcome);
        Assert.Null(result.Content);
    }

    [Fact]
    public void RemoteEmailContentFetchResult_RetrievedWithoutContent_IsRejected()
    {
        // Act, Assert
        Assert.Throws<ArgumentNullException>(() => RemoteEmailContentFetchResult.Retrieved(null!));
    }
}

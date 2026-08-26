// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Accounts;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Synchronization;
using MailFathom.Host.Api;
using MailFathom.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using Xunit;

namespace MailFathom.Host.UnitTests.Api;

/// <summary>Covers the one guard every administrative endpoint puts in front of the account a request names.</summary>
/// <remarks>
/// The refusals are the contract here, not an implementation detail: eight endpoints answer a caller who named the
/// wrong account, and what they say is now decided in one place. The wording is asserted rather than described, because
/// the copies this type replaced had already drifted apart in what they echoed back to the caller.
/// </remarks>
public sealed class AdminAccountRequestTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");

    /// <summary>The account a resolution answers with, which names the owner as well as the identifier.</summary>
    private static readonly MailAccountIdentity WorkIdentity =
        MailAccountIdentity.Create(SyntheticMailOwner.Deployment, Work);

    /// <summary>The ordinary case: a name this deployment serves resolves to its identifier.</summary>
    [Fact]
    public void Resolve_AnAccountTheDeploymentServes_ReadsTheIdentifier()
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var accountId = AdminAccountRequest.Resolve("work", accounts);

        // Assert
        Assert.Equal(WorkIdentity, accountId);
    }

    /// <summary>Whitespace around the name is not part of it, so a padded name reaches the same account.</summary>
    [Fact]
    public void Resolve_AnAccountNamedWithSurroundingWhitespace_ReadsTheSameIdentifier()
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var accountId = AdminAccountRequest.Resolve("  work  ", accounts);

        // Assert
        Assert.Equal(WorkIdentity, accountId);
    }

    /// <summary>An absent, empty, or blank name resolves to nothing rather than to a refusal of its own.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_NoAccountNamed_ReadsNothing(string? account)
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var accountId = AdminAccountRequest.Resolve(account, accounts);

        // Assert
        Assert.Null(accountId);
    }

    /// <summary>A name this deployment does not configure resolves to nothing, exactly as an absent one does.</summary>
    [Fact]
    public void Resolve_AnAccountTheDeploymentDoesNotServe_ReadsNothing()
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var accountId = AdminAccountRequest.Resolve("archive", accounts);

        // Assert
        Assert.Null(accountId);
    }

    /// <summary>A request that named no account is told so, without an empty name echoed into the sentence.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Refuse_NoAccountNamed_StatesThatTheRequestNamedNone(string? account)
    {
        // Act
        var refusal = AdminAccountRequest.Refuse(account);

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Equal("The request named no mail account.", Detail(refusal));
    }

    /// <summary>An account nobody configured is named back to the caller, so they can see what was looked up.</summary>
    [Fact]
    public void Refuse_AnAccountTheDeploymentDoesNotServe_NamesItBack()
    {
        // Act
        var refusal = AdminAccountRequest.Refuse("archive");

        // Assert
        Assert.Equal(StatusCodes.Status400BadRequest, refusal.StatusCode);
        Assert.Equal("This deployment configures no mail account named 'archive'.", Detail(refusal));
    }

    /// <summary>
    /// The direction the whitespace question was settled in. What the refusal echoes is the identifier that was looked
    /// up rather than the text the request carried, so a padded name and a bare one are answered with one sentence
    /// wherever they are sent — which two of the eight endpoints already did and six did not.
    /// </summary>
    [Fact]
    public void Refuse_AnAccountNamedWithSurroundingWhitespace_NamesTheIdentifierThatWasLookedUp()
    {
        // Act
        var padded = AdminAccountRequest.Refuse("  archive  ");
        var bare = AdminAccountRequest.Refuse("archive");

        // Assert
        Assert.Equal("This deployment configures no mail account named 'archive'.", Detail(padded));
        Assert.Equal(Detail(bare), Detail(padded));
    }

    /// <summary>An absent filter narrows nothing, which is the reading a caller asked for rather than a mistake.</summary>
    [Fact]
    public void TryResolveFilter_NoFilter_ReadsEveryAccount()
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var admitted = AdminAccountRequest.TryResolveFilter(null, accounts, out var accountId, out var refusal);

        // Assert
        Assert.True(admitted);
        Assert.Null(accountId);
        Assert.Null(refusal);
    }

    /// <summary>A filter naming a served account narrows the reading to it.</summary>
    [Fact]
    public void TryResolveFilter_AnAccountTheDeploymentServes_NarrowsToIt()
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var admitted = AdminAccountRequest.TryResolveFilter("  work  ", accounts, out var accountId, out var refusal);

        // Assert
        Assert.True(admitted);
        Assert.Equal(WorkIdentity, accountId);
        Assert.Null(refusal);
    }

    /// <summary>A filter present and empty is a mistake rather than every account, and the refusal names the remedy.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void TryResolveFilter_AnEmptyFilter_SaysToLeaveItOut(string account)
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var admitted = AdminAccountRequest.TryResolveFilter(account, accounts, out var accountId, out var refusal);

        // Assert
        Assert.False(admitted);
        Assert.Null(accountId);
        Assert.Equal(StatusCodes.Status400BadRequest, refusal!.StatusCode);
        Assert.Equal(
            "The account filter named no mail account. Leave it out to read every account.",
            Detail(refusal));
    }

    /// <summary>A filter naming an account nobody configured is refused in the same sentence a required one is.</summary>
    [Fact]
    public void TryResolveFilter_AnAccountTheDeploymentDoesNotServe_NamesItBack()
    {
        // Arrange
        var accounts = CatalogServing(Work);

        // Act
        var admitted = AdminAccountRequest.TryResolveFilter("  archive  ", accounts, out var accountId, out var refusal);

        // Assert
        Assert.False(admitted);
        Assert.Null(accountId);
        Assert.Equal("This deployment configures no mail account named 'archive'.", Detail(refusal!));
    }

    private static string? Detail(ProblemHttpResult refusal) => refusal.ProblemDetails.Detail;

    private static IDeploymentMailAccountCatalog CatalogServing(params MailAccountId[] accounts)
    {
        var catalog = Substitute.For<IDeploymentMailAccountCatalog>();
        catalog.ServedAccounts.Returns(
        [
            .. accounts.Select(account => new ServedMailAccount(
                SyntheticMailOwner.Deployment,
                account,
                MailAccountDisplayName.Create(account.Value),
                MailSynchronizationMode.Polling)),
        ]);

        return catalog;
    }
}

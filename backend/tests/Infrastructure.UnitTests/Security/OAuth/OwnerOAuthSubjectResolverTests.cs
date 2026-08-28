// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Access.Credentials;
using MailFathom.Domain.Access;
using MailFathom.Infrastructure.Security.OAuth;
using NSubstitute;
using Xunit;

namespace MailFathom.Infrastructure.UnitTests.Security.OAuth;

/// <summary>Covers the step between a validated token and the owner it acts for.</summary>
/// <remarks>
/// The token is already valid by the time this runs, so nothing here judges a signature. What it decides is whether the
/// subject that token names is one this deployment mapped onto an owner, which is an indexed read rather than a scan.
/// </remarks>
public sealed class OwnerOAuthSubjectResolverTests
{
    private const string Issuer = "https://login.example/";

    private const string Subject = "9f0a2c";

    private static readonly MailOwnerId Owner = MailOwnerId.Create(new Guid("0197c0de-0000-7000-8000-00000000ffff"));

    private static readonly Guid CredentialId = new("0197c0de-0000-7000-8000-000000000003");

    private static readonly IReadOnlyList<MailFathomPermission> Grant =
        [MailFathomPermission.MailRead, MailFathomPermission.MailAsk];

    [Fact]
    public async Task ResolveAsync_ASubjectTheDeploymentMapped_AdmitsTheOwnerWithTheGrantOnTheMapping()
    {
        // Arrange
        var harness = new ResolverHarness();
        harness.Maps(enabled: true);

        // Act
        var admitted = await harness.ResolveAsync(Issuer, Subject);

        // Assert
        Assert.NotNull(admitted);
        Assert.Equal(CredentialId, admitted.CredentialId);
        Assert.Equal(Owner, admitted.Owner);
        Assert.Equal(Grant, admitted.Permissions);
    }

    /// <summary>The issuer travels with the subject, so a second server's identically named subject is a different mapping.</summary>
    [Fact]
    public async Task ResolveAsync_TheSameSubjectFromAnotherIssuer_ResolvesNobody()
    {
        // Arrange
        var harness = new ResolverHarness();
        harness.Maps(enabled: true);

        // Act
        var admitted = await harness.ResolveAsync("https://other.example/", Subject);

        // Assert
        Assert.Null(admitted);
    }

    [Fact]
    public async Task ResolveAsync_ASubjectNobodyMapped_ResolvesNobody()
    {
        // Arrange
        var harness = new ResolverHarness();

        // Act
        var admitted = await harness.ResolveAsync(Issuer, Subject);

        // Assert
        Assert.Null(admitted);
    }

    /// <summary>Disabling a mapping is how a person is taken off a deployment without their authorization server changing.</summary>
    [Fact]
    public async Task ResolveAsync_ADisabledMapping_ResolvesNobody()
    {
        // Arrange
        var harness = new ResolverHarness();
        harness.Maps(enabled: false);

        // Act
        var admitted = await harness.ResolveAsync(Issuer, Subject);

        // Assert
        Assert.Null(admitted);
    }

    /// <summary>A pair that composes no lookup is answered without a query, because no stored value could equal it.</summary>
    [Theory]
    [InlineData(null, Subject)]
    [InlineData(Issuer, null)]
    [InlineData(Issuer, "  ")]
    [InlineData("https://login example/", Subject)]
    public async Task ResolveAsync_APairThatComposesNoLookup_ResolvesNobodyWithoutReadingACredential(
        string? issuer,
        string? subject)
    {
        // Arrange
        var harness = new ResolverHarness();

        // Act
        var admitted = await harness.ResolveAsync(issuer, subject);

        // Assert
        Assert.Null(admitted);

        await harness.Credentials.DidNotReceiveWithAnyArgs()
            .FindAsync(default, default, TestContext.Current.CancellationToken);
    }

    private sealed class ResolverHarness
    {
        internal ResolverHarness()
        {
            this.Credentials = Substitute.For<IOwnerCredentialStore>();
            this.Credentials.FindAsync(
                    Arg.Any<OwnerCredentialMethod>(),
                    Arg.Any<OwnerCredentialLookup>(),
                    Arg.Any<CancellationToken>())
                .Returns((ResolvedOwnerCredential?)null);

            this.Resolver = new OwnerOAuthSubjectResolver(this.Credentials);
        }

        internal OwnerOAuthSubjectResolver Resolver { get; }

        internal IOwnerCredentialStore Credentials { get; }

        internal void Maps(bool enabled)
        {
            Assert.True(OwnerCredentialLookup.TryCreateForOAuthSubject(Issuer, Subject, out var lookup));

            this.Credentials.FindAsync(OwnerCredentialMethod.OAuthSubject, lookup, Arg.Any<CancellationToken>())
                .Returns(new ResolvedOwnerCredential(
                    CredentialId,
                    Owner,
                    OwnerCredentialMethod.OAuthSubject,
                    Grant,
                    enabled,
                    Material: null));
        }

        internal Task<AdmittedOwnerCredential?> ResolveAsync(string? issuer, string? subject) =>
            this.Resolver.ResolveAsync(issuer, subject, TestContext.Current.CancellationToken);
    }
}

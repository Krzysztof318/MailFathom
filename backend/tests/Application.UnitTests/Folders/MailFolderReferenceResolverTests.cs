// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Application.Folders;
using MailFathom.Domain.Accounts;
using MailFathom.Domain.Folders;
using MailFathom.TestSupport;
using Xunit;

namespace MailFathom.Application.UnitTests.Folders;

/// <summary>Covers the one place an alias or a role becomes the folder of an account it means.</summary>
/// <remarks>
/// What matters here is that the two kinds of name fail differently and that the refusal a role produces is one
/// refusal: every caller that lets a folder be named comes through this, so a second reading anywhere would be a second
/// answer to the same question.
/// </remarks>
public sealed class MailFolderReferenceResolverTests
{
    private static readonly MailAccountId Work = MailAccountId.Create("work");
    private static readonly MailAccountId Private = MailAccountId.Create("private");

    [Fact]
    public void Resolve_ARoleTheAccountMapsByPath_AnswersWithThatFolder()
    {
        // Arrange
        var spam = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("spam"),
            RemoteFolderPath.Create("INBOX.Spam"),
            specialUse: MailFolderSpecialUse.Junk);
        var resolver = StubMailFolderMappings.Nothing.With(Work, spam).Resolver;

        // Act
        var resolved = resolver.Resolve(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Equal(spam, resolved);
        Assert.Equal(MailFolderAlias.Create("spam"), resolver.ResolveAlias(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk)));
    }

    /// <summary>A destination is exactly what an unmirrored folder is for, so what it takes part in decides nothing here.</summary>
    [Fact]
    public void Resolve_ARoleOnAFolderNothingMirrors_StillAnswersWithThatFolder()
    {
        // Arrange
        var quarantine = MailFolderMapping.ToRemotePath(
            MailFolderAlias.Create("quarantine"),
            RemoteFolderPath.Create("INBOX.Quarantine"),
            MailFolderParticipation.Create(isSynchronized: false, generatesEmbeddings: false, isVisibleToTools: false),
            specialUse: MailFolderSpecialUse.Junk);
        var resolver = StubMailFolderMappings.Nothing.With(Work, quarantine).Resolver;

        // Act
        var resolved = resolver.Resolve(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Equal(quarantine, resolved);
    }

    [Fact]
    public void Resolve_ARoleNoFolderOfTheAccountPlays_RefusesNamingTheRole()
    {
        // Arrange
        var resolver = StubMailFolderMappings.Nothing
            .With(Private, MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("junk"), MailFolderSpecialUse.Junk))
            .Resolver;

        // Act
        var failure = Assert.Throws<MailFolderRoleUnmappedException>(
            () => resolver.Resolve(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk)));

        // Assert
        Assert.Equal(MailFolderSpecialUse.Junk, failure.Role);
        Assert.Equal(Work, failure.AccountId);
        Assert.Contains("Junk", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_AnAliasTheAccountMaps_AnswersWithThatFolder()
    {
        // Arrange
        var archive = MailFolderMapping.ToSpecialUse(MailFolderAlias.Create("archive"), MailFolderSpecialUse.Archive);
        var resolver = StubMailFolderMappings.Nothing.With(Work, archive).Resolver;

        // Act
        var resolved = resolver.Resolve(Work, MailFolderReference.ToAlias(MailFolderAlias.Create("ARCHIVE")));

        // Assert
        Assert.Equal(archive, resolved);
    }

    /// <summary>An alias is already the folder's name, so one nothing maps selects nothing rather than being refused.</summary>
    [Fact]
    public void Resolve_AnAliasNothingMaps_AnswersWithNothingAndKeepsTheName()
    {
        // Arrange
        var resolver = StubMailFolderMappings.ResolvingNothing;
        var reference = MailFolderReference.ToAlias(MailFolderAlias.Create("gone"));

        // Act
        var resolved = resolver.Resolve(Work, reference);

        // Assert
        Assert.Null(resolved);
        Assert.Equal(MailFolderAlias.Create("gone"), resolver.ResolveAlias(Work, reference));
    }

    /// <summary>One account lacking the folder is an account contributing nothing, which a caller asking several of them needs.</summary>
    [Fact]
    public void TryResolve_ARoleNoFolderOfTheAccountPlays_AnswersWithNothingRatherThanRefusing()
    {
        // Arrange
        var resolver = StubMailFolderMappings.ResolvingNothing;

        // Act
        var resolved = resolver.TryResolve(Work, MailFolderReference.ToRole(MailFolderSpecialUse.Junk));

        // Assert
        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_TheUnspecifiedDefault_ThrowsArgumentException()
    {
        // Arrange
        var resolver = StubMailFolderMappings.ResolvingNothing;

        // Act, Assert
        Assert.Throws<ArgumentException>(() => resolver.Resolve(Work, default));
    }
}

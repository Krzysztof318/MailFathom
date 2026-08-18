// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the trailing-wildcard shorthand a grant may name a whole part of the vocabulary with.</summary>
/// <remarks>
/// What is asserted here is the syntax and the reach, because both are read from an operator's own configuration file:
/// which written values are a subtree at all, which published names each one reaches, and that the reach is computed
/// from the published set every time rather than fixed when the value was parsed.
/// </remarks>
public sealed class PermissionSubtreeTests
{
    /// <summary>The one shape the syntax has, and the reason the feature exists: one written value in place of a list an operator would otherwise revisit.</summary>
    [Fact]
    public void CoveredPermissions_ASurfaceSubtree_ReachesEveryPermissionBeneathThePrefix()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("mailfathom.admin.*", out var subtree));

        // Act
        var covered = subtree.CoveredPermissions();

        // Assert
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Administration), covered);
    }

    /// <summary>The prefix keeps its trailing dot, so a subtree stops at a segment boundary rather than reaching halfway into a name.</summary>
    [Fact]
    public void CoveredPermissions_ADeeperSubtree_ReachesOnlyWhatSitsBeneathThatSegment()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("mailfathom.mail.contacts.*", out var subtree));

        // Act
        var covered = subtree.CoveredPermissions();

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailContactsRead, MailFathomPermission.MailContactsWrite],
            covered);
    }

    /// <summary>A grant resolves into claims, a startup line, and a metadata document, all of which state the published order.</summary>
    [Fact]
    public void CoveredPermissions_ASubtreeSpanningSeveralNames_ReportsThemInThePublishedOrder()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("mailfathom.mail.*", out var subtree));

        // Act
        var covered = subtree.CoveredPermissions();

        // Assert
        Assert.Equal(MailFathomPermission.PublishedFor(ProtectedSurface.Mail), covered);
    }

    /// <summary>A prefix nothing sits beneath reaches nothing rather than everything, which is the direction a misspelling has to fail in.</summary>
    [Theory]
    [InlineData("mailfathom.post.*")]
    [InlineData("mailfathom.mail.read.*")]
    [InlineData("nothing.*")]
    public void CoveredPermissions_APrefixNothingIsPublishedBeneath_ReachesNothing(string written)
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse(written, out var subtree));

        // Act, Assert
        Assert.Empty(subtree.CoveredPermissions());
    }

    /// <summary>The reach is read from the published set on every call, which is what makes a subtree shorthand for the surface rather than for the names published when the file was written.</summary>
    [Theory]
    [InlineData("*")]
    [InlineData("mailfathom.*")]
    public void ReachesEveryPublishedPermission_AValueSpanningBothSurfaces_SaysSo(string written)
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse(written, out var subtree));

        // Act, Assert
        Assert.True(subtree.ReachesEveryPublishedPermission());
        Assert.Equal(MailFathomPermission.All, subtree.CoveredPermissions());
    }

    [Theory]
    [InlineData("mailfathom.mail.*")]
    [InlineData("mailfathom.admin.*")]
    [InlineData("mailfathom.admin.audit.*")]
    public void ReachesEveryPublishedPermission_ASubtreeOfOneSurface_SaysItDoesNot(string written)
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse(written, out var subtree));

        // Act, Assert
        Assert.False(subtree.ReachesEveryPublishedPermission());
    }

    /// <summary>
    /// The syntax is the trailing <c>.*</c> and nothing else. A value that only looks like a pattern has to fall
    /// through to the refusal an unpublished name draws, or an operator writing a partial segment would be told their
    /// pattern matched nothing rather than that they had not written one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".*")]
    [InlineData("mailfathom.mail.c*")]
    [InlineData("mailfathom.mail*")]
    [InlineData("mailfathom.*.read")]
    [InlineData("mailfathom.mail.read")]
    public void TryParse_AValueThatIsNotWrittenAsASubtree_ReportsUnspecified(string? written)
    {
        // Act
        var parsed = PermissionSubtree.TryParse(written, out var subtree);

        // Assert
        Assert.False(parsed);
        Assert.False(subtree.IsSpecified);
    }

    /// <summary>The struct default is reachable and names nothing, so it must refuse to answer rather than reading as the subtree that covers everything.</summary>
    [Fact]
    public void Default_NamesNoSubtree()
    {
        // Arrange
        var subtree = default(PermissionSubtree);

        // Act, Assert
        Assert.False(subtree.IsSpecified);
        Assert.Throws<InvalidOperationException>(() => subtree.Written);
        Assert.Throws<InvalidOperationException>(subtree.CoveredPermissions);
        Assert.Equal("(unspecified)", subtree.ToString());
    }

    /// <summary>A refusal quotes the value back, so it has to survive parsing exactly as the operator typed it.</summary>
    [Fact]
    public void Written_AParsedSubtree_IsWhatTheOperatorWrote()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("mailfathom.admin.*", out var subtree));

        // Act, Assert
        Assert.Equal("mailfathom.admin.*", subtree.Written);
        Assert.Equal("mailfathom.admin.*", subtree.ToString());
    }
}

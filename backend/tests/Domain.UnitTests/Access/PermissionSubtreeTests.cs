// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Domain.Access;
using Xunit;

namespace MailFathom.Domain.UnitTests.Access;

/// <summary>Covers the wildcard shorthand a grant may name a whole part of the vocabulary with.</summary>
/// <remarks>
/// What is asserted here is the syntax and the reach, because both are read from an operator's own configuration file:
/// which written values are a subtree at all, which published names each one reaches, and that the reach is computed
/// from the published set every time rather than fixed when the value was parsed. A wildcard stands for one or more
/// whole segments at whatever position it is written, so the trailing form is asserted as one case of that rule rather
/// than as a shape of its own.
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

    /// <summary>A wildcard is matched as a whole segment, so a subtree stops at a segment boundary rather than reaching halfway into a name.</summary>
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

    /// <summary>The reason the wildcard was widened: the reading half of a surface sits at two depths, and no trailing pattern reaches both.</summary>
    [Fact]
    public void CoveredPermissions_AWildcardBeforeTheLastSegment_ReachesEveryDepthBeneathIt()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("mailfathom.mail.*.read", out var subtree));

        // Act
        var covered = subtree.CoveredPermissions();

        // Assert
        Assert.Equal([MailFathomPermission.MailContactsRead], covered);
    }

    /// <summary>A wildcard stands for one or more segments, so one written value names what sits directly beneath a prefix and what sits below that.</summary>
    [Fact]
    public void CoveredPermissions_AWildcardSpanningBothDepths_ReachesTheNamesOfBothSurfaces()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("mailfathom.*.read", out var subtree));

        // Act
        var covered = subtree.CoveredPermissions();

        // Assert
        Assert.Equal(
            [
                MailFathomPermission.MailRead,
                MailFathomPermission.MailContactsRead,
                MailFathomPermission.AdminRead,
                MailFathomPermission.AdminAuditRead,
            ],
            covered);
    }

    /// <summary>Nothing about the position is special, so a value may carry more than one wildcard and each stands for the same thing.</summary>
    [Fact]
    public void CoveredPermissions_SeveralWildcards_EachStandsForOneOrMoreSegments()
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse("*.contacts.*", out var subtree));

        // Act
        var covered = subtree.CoveredPermissions();

        // Assert
        Assert.Equal(
            [MailFathomPermission.MailContactsRead, MailFathomPermission.MailContactsWrite],
            covered);
    }

    /// <summary>A wildcard stands for at least one segment, so a pattern never reaches the name it was written around.</summary>
    [Theory]
    [InlineData("mailfathom.mail.read.*")]
    [InlineData("mailfathom.*.mail.read")]
    [InlineData("mailfathom.mail.read.*.read")]
    public void CoveredPermissions_AWildcardStandingForNothing_ReachesNothing(string written)
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse(written, out var subtree));

        // Act, Assert
        Assert.Empty(subtree.CoveredPermissions());
    }

    /// <summary>A prefix nothing sits beneath reaches nothing rather than everything, which is the direction a misspelling has to fail in.</summary>
    [Theory]
    [InlineData("mailfathom.post.*")]
    [InlineData("mailfathom.mail.read.*")]
    [InlineData("nothing.*")]
    [InlineData("mailfathom.*.delete")]
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
    [InlineData("mailfathom.*.read")]
    public void ReachesEveryPublishedPermission_ASubtreeShortOfTheWholeVocabulary_SaysItDoesNot(string written)
    {
        // Arrange
        Assert.True(PermissionSubtree.TryParse(written, out var subtree));

        // Act, Assert
        Assert.False(subtree.ReachesEveryPublishedPermission());
    }

    /// <summary>
    /// A wildcard is a whole segment or it is not a wildcard. A value that only looks like a pattern has to fall
    /// through to the refusal an unpublished name draws, or an operator writing a partial segment would be told their
    /// pattern matched nothing rather than that they had not written one.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(".*")]
    [InlineData("mailfathom..*")]
    [InlineData("mailfathom.mail.c*")]
    [InlineData("mailfathom.mail*")]
    [InlineData("mailfathom.*read")]
    [InlineData("*mailfathom.read")]
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

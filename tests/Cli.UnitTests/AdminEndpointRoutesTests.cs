// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Cli.Administration;
using Xunit;

namespace MailFathom.Cli.UnitTests;

/// <summary>Covers the paths the command assumes a deployment answers at.</summary>
/// <remarks>
/// These are the command's half of an agreement with a service it cannot reference: the deployment declares the same
/// prefix in <c>AdminEndpointOptions.RoutePrefix</c>, and its own suite pins that constant to the same literal. Two
/// assertions against one written-out path is what an agreement across an assembly boundary looks like — the
/// alternative is a rename on one side that compiles cleanly and leaves every sign-in reaching a 404.
/// </remarks>
public sealed class AdminEndpointRoutesTests
{
    /// <summary>The literal the deployment's own tests pin its route prefix to.</summary>
    [Fact]
    public void Prefix_IsTheOneTheDeploymentServesItsRoutesBeneath() =>
        Assert.Equal("/api/admin", AdminEndpointRoutes.Prefix);

    [Fact]
    public void SessionPath_IsTheRouteThatReportsWhoTheCallerIs() =>
        Assert.Equal("/api/admin/session", AdminEndpointRoutes.SessionPath);

    /// <summary>
    /// The route the status command reads, pinned as a literal for the reason every other path here is: the deployment's
    /// own suite pins the same one, and a rename on either side would compile cleanly and leave the command reaching a
    /// 404 that reads exactly like an administrative endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void MailboxSynchronizationPath_IsTheRouteTheDeploymentReportsItsRunsAt() =>
        Assert.Equal("/api/admin/mailbox/synchronization", AdminEndpointRoutes.MailboxSynchronizationPath);

    /// <summary>
    /// The three embedding paths, pinned as literals for the reason the prefix is: the deployment's own suite pins the
    /// same three, and a rename on either side would otherwise compile cleanly and leave every embedding command
    /// reaching a 404 that reads exactly like an administrative endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void EmbeddingPaths_AreTheRoutesTheDeploymentServesThemAt()
    {
        Assert.Equal("/api/admin/embeddings", AdminEndpointRoutes.EmbeddingStatusPath);
        Assert.Equal("/api/admin/embeddings/activation", AdminEndpointRoutes.EmbeddingActivationPath);
        Assert.Equal(
            "/api/admin/embeddings/reindex/cancellation",
            AdminEndpointRoutes.EmbeddingReindexCancellationPath);
    }

    /// <summary>
    /// The route the one operation that disposes of mail is asked for on, pinned for the reason every other path here
    /// is: the deployment's own suite pins the same literal, and a rename on either side would compile cleanly and
    /// leave the erase command reaching a 404 that reads exactly like an endpoint nobody enabled.
    /// </summary>
    [Fact]
    public void FolderErasurePath_IsTheRouteTheDeploymentErasesAFolderAt() =>
        Assert.Equal("/api/admin/folders/erasure", AdminEndpointRoutes.FolderErasurePath);

    /// <summary>
    /// The two routes that bring stored mail up to a newer release's properties, pinned for the reason every other path
    /// here is. They sit beneath the mailbox segment rather than under the folder one because both act on an account
    /// and merely narrow to a folder.
    /// </summary>
    [Fact]
    public void MaintenancePaths_AreTheRoutesTheDeploymentRefreshesStoredMailAt()
    {
        Assert.Equal("/api/admin/mailbox/rewind", AdminEndpointRoutes.MailboxRewindPath);
        Assert.Equal("/api/admin/mailbox/rederivation", AdminEndpointRoutes.MailboxRederivationPath);
    }

    /// <summary>
    /// RFC 9728 places the document under a well-known segment with the resource's path appended, and the deployment
    /// refuses to start unless its resource path is the route prefix. Composing it here rather than reading it from a
    /// challenge is what makes a sign-in one request instead of two, and this is the assertion that keeps the
    /// composition honest.
    /// </summary>
    [Fact]
    public void ProtectedResourceMetadataPath_PlacesTheDocumentWhereRfc9728Does() =>
        Assert.Equal(
            "/.well-known/oauth-protected-resource/api/admin",
            AdminEndpointRoutes.ProtectedResourceMetadataPath);
}

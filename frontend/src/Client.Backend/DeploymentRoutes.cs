// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Globalization;

namespace MailFathom.Client.Backend;

/// <summary>Where a deployment answers, relative to the address the composing host supplied.</summary>
/// <remarks>
/// <para>
/// The client is configured with an address and appends the rest, so these paths are the whole of what it assumes
/// about the other side. Stated together rather than beside each caller, because they are one agreement with the
/// service: the deployment publishes the client surface beneath the prefix and derives its resource identifier from
/// that same prefix, which is what puts the metadata document exactly where RFC 9728 says to look for it.
/// </para>
/// <para>
/// The same agreement is stated at the other end, in <c>backend/src/Cli/Administration/AdminEndpointRoutes.cs</c> for
/// the administrative surface. Two lists that happen to share a shape are not duplication — they are one contract
/// stated at each end, and sharing a constant across the two stacks is exactly the compile-time coupling
/// <c>frontend/src/AGENTS.md</c> refuses.
/// </para>
/// </remarks>
internal static class DeploymentRoutes
{
    /// <summary>The prefix every client route is served beneath.</summary>
    internal const string Prefix = "/api/client";

    /// <summary>Where a deployment reports what a presented credential makes the caller.</summary>
    internal const string SessionPath = $"{Prefix}/session";

    /// <summary>Where a deployment reports the signed-in owner's mail accounts and how current each one's copy is.</summary>
    internal const string MailAccountsPath = $"{Prefix}/accounts";

    /// <summary>Where a deployment reports the owner's mailboxes and every folder in them, which is the one tree a screen is drawn from.</summary>
    /// <remarks>
    /// Its own route rather than a wider version of the one above, because counting a folder's mail is work
    /// proportional to the mail: a client asking whether a mailbox is reachable reads the accounts, and a client
    /// drawing a tree reads this.
    /// </remarks>
    internal const string MailFoldersPath = $"{Prefix}/folders";

    /// <summary>Where a deployment serves one page of the owner's message list, keyset-paged in either direction.</summary>
    /// <remarks>
    /// The route a mail screen spends its time in. It is asked with a query string composed from
    /// <see cref="Timeline.MailTimelineQuery" /> rather than with a path of its own, because everything that narrows a
    /// list is a filter over the same walk rather than a different resource.
    /// </remarks>
    internal const string MailTimelinePath = $"{Prefix}/emails";

    /// <summary>Where a deployment serves one message's body, as the two renderings a reading pane draws it from.</summary>
    /// <param name="storedEmailId">The message, as a list row or a conversation published it.</param>
    /// <param name="remoteImages">Whether the reader has asked for this message's remote pictures.</param>
    /// <returns>The path, relative to the address the composing host supplied.</returns>
    /// <remarks>
    /// The override is a query rather than anything either end keeps, which is the whole of how it is not remembered:
    /// the request carries it, the answer is drawn, and opening the message again asks again. The parameter is written
    /// only when it is true, so an ordinary read is the plain path.
    /// </remarks>
    internal static string MailBodyPath(Guid storedEmailId, bool remoteImages) => string.Create(
        CultureInfo.InvariantCulture,
        $"{Prefix}/messages/{storedEmailId:D}/body{(remoteImages ? "?remoteImages=true" : string.Empty)}");

    /// <summary>Where a deployment publishes what a client must obtain before it holds any credential.</summary>
    internal const string ProtectedResourceMetadataPath = $"/.well-known/oauth-protected-resource{Prefix}";
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Infrastructure.Mail.OAuth;

/// <summary>Supplies the access token an account's OAuth SASL mechanism presents to its mail server.</summary>
/// <remarks>
/// <para>
/// The port sits beside <see cref="IImapAccountSettingsProvider" /> rather than in <c>Application</c>, for the reason
/// that port does: a use case never opens a mail connection, so a contract about how one authenticates has no consumer
/// above this boundary. What the architecture actually requires — that no identity library type reaches
/// <c>Application</c> or <c>Domain</c> — holds either way, and holds trivially here because the implementations speak
/// RFC 6749 over <see cref="HttpClient" /> and add no identity library at all.
/// </para>
/// <para>
/// Two methods rather than one, because a caller has two genuinely different questions. Establishing a connection asks
/// for a usable token and does not care whether it was cached. A server rejecting a token that this process still
/// believes is valid — revoked, or its mailbox password changed — asks for a replacement, and answering that from the
/// cache would retry the same rejected value until the attempt budget ran out.
/// </para>
/// </remarks>
internal interface IMailAccessTokenSource
{
    /// <summary>Gets a token that is valid now, issuing a new one only when the cached token is missing or due for refresh.</summary>
    /// <param name="accountId">The normalized local account identifier.</param>
    /// <param name="cancellationToken">Cancels the token request.</param>
    /// <returns>A token usable for the connection attempt that asked for it.</returns>
    /// <exception cref="MailAccessTokenUnavailableException">Thrown when the authorization server refused or could not be reached.</exception>
    Task<MailAccessToken> GetAccessTokenAsync(string accountId, CancellationToken cancellationToken);

    /// <summary>Replaces a token a mail server rejected, unless another caller has already replaced it.</summary>
    /// <param name="accountId">The normalized local account identifier.</param>
    /// <param name="rejectedToken">The token the mail server refused, which is never handed back.</param>
    /// <param name="cancellationToken">Cancels the token request.</param>
    /// <returns>A token that is not the rejected one.</returns>
    /// <exception cref="MailAccessTokenUnavailableException">Thrown when the authorization server refused or could not be reached.</exception>
    /// <remarks>
    /// Called after a mail server rejected a token this process considered valid, which is the one case the expiry
    /// instant cannot predict. The rejected token is named so that a burst of connections failing together over one
    /// revoked credential collapses into a single replacement rather than one request each.
    /// </remarks>
    Task<MailAccessToken> RenewAccessTokenAsync(
        string accountId,
        MailAccessToken rejectedToken,
        CancellationToken cancellationToken);
}

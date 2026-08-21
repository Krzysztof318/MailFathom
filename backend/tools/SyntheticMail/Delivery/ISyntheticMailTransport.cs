// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MimeKit;

namespace MailFathom.SyntheticMail.Delivery;

/// <summary>What a batch submits through, narrowed to the two things a batch does.</summary>
/// <remarks>
/// <para>
/// This exists so the pacing, the per-message failure handling, and the report can be exercised without a mail server,
/// which is the one part of delivery a unit test can reach at all. It is deliberately not a restatement of MailKit's
/// own interface: it names an account's whole submission session, opens it once, and translates the library's failures
/// into <see cref="SyntheticMailFailure" /> so nothing above it has to know which library refused.
/// </para>
/// <para>
/// A failure from <see cref="SendAsync" /> is about one message and the batch continues past it. A failure from
/// <see cref="OpenAsync" /> is about the run.
/// </para>
/// </remarks>
internal interface ISyntheticMailTransport : IAsyncDisposable
{
    /// <summary>Opens the session and authenticates it over a secured connection.</summary>
    /// <param name="cancellationToken">Cancels the connection and the authentication.</param>
    /// <returns>A task that completes once the session is ready to submit.</returns>
    /// <exception cref="SyntheticMailFailure">Thrown when the endpoint cannot be reached, cannot be secured, or refuses the credential.</exception>
    Task OpenAsync(CancellationToken cancellationToken);

    /// <summary>Submits one message to one recipient.</summary>
    /// <param name="message">The message to submit.</param>
    /// <param name="recipient">The one envelope recipient, which is the only real address a run touches.</param>
    /// <param name="cancellationToken">Cancels the submission.</param>
    /// <returns>A task that completes once the server has accepted the message.</returns>
    /// <exception cref="SyntheticMailFailure">Thrown when the server refuses this message.</exception>
    Task SendAsync(MimeMessage message, MailboxAddress recipient, CancellationToken cancellationToken);
}

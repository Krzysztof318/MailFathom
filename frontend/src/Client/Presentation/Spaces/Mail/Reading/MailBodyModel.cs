// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using MailFathom.Client.Backend;
using Microsoft.Extensions.Localization;

namespace MailFathom.Client.Presentation.Spaces.Mail.Reading;

/// <summary>What message the reading pane is showing, and whether this showing of it asked for remote pictures.</summary>
/// <param name="Message">The message, or <see langword="null" /> where nothing is open.</param>
/// <param name="RemoteImages">Whether the reader has asked for this message's remote pictures.</param>
/// <remarks>
/// One value rather than two states, and that is the whole of how the override is not remembered: the message and the
/// answer about its remote content are the same value, so opening another message replaces both at once and no code
/// path exists in which an allowance survives the message it was given for.
/// </remarks>
public sealed record MailBodyRequest(Guid? Message, bool RemoteImages)
{
    /// <summary>Nothing open, which is what the pane starts at.</summary>
    public static MailBodyRequest Nothing { get; } = new(Message: null, RemoteImages: false);
}

/// <summary>The model behind the reading pane: one message's body, read from the local copy the deployment holds.</summary>
/// <remarks>
/// <para>
/// Reading a body reaches no mail server, so nothing here can wait on IMAP or set the remote <c>\Seen</c> flag. What it
/// reads is what the deployment already has, through the route that serves both renderings at once — the document the
/// pane draws and the words it falls back to — so a refused document never costs a second request.
/// </para>
/// <para>
/// Asking for remote pictures is a second read of the same message rather than a setting, and neither end keeps the
/// answer. That is deliberate: a remembered allowance is a standing consent that outlives the reason it was given, and
/// the reader's own act is what should decide, message by message, whether a sender's server is told they opened it.
/// </para>
/// </remarks>
public partial record MailBodyModel
{
    private readonly DeploymentClient deployment;
    private readonly IStringLocalizer words;

    /// <summary>Initializes the pane over the deployment it reads from.</summary>
    /// <param name="deployment">Where a message's body is read from.</param>
    /// <param name="words">Where the sentences the pane composes come from.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deployment" /> or <paramref name="words" /> is <see langword="null" />.</exception>
    public MailBodyModel(DeploymentClient deployment, IStringLocalizer words)
    {
        ArgumentNullException.ThrowIfNull(deployment);
        ArgumentNullException.ThrowIfNull(words);

        this.deployment = deployment;
        this.words = words;
    }

    /// <summary>Gets the body as the pane draws it, re-read whenever what is asked for changes.</summary>
    /// <remarks>
    /// A feed rather than an awaited call, so the pane renders the wait and the failure from the same source it renders
    /// the message from, and a reader watching a slow deployment sees it working rather than a blank column.
    /// </remarks>
    public IFeed<MailBodyReading> Body => this.Asked.SelectAsync(this.ReadAsync);

    /// <summary>Gets what is open, which is the message and the answer about its remote content at once.</summary>
    private IState<MailBodyRequest> Asked => State.Value(this, () => MailBodyRequest.Nothing);

    /// <summary>Opens a message, on the terms every message is opened on: without its remote content.</summary>
    /// <param name="storedEmailId">The message to read.</param>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the pane has been pointed at the message.</returns>
    public async ValueTask Open(Guid storedEmailId, CancellationToken cancellationToken) =>
        await this.Asked
            .UpdateAsync(_ => new MailBodyRequest(storedEmailId, RemoteImages: false), cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Closes whatever is open, leaving the pane with nothing in it.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the pane is empty.</returns>
    public async ValueTask Close(CancellationToken cancellationToken) =>
        await this.Asked.UpdateAsync(_ => MailBodyRequest.Nothing, cancellationToken).ConfigureAwait(false);

    /// <summary>Reads the open message again, this time fetching what it asks for from somebody else's server.</summary>
    /// <param name="cancellationToken">Abandons the change.</param>
    /// <returns>A task completing once the second read has been asked for.</returns>
    /// <remarks>
    /// The reader's act and nothing else, taken for the message in front of them: it is not written down, not carried
    /// to the next message, and not carried to this one the next time it is opened.
    /// </remarks>
    public async ValueTask ShowRemoteContent(CancellationToken cancellationToken) =>
        await this.Asked
            .UpdateAsync(
                asked => asked is { Message: not null } ? asked with { RemoteImages = true } : asked,
                cancellationToken)
            .ConfigureAwait(false);

    private async ValueTask<MailBodyReading> ReadAsync(MailBodyRequest asked, CancellationToken cancellationToken)
    {
        if (asked.Message is not { } message)
        {
            return MailBodyReading.Nothing(this.words);
        }

        var body = await this.deployment
            .ReadMailBodyAsync(message, asked.RemoteImages, cancellationToken)
            .ConfigureAwait(false);

        return MailBodyReading.Of(body, this.words);
    }
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Collections.Immutable;

namespace MailFathom.Client.Presentation.Threads;

/// <summary>The conversation as its header is drawn, with every sentence about it already in words.</summary>
/// <remarks>
/// <para>
/// The header is answered about the whole conversation rather than about the messages that have arrived, which is what
/// the deployment publishes the participants and the count for: everything here stays true while the conversation is
/// paged, and a screen that derived it would be reading a thread in order to say who is in it.
/// </para>
/// <para>
/// Everything it carries is mail, so none of it is logged, written to local storage, or put in a telemetry event.
/// </para>
/// </remarks>
public sealed record ThreadReading
{
    private ThreadReading()
    {
        this.IsClosed = true;
        this.Subject = string.Empty;
        this.MessageCount = string.Empty;
        this.Announcement = string.Empty;
        this.Participants = [];
    }

    /// <summary>No conversation open, which is what the screen shows before a message is selected.</summary>
    public static ThreadReading Nothing { get; } = new();

    /// <summary>Gets whether a conversation is open at all, as against a screen nothing has been opened in.</summary>
    public bool IsOpen { get; private init; }

    /// <summary>Gets whether nothing is open, which is what the screen shows before a message is selected.</summary>
    /// <remarks>
    /// Stated as its own affirmative rather than read as the absence of the one above, because both sides of the
    /// decision are drawn and neither may be drawn before the answer arrives: a header carrying no value yet reaches a
    /// binding as nothing at all, and a screen that showed <em>nothing is open</em> on that would announce an empty
    /// conversation while the one somebody asked for was still on its way.
    /// </remarks>
    public bool IsClosed { get; private init; }

    /// <summary>Gets what the conversation is about, taken from the message that began it.</summary>
    public string Subject { get; private init; }

    /// <summary>Gets how many messages the conversation holds, as the reader's own language writes a sentence about it.</summary>
    public string MessageCount { get; private init; }

    /// <summary>Gets the conversation stated once, which is what a screen reader is given for the header.</summary>
    public string Announcement { get; private init; }

    /// <summary>Gets everybody the deployment named as having written in the conversation.</summary>
    public IImmutableList<ThreadParticipantRow> Participants { get; private init; }

    /// <summary>Gets whether the conversation has authors the header does not name.</summary>
    public bool HasUnnamedParticipants { get; private init; }

    /// <summary>Gets whether the conversation runs past what one read of it assembles at all.</summary>
    /// <remarks>
    /// Said rather than hidden, and said as its own sentence rather than folded into the count: a conversation that
    /// large is a mailing list's archive rather than correspondence somebody is following, and a screen that silently
    /// showed the first five hundred of it would be claiming to show the whole.
    /// </remarks>
    public bool RunsPastAssembly { get; private init; }

    /// <summary>Reads a conversation into the header the screen draws.</summary>
    /// <param name="subject">What the conversation is about.</param>
    /// <param name="messageCount">How many messages it holds, already in words.</param>
    /// <param name="announcement">The whole header as one sentence.</param>
    /// <param name="participants">Everybody the deployment named as having written in it.</param>
    /// <param name="hasUnnamedParticipants">Whether it has authors the participants do not name.</param>
    /// <param name="runsPastAssembly">Whether it runs past what one read assembles.</param>
    /// <returns>The header.</returns>
    /// <exception cref="ArgumentNullException">Thrown when an argument is <see langword="null" />.</exception>
    public static ThreadReading Of(
        string subject,
        string messageCount,
        string announcement,
        IImmutableList<ThreadParticipantRow> participants,
        bool hasUnnamedParticipants,
        bool runsPastAssembly)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(messageCount);
        ArgumentNullException.ThrowIfNull(announcement);
        ArgumentNullException.ThrowIfNull(participants);

        return new ThreadReading
        {
            IsOpen = true,
            IsClosed = false,
            Subject = subject,
            MessageCount = messageCount,
            Announcement = announcement,
            Participants = participants,
            HasUnnamedParticipants = hasUnnamedParticipants,
            RunsPastAssembly = runsPastAssembly,
        };
    }
}

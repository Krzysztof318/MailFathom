// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Preferences;

/// <summary>What one person set about their own client, which the deployment holds so it follows them between machines.</summary>
/// <param name="TelemetryEnabled">Whether this deployment may be told what the person's client is doing.</param>
/// <param name="Theme">What the client is painted in once a session exists.</param>
/// <param name="OpenMailInTabs">Whether opening a message opens a tab rather than replacing what is on the screen.</param>
/// <param name="MarkReadOnOpen">Whether opening a message in the client marks it read on the user's own mail server.</param>
/// <param name="ExpandWholeThread">Whether opening a conversation draws every message in it rather than the one it was opened at.</param>
/// <param name="MessageView">Which of the three renderings an open message is drawn on.</param>
/// <param name="AiFiltersShown">Whether the folder tree carries the standing views of what a derivation read in the mail.</param>
/// <param name="NotificationSeconds">How long one of the client's own notifications stands before it takes itself away.</param>
/// <remarks>
/// <para>
/// A closed set of eight rather than a settings service. Each of them says how somebody wants to work rather than what
/// the screen in front of them is like, which is why they belong to the person and not to the browser profile or the
/// desktop install they happened to set them in — and why a ninth is added when there is a ninth to add.
/// </para>
/// <para>
/// Marking read is here rather than on the mail account for the reason
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md">ADR 0026</see>
/// gives: read state is what must not fragment between the machines one person reads on, so it covers every account
/// that user reads and is not an operator's key.
/// </para>
/// <para>
/// It is deliberately not part of the user record. That document is configuration, binds strictly against the rules a
/// configuration file does, and is written under a grant that decides which mailboxes this deployment reads; none of
/// those has anything to do with whether a person may turn telemetry off or choose a theme.
/// </para>
/// <para>
/// Nothing here is personal data about a third party and nothing here is mail. What it does carry is a decision about
/// what may be said about this person, which is why the switch is theirs to set under the grant they already hold
/// rather than under one an administrator maintains for them.
/// </para>
/// </remarks>
public sealed record ClientPreferences(
    bool TelemetryEnabled,
    ClientThemeChoice Theme,
    bool OpenMailInTabs,
    bool MarkReadOnOpen,
    bool ExpandWholeThread,
    ClientMessageView MessageView,
    bool AiFiltersShown,
    int NotificationSeconds)
{
    /// <summary>The shortest a notification may be asked to stand for.</summary>
    /// <remarks>A second is long enough to read a title and reach the control on it, and shorter than that is a notification nobody can act on rather than a preference.</remarks>
    public const int ShortestNotificationSeconds = 1;

    /// <summary>The longest a notification may be asked to stand for.</summary>
    /// <remarks>
    /// Half a minute is past the point where a card in the corner is being read and into the point where it is in the
    /// way, and a permanent delete waits it out, with a short grace behind it, before it reaches the mail server — so a
    /// bound above this would be a person asking their own mailbox to hold still for as long as they liked.
    /// </remarks>
    public const int LongestNotificationSeconds = 30;

    /// <summary>Gets whether a stated notification time is one this deployment accepts.</summary>
    /// <param name="seconds">The whole seconds a notification was asked to stand for.</param>
    /// <returns><see langword="true" /> when the value is within the bound, both ends included.</returns>
    /// <remarks>Asked at the boundary and answered with a refusal rather than clamped, because a client told its value was stored and given a different one is a screen that disagrees with the deployment about what somebody chose.</remarks>
    public static bool IsUsableNotificationTime(int seconds) =>
        seconds is >= ShortestNotificationSeconds and <= LongestNotificationSeconds;

    /// <summary>Gets what a person who has set nothing is answered with.</summary>
    /// <remarks>
    /// Telemetry on, because the switch withdraws a default this deployment already applies rather than granting one,
    /// and a stored answer that has never been written is not a refusal. The theme follows the machine, which is what
    /// the client resolves on the device before there is a session to read this at all. Tabs are off, because a person
    /// who has not asked for them is reading one message at a time. Marking read is on, because every mail client the
    /// user already uses does it and a client that leaves their read state behind is one they keep another beside.
    /// A conversation opens at the message it was opened at, because that is the message somebody came for and the
    /// history behind it is one control away. A message is read as the reduced text, because that is what this client
    /// has always drawn and the sender's own markup is a surface somebody asks for rather than one they are handed.
    /// The standing views are drawn, because they are a section of the tree somebody turns off rather than one they
    /// go looking for, and a deployment deriving nothing answers each of them as a list with nothing in it.
    /// A notification stands for five seconds, which is what the client's own toast has always stood for — so a
    /// deployment nobody has asked behaves as it did before this preference existed.
    /// </remarks>
    public static ClientPreferences Unset { get; } =
        new(true, ClientThemeChoice.System, false, true, false, ClientMessageView.Reduced, true, 5);
}

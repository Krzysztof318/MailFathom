// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Preferences;

/// <summary>What one person set about their own client, which the deployment holds so it follows them between machines.</summary>
/// <param name="TelemetryEnabled">Whether this deployment may be told what the person's client is doing.</param>
/// <param name="Theme">What the client is painted in once a session exists.</param>
/// <param name="OpenMailInTabs">Whether opening a message opens a tab rather than replacing what is on the screen.</param>
/// <param name="MarkReadOnOpen">Whether opening a message in the client marks it read on the owner's own mail server.</param>
/// <param name="ExpandWholeThread">Whether opening a conversation draws every message in it rather than the one it was opened at.</param>
/// <remarks>
/// <para>
/// A closed set of five rather than a settings service. Each of them says how somebody wants to work rather than what
/// the screen in front of them is like, which is why they belong to the person and not to the browser profile or the
/// desktop install they happened to set them in — and why a sixth is added when there is a sixth to add.
/// </para>
/// <para>
/// Marking read is here rather than on the mail account for the reason
/// <see href="https://github.com/Krzysztof318/MailFathom/blob/main/docs/decisions/0026-marking-a-message-read-when-a-person-opens-it-in-the-client.md">ADR 0026</see>
/// gives: read state is what must not fragment between the machines one person reads on, so it covers every account
/// that owner reads and is not an operator's key.
/// </para>
/// <para>
/// It is deliberately not part of the owner record. That document is configuration, binds strictly against the rules a
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
    bool ExpandWholeThread)
{
    /// <summary>Gets what a person who has set nothing is answered with.</summary>
    /// <remarks>
    /// Telemetry on, because the switch withdraws a default this deployment already applies rather than granting one,
    /// and a stored answer that has never been written is not a refusal. The theme follows the machine, which is what
    /// the client resolves on the device before there is a session to read this at all. Tabs are off, because a person
    /// who has not asked for them is reading one message at a time. Marking read is on, because every mail client the
    /// owner already uses does it and a client that leaves their read state behind is one they keep another beside.
    /// A conversation opens at the message it was opened at, because that is the message somebody came for and the
    /// history behind it is one control away.
    /// </remarks>
    public static ClientPreferences Unset { get; } = new(true, ClientThemeChoice.System, false, true, false);
}

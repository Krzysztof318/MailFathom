// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

using System.Text.Json.Serialization;

namespace MailFathom.Application.Discovery.Presentation;

/// <summary>Says what the correspondence does for what a block states.</summary>
/// <remarks>
/// <para>
/// The four members are the reason a plan can be honest. Without them the only way to present a fact nothing backs is
/// to present it as though something did, the only way to present two sources that disagree is to pick one, and the
/// only way to present a fact whose every source is behind the mail server is to present it as current. All three are
/// states a run reaches routinely over years of mail, and all three are worse as prose inside an answer than as a value
/// a client can draw differently.
/// </para>
/// <para>
/// They partition rather than overlap, which is what lets a client draw one of four things and never guess between two.
/// <see cref="Stale" /> is the member that reads like a second axis and is not: freshness says how current the data
/// behind a block was, and this says what that currency does to the claim. A backed block whose freshness is stale is
/// <see cref="Stale" /> and never <see cref="Supported" />, because a reader deciding from three sources that all
/// stopped being current on Tuesday is in a different position from one reading three current ones.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<PresentationSupport>))]
public enum PresentationSupport
{
    /// <summary>The correspondence backs what the block states, and the citations are where it does.</summary>
    Supported = 0,

    /// <summary>Nothing found backs what the block states, which the block says rather than leaving the reader to assume.</summary>
    Unsupported = 1,

    /// <summary>The cited sources disagree with each other, and the block presents the disagreement rather than a choice between them.</summary>
    Conflicting = 2,

    /// <summary>The correspondence backs what the block states and every source behind it is known to be behind the mail server.</summary>
    /// <remarks>
    /// Told apart from <see cref="Supported" /> because the two invite different acts. A supported claim is checked by
    /// reading its sources; a stale one is checked by synchronizing first, since what would contradict it may be mail
    /// this deployment has not taken in yet.
    /// </remarks>
    Stale = 3,
}

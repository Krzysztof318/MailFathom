// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Chat;

/// <summary>Declares which model one capability runs on, for a capability that has nothing else to declare.</summary>
/// <remarks>
/// <para>
/// One type for every such block rather than one per capability, because each of them carries the same single key and a
/// type per section would be four copies of it. What a block means is named by the property that holds it on
/// <see cref="ChatModelOptions" />, which is also the key an operator writes.
/// </para>
/// <para>
/// A capability that decides anything else — whether it runs at all, what it may spend, how it judges — keeps a block of
/// its own with its own settings and a <c>Model</c> beside them, and is not one of these.
/// </para>
/// </remarks>
internal sealed class ChatCapabilityModelOptions
{
    /// <summary>Gets or sets which declared model this capability runs on, and empty to run it on <c>Chat:MainModel</c>.</summary>
    public ChatModelReferenceOptions Model { get; set; } = new();
}

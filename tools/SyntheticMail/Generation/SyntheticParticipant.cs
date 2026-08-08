// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation;

/// <summary>One invented person a generated message names.</summary>
/// <param name="DisplayName">The name the header carries, which may hold characters outside ASCII.</param>
/// <param name="Address">The address, always under <see cref="SyntheticVocabulary.ReservedTopLevelDomain" />.</param>
/// <remarks>
/// Nothing here is a real person and nothing here can become one: the address is unroutable by construction, so a
/// generated participant echoed into a reply or a forward reaches nobody.
/// </remarks>
internal sealed record SyntheticParticipant(string DisplayName, string Address);

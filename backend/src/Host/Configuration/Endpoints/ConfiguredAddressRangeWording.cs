// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Host.Configuration.Endpoints;

/// <summary>The words one configured address list's refusals are phrased in.</summary>
/// <param name="EntryNames">What one entry of the list names, as in "an empty entry names no …".</param>
/// <param name="Verb">What the list does with the addresses it names, as in "Write the network to … that whole range".</param>
/// <param name="WhyNotADnsName">The sentence explaining why a host name cannot stand in for an entry.</param>
internal sealed record ConfiguredAddressRangeWording(string EntryNames, string Verb, string WhyNotADnsName);

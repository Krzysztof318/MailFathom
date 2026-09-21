// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search.Phrasing;

/// <summary>A sentence somebody typed to describe the mail they are looking for, and the instant they typed it on.</summary>
/// <remarks>
/// <para>
/// The text is the same value a search is asked with, because it is the same field: somebody types into one box, and
/// whether what they wrote is two words or a sentence is not a decision they were asked to take. Reusing
/// <see cref="EmailSearchQueryText" /> is therefore the bound and the refusal already stated once, rather than a second
/// almost-identical rule about how long a thing somebody types may be.
/// </para>
/// <para>
/// The instant travels beside it because a sentence is full of time and none of it is absolute: <em>last quarter</em>,
/// <em>since Tuesday</em> and <em>this year</em> mean something only against the day the person is standing on, and
/// that is their day rather than the deployment's. It is resolved from this deployment's own clock and the zone that
/// person's record carries rather than sent by the client, so what reaches a provider's prompt is a value this
/// deployment holds. The reading still answers in calendar days, which is what the client turns back into the range it
/// already turns its own date controls into.
/// </para>
/// </remarks>
/// <param name="Text">What was typed, bounded and checked exactly as a search's own text is.</param>
/// <param name="AskedAt">The instant the reader is standing on, which every relative time expression is resolved against.</param>
public sealed record MailSearchPhrase(EmailSearchQueryText Text, DateTimeOffset AskedAt);

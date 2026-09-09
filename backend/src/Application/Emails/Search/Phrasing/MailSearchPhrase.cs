// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.Application.Emails.Search.Phrasing;

/// <summary>A sentence somebody typed to describe the mail they are looking for, and the day they typed it on.</summary>
/// <remarks>
/// <para>
/// The text is the same value a search is asked with, because it is the same field: somebody types into one box, and
/// whether what they wrote is two words or a sentence is not a decision they were asked to take. Reusing
/// <see cref="EmailSearchQueryText" /> is therefore the bound and the refusal already stated once, rather than a second
/// almost-identical rule about how long a thing somebody types may be.
/// </para>
/// <para>
/// The day travels beside it because a sentence is full of time and none of it is absolute: <em>last quarter</em>,
/// <em>since Tuesday</em> and <em>this year</em> mean something only against the day the person is standing on, and
/// that is their day rather than the deployment's. It is a calendar day rather than an instant so no zone arithmetic
/// happens here at all — the client sends the day it is showing them, the reading answers in calendar days, and the
/// client turns those back into the range it already turns its own date controls into.
/// </para>
/// </remarks>
/// <param name="Text">What was typed, bounded and checked exactly as a search's own text is.</param>
/// <param name="AskedOn">The reader's own calendar day, which every relative time expression is resolved against.</param>
public sealed record MailSearchPhrase(EmailSearchQueryText Text, DateOnly AskedOn);

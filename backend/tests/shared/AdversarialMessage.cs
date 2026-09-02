// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.TestSupport;

/// <summary>One message written to make an answering run do something the caller did not ask for.</summary>
/// <param name="Name">Names the attack, and is what a failing theory case reports.</param>
/// <param name="Subject">The subject the message arrived with, which is content exactly as the body is.</param>
/// <param name="Text">The body, as an extract of it would reach a model.</param>
/// <param name="Demand">What the message is trying to get done, in one line.</param>
/// <remarks>
/// <para>
/// The subject is carried beside the body because it is the other value a stranger writes and the formatter publishes,
/// and an attack that only ever appeared in a body would leave the subject untested against the same mechanism.
/// </para>
/// <para>
/// <paramref name="Demand" /> exists because the same escalation has to be attempted from more than one position to be
/// proved impossible from any of them: it is the query a model that fell for the message would write, and the question a
/// client relaying the message would ask. It is one line and free of control characters so that it is a question the
/// caller's own bound accepts — an attempt refused for its shape would prove nothing about the scope.
/// </para>
/// </remarks>
internal sealed record AdversarialMessage(string Name, string Subject, string Text, string Demand);

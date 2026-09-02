// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.AiContent;

/// <summary>Answers one message's content from a model, which is the only thing AI generation asks of it.</summary>
/// <remarks>
/// <para>
/// The seam the generator is written against. The generator decides the envelope — author, thread, date, language,
/// topic, attachment — and hands one request over at a time; what the answer says is the source's alone. A test
/// answers from a table, a run answers from the provider named in its configuration, and the corpus the generator
/// builds around the answers is identical either way.
/// </para>
/// <para>
/// Nothing about the source's transport crosses this boundary: a provider that refuses the credential, times out, or
/// fails the request surfaces as a <see cref="SyntheticMailFailure" /> with one line naming the move, for the reason
/// <see cref="SyntheticMailFailure" /> gives. The prompt and the answer never reach a log, because the prompt names a
/// language, a topic, and the opening of the synthetic message being answered, and the answer is message content —
/// both are what this tool exists to keep out of a developer's real mail, and a log line is a third copy of the
/// second.
/// </para>
/// </remarks>
internal interface IAiEmailContentSource
{
    /// <summary>Generates the content one message carries.</summary>
    /// <param name="request">What the message is: its language, its topic, who writes it, and what it answers.</param>
    /// <param name="cancellationToken">Cancels the generation.</param>
    /// <returns>The subject, the body as text, and the body as markup.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="request" /> is <see langword="null" />.</exception>
    /// <exception cref="SyntheticMailFailure">Thrown when the provider cannot or will not answer, with a message naming the move.</exception>
    Task<AiEmailContent> GenerateAsync(AiEmailContentRequest request, CancellationToken cancellationToken);
}

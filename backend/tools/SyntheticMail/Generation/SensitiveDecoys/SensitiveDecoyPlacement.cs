// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

namespace MailFathom.SyntheticMail.Generation.SensitiveDecoys;

/// <summary>Where in its sentence a fabricated value is written, which decides what character follows it.</summary>
/// <remarks>
/// <para>
/// <b>The character after the value is part of what a decoy tests.</b> A rule recognises a credential by its shape and
/// then has to establish where that shape ends, and the corpora this generator plants against answer that by looking at
/// whatever stands immediately afterwards. A corpus that always wrote a value the same way would exercise one answer to
/// that question and report every other one as though it could not fail — which is what happened: every decoy was
/// followed by a space, and forty-nine rules that could not see past a full stop passed the corpus for a year.
/// </para>
/// <para>
/// The four are placements mail actually produces: a value inside a sentence, one closing a sentence, one a writer put
/// in brackets, and one standing in a cell of the pipe-delimited table a client renders a table as. A placement changes
/// what surrounds the value and never the sentence's own words, because those carry the context a personal-data
/// recogniser scores on and rewriting them would change what the decoy tests rather than where it sits.
/// </para>
/// </remarks>
internal enum SensitiveDecoyPlacement
{
    /// <summary>Written where the sentence puts it, with the sentence going on afterwards.</summary>
    MidSentence = 0,

    /// <summary>Written at the end of the sentence, closed by the full stop that ends it.</summary>
    ClosingTheSentence = 1,

    /// <summary>Written in round brackets, so a closing bracket follows the value.</summary>
    InBrackets = 2,

    /// <summary>Written as a cell of a pipe-delimited table, so a bar follows the value.</summary>
    InATableCell = 3,
}

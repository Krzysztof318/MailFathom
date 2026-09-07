// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailSearchAttachmentMatch, MailSearchResult } from '@mailfathom/client-backend';

// Which of a result's cited files a row is about. It is one decision read in two places — the line that names the file
// and the act of opening it have to agree, or a reader is told about one file and handed another — so it is a function
// beside the screen rather than an expression written twice inside it.

/**
 * The file a row names, where the query reached more than one.
 *
 * A file's own words are preferred over a description of a picture, because
 * [ADR 0030](../../../../../docs/decisions/0030-describing-an-image-attachment-in-words-and-ranking-a-depicted-match-below-a-written-one.md)
 * makes a depicted match corroborating rather than a headline: a row leading with a model's guess while a document the
 * sender wrote also matched would report the weaker of the two as the reason it is there.
 *
 * @param result The result being drawn.
 * @returns The file to name, or `undefined` where the query reached none.
 */
export function citedFileOf(result: MailSearchResult): MailSearchAttachmentMatch | undefined {
    return result.attachmentMatches.find((match) => match.source === 'Document') ?? result.attachmentMatches[0];
}

/**
 * The file a row explains itself with, which is none where the message's own words already explain it.
 *
 * An extract of the body is what the row shows wherever the deployment cut one, so a file is named only where it is
 * what the row is actually about. Opening the row follows the same answer, which is why both read this rather than
 * deciding separately.
 *
 * @param result The result being drawn.
 * @returns The file the row explains itself with, or `undefined` where the body's own extract does.
 */
export function explainingFileOf(result: MailSearchResult): MailSearchAttachmentMatch | undefined {
    return result.snippets[0] === undefined ? citedFileOf(result) : undefined;
}

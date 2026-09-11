// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { MailBody, MailDocumentBlock, MailInlineRun, MailTextEmphasis } from '@mailfathom/client-backend';
import { followable, type WrittenElement, type WrittenNode } from './writtenText';

// A draft the deployment holds, read back into the closed tree the composer writes. It is the one direction
// `writtenText.ts` does not already cover: that module reads an editable region into the tree and renders the tree
// back out, and this reads the *deployment's* description of a message into the same tree so a draft can be carried
// on with rather than only read.
//
// **It reads the reduced document rather than the sender's markup**, which is the whole reason it can exist at all.
// ADR 0024 keeps this client from parsing mail markup anywhere, and nothing here does: what arrives is already the
// typed tree the reading pane draws, checked at the client's trust boundary in `mailBody.ts`, and this walks it into a
// second typed tree. No string is parsed, no sanitizer is pinned, and what a hostile draft can put in the composer is
// bounded by what `WrittenNode` can express.
//
// **What the composer cannot write is not written.** A draft carrying a table, a picture, or a block a later
// deployment publishes has no shape here — the composer's set is what a person can type with its own controls, and
// inventing an approximation would put words on the screen the author never wrote. A table's cells are still walked,
// because the words in them are the message; the grid is what is lost. A body that leaves nothing the composer can
// write — one the deployment could not reduce at all, and one whose every block is a block this has no shape for —
// falls back to its plain text, which is the rendering the deployment always answers with.

/** The words of a draft, as the closed tree the composer writes. */
export function draftWords(body: MailBody): readonly WrittenNode[] {
    const written = (body.document?.blocks ?? []).flatMap(writtenBlock);

    return written.length === 0 ? writtenParagraphs(body.plainText.text) : written;
}

// One block as the elements the composer has for it. An empty array is a block it has none for, which is dropped
// rather than approximated.
function writtenBlock(block: MailDocumentBlock): readonly WrittenNode[] {
    switch (block.type) {
        case 'paragraph':
            return [wrapping('p', runsWritten(block.content))];

        // The composer writes no headings, so a heading is a paragraph in the strongest emphasis it does write. That
        // keeps the line reading as the line it was rather than levelling it into the prose around it.
        case 'heading':
            return [wrapping('p', [wrapping('strong', runsWritten(block.content))])];

        case 'list':
            return [
                wrapping(
                    block.ordered ? 'ol' : 'ul',
                    block.items.map((item) => wrapping('li', item.blocks.flatMap(writtenBlock))),
                ),
            ];

        case 'quote':
            return [wrapping('blockquote', block.blocks.flatMap(writtenBlock))];

        case 'preformatted':
            return writtenParagraphs(block.text);

        // The grid is not something the composer draws, so what is kept is what the cells say, in the order they say
        // it. A table dropped whole would take the message's words with it.
        case 'table':
            return block.rows.flatMap((row) => row.cells.flatMap((cell) => cell.blocks.flatMap(writtenBlock)));

        case 'image':
        case 'separator':
        case 'unimplemented':
            return [];
    }
}

// The runs of one paragraph, each wrapped in whatever it carries. A run is wrapped from the inside out, so a bold
// italic link is one element inside another rather than a shape this had to name.
function runsWritten(runs: readonly MailInlineRun[]): readonly WrittenNode[] {
    return runs.map((run) => {
        let written: WrittenNode = { text: run.text };

        for (const element of emphasised(run.emphasis)) {
            written = { element, address: null, holds: [written] };
        }

        // A link the composer would not make is drawn as its own words instead, which is the same refusal
        // `writtenText.ts` states for one somebody pastes: a scheme this client will not follow is not one it will
        // put back on the screen as a link either.
        return run.link !== null && followable(run.link.target)
            ? { element: 'a' as const, address: run.link.target, holds: [written] }
            : written;
    });
}

// Which of the composer's own emphases a run carries. Monospace is not among them: the composer writes no code and a
// run that was monospace loses that rather than borrowing an element that means something else.
function emphasised(emphasis: MailTextEmphasis): readonly WrittenElement[] {
    return [
        ...(emphasis.bold ? (['strong'] as const) : []),
        ...(emphasis.italic ? (['em'] as const) : []),
        ...(emphasis.underline ? (['u'] as const) : []),
        ...(emphasis.strikethrough ? (['s'] as const) : []),
    ];
}

/**
 * Plain text as the closed tree, one paragraph per line.
 *
 * A line is a paragraph rather than a `br` because that is what the composer's own editable region produces from
 * typing, and text that goes back through it should come out the shape it went in. Exported because a drafted message
 * arrives as plain text too — the drafting route answers no document — and reading it any other way here would be a
 * second answer to the same question.
 *
 * @param text The text, with its line breaks.
 * @returns One paragraph per line, an empty line included.
 */
export function writtenParagraphs(text: string): readonly WrittenNode[] {
    return text.split('\n').map((line) => wrapping('p', line === '' ? [] : [{ text: line }]));
}

function wrapping(element: WrittenElement, holds: readonly WrittenNode[]): WrittenNode {
    return { element, address: null, holds };
}

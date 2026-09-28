// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type {
    MailBlockAlignment,
    MailDocumentBlock,
    MailDocumentLink,
    MailHeadingBlock,
    MailImageBlock,
    MailInlineRun,
    MailListBlock,
    MailPreformattedBlock,
    MailQuoteBlock,
    MailTableBlock,
} from '@mailfathom/client-backend';
import { useLocalization } from '../localization/useLocalization';
import { MessageLink } from './MessageLink';
import { senderColourRole } from './senderColour';
import {
    headingShapeAt,
    inlineCodeShape,
    orderedListShape,
    preformattedShape,
    quoteShape,
    separatorShape,
    strikethroughShape,
    strongShape,
    tableCellShape,
    tableHeaderCellShape,
    tableRegionShape,
    tableRowShape,
    tableShape,
    unorderedListShape,
} from './writtenBlockShapes';

// The whole of what a message may draw, which is the closed catalogue the service reduces every body to. Each block is
// an ordinary element of the application's own document: nothing here is handed markup, nothing here writes any, and
// the only presentation a message contributes is an opaque colour, an alignment, and an emphasis flag — each applied
// by the component below rather than by anything the sender wrote.
//
// The catalogue is closed, so this switch is exhaustive by its own type: a block added to the contract fails to
// compile here until this file says how it is drawn.
//
// **How each of them is drawn is the design project's message body rather than a browser's defaults.** The measures
// below are that document read block by block: a quotation is the callout the design draws — highlighted, cut against
// its own rule — a table stands in a bordered card with its header row on the sunken tint, a code block is a panel of
// its own, and a heading a sender wrote climbs the same three steps the design gives one. The scale itself lives with
// the body around it in `MessageBody.tsx`, so a message carrying none of these blocks inherits it and reads as the
// plain text it is.

const alignments: Readonly<Record<MailBlockAlignment, string>> = {
    Inherited: '',
    Start: 'text-start',
    Center: 'text-center',
    End: 'text-end',
    Justify: 'text-justify',
};

// A message never claims a level the screen around it already holds, so every heading it wrote is drawn two levels
// deeper: the space's own title is the first, and the reading pane draws the message's subject as the second. What a
// sender wrote therefore starts below the subject it belongs to, which is what makes the reading order a real heading
// order rather than a message whose own headings read as siblings of its subject.
const headingElements = ['h3', 'h4', 'h5', 'h6', 'h6', 'h6'] as const;

export function MessageBlocks({ blocks }: { readonly blocks: readonly MailDocumentBlock[] }) {
    return (
        <>
            {blocks.map((block, position) => (
                <MessageBlock key={position} block={block} />
            ))}
        </>
    );
}

function MessageBlock({ block }: { readonly block: MailDocumentBlock }) {
    switch (block.type) {
        case 'paragraph':
            return (
                <p className={alignments[block.alignment]}>
                    <MessageRuns content={block.content} />
                </p>
            );
        case 'heading':
            return <MessageHeading block={block} />;
        case 'list':
            return <MessageList block={block} />;
        case 'table':
            return <MessageTable block={block} />;
        case 'quote':
            return <MessageQuote block={block} />;
        case 'image':
            return <MessagePicture block={block} />;
        case 'separator':
            return <hr className={separatorShape} />;
        case 'preformatted':
            return <MessagePreformatted block={block} />;
        case 'unimplemented':
            return <UndrawnBlock />;
    }
}

function MessageHeading({ block }: { readonly block: MailHeadingBlock }) {
    const Heading = headingElements[block.level - 1] ?? 'h6';
    // The element is decided above and the measure independently, because the reading order a screen reader follows
    // is not the type scale.
    const measure = headingShapeAt(block.level);

    return (
        <Heading className={`${measure} ${alignments[block.alignment]}`}>
            <MessageRuns content={block.content} />
        </Heading>
    );
}

function MessageList({ block }: { readonly block: MailListBlock }) {
    const items = block.items.map((item, position) => (
        <li key={position}>
            <MessageBlocks blocks={item.blocks} />
        </li>
    ));

    return block.ordered ? (
        <ol className={orderedListShape}>{items}</ol>
    ) : (
        <ul className={unorderedListShape}>{items}</ul>
    );
}

function MessageTable({ block }: { readonly block: MailTableBlock }) {
    const { translate } = useLocalization();

    return (
        // A table is how mail layout is overwhelmingly built, so it is the one block that may be wider than the pane.
        // It scrolls inside its own box rather than making the page scroll sideways under everything else — and it is
        // focusable and named, because WebKit gives a scroll container holding nothing focusable no keyboard path at
        // all, which would leave the columns past the right edge unreachable on the head the desktop shell renders in.
        <div aria-label={translate('body.tableRegion')} className={tableRegionShape} role="group" tabIndex={0}>
            <table className={tableShape}>
                <colgroup>
                    {block.columns.map((column, position) => (
                        <col
                            key={position}
                            style={
                                column.widthShare === null
                                    ? undefined
                                    : { width: `${(column.widthShare * 100).toFixed(2)}%` }
                            }
                        />
                    ))}
                </colgroup>
                <tbody>
                    {block.rows.map((row, rowPosition) => (
                        <tr className={tableRowShape} key={rowPosition}>
                            {row.cells.map((cell, cellPosition) => {
                                const Cell = row.isHeader ? 'th' : 'td';

                                return (
                                    <Cell
                                        key={cellPosition}
                                        // A cell the sender filled keeps the fact that they filled it and loses the
                                        // colour they filled it with, which is the rule a coloured run follows: a total
                                        // row stands out from the rows above it, in the one fill this client draws on
                                        // both panels rather than in a value picked against a white page. The weight is
                                        // the header's alone — a sender who filled a cell said it stands out, not that
                                        // it labels the column, and drawing it bold would be this client claiming so.
                                        className={`${tableCellShape} ${
                                            row.isHeader ? tableHeaderCellShape : ''
                                        } ${cell.background === null ? '' : 'bg-sunken'} ${alignments[cell.alignment]}`}
                                        colSpan={cell.columnSpan}
                                        rowSpan={cell.rowSpan}
                                        scope={row.isHeader ? 'col' : undefined}
                                    >
                                        <MessageBlocks blocks={cell.blocks} />
                                    </Cell>
                                );
                            })}
                        </tr>
                    ))}
                </tbody>
            </table>
        </div>
    );
}

function MessageQuote({ block }: { readonly block: MailQuoteBlock }) {
    return (
        <blockquote className={quoteShape}>
            <MessageBlocks blocks={block.blocks} />
        </blockquote>
    );
}

function MessagePicture({ block }: { readonly block: MailImageBlock }) {
    const { translate } = useLocalization();

    // A picture the sender described carries their words; one they did not is still announced, because a reader who
    // cannot see it is owed the fact that something is there rather than silence.
    const description = block.image.alternativeText ?? translate('body.pictureWithoutDescription');

    const picture = (
        <img
            alt={description}
            className="max-w-full"
            height={block.image.height ?? undefined}
            // A reader who asked for the sender's pictures agreed to tell them the message was opened, and to nothing
            // beyond that. The default policy would send this deployment's own address with the request, which for a
            // self-hosted MailFathom is somewhere somebody chose and did not publish.
            referrerPolicy="no-referrer"
            src={block.image.source}
            width={block.image.width ?? undefined}
        />
    );

    return (
        <p className={alignments[block.alignment]}>
            {block.link === null ? picture : <MessageLink link={block.link}>{picture}</MessageLink>}
        </p>
    );
}

function MessagePreformatted({ block }: { readonly block: MailPreformattedBlock }) {
    const { translate } = useLocalization();

    // Focusable and named for the reason the table above is: a line longer than the pane is reachable from a keyboard
    // only where the region that scrolls it can take focus.
    return (
        <pre aria-label={translate('body.preformattedRegion')} className={preformattedShape} role="group" tabIndex={0}>
            {block.text}
        </pre>
    );
}

function UndrawnBlock() {
    const { translate } = useLocalization();

    return <p className="text-sm text-muted">{translate('body.blockNotDrawn')}</p>;
}

// One anchor is one link on the screen, however many runs the service split it into: it merges adjacent runs only
// where the emphasis, the colour, and the link are all equal, so an anchor whose words change weight halfway arrives as
// two runs of one link. Drawn one anchor per run, a reader would meet the target and the warning twice, tab through it
// twice, and find it twice in a screen reader's list of links.
function MessageRuns({ content }: { readonly content: readonly MailInlineRun[] }) {
    return (
        <>
            {anchors(content).map((anchor, position) =>
                anchor.link === null ? (
                    <MessageRunGroup key={position} runs={anchor.runs} />
                ) : (
                    <MessageLink key={position} link={anchor.link}>
                        <MessageRunGroup runs={anchor.runs} />
                    </MessageLink>
                ),
            )}
        </>
    );
}

interface Anchor {
    readonly link: MailDocumentLink | null;
    readonly runs: readonly MailInlineRun[];
}

/** The runs grouped as the reader meets them: adjacent runs of one link together, and every other run on its own. */
function anchors(content: readonly MailInlineRun[]): Anchor[] {
    const grouped: Anchor[] = [];

    for (const run of content) {
        const last = grouped.at(-1);

        // The same link rather than an equal one: the parser answers with one object per anchor and the service splits
        // an anchor only into runs that follow each other, so identity is what says "still inside that anchor".
        if (run.link !== null && last?.link === run.link) {
            grouped[grouped.length - 1] = { link: run.link, runs: [...last.runs, run] };
            continue;
        }

        grouped.push({ link: run.link, runs: [run] });
    }

    return grouped;
}

function MessageRunGroup({ runs }: { readonly runs: readonly MailInlineRun[] }) {
    return (
        <>
            {runs.map((run, position) => (
                <MessageRun key={position} run={run} />
            ))}
        </>
    );
}

function MessageRun({ run }: { readonly run: MailInlineRun }) {
    // A `<br>` the sender wrote survives as a newline inside the run rather than as a block of its own, which is what
    // `MailInlineRun` states — so a signature, a postal address, and a poem are all line breaks this has to keep.
    const emphasized = emphasize(run, <span className="whitespace-pre-line">{run.text}</span>);

    // The sender's colour decides which of this client's own two it is drawn in rather than what it is drawn in, for the
    // reason `messageBody/senderColour.ts` holds: a reading surface is drawn in the reader's theme, and what survives
    // of the sender's own presentation is the emphasis above, which means something a colour does not.
    const receding = run.foreground !== null && senderColourRole(run.foreground) === 'receding';

    return receding ? <span className="text-muted">{emphasized}</span> : emphasized;
}

// Emphasis is drawn with the elements that mean it rather than with a class, so the meaning survives a stylesheet and
// composes when a run carries several flags at once — which a single text-decoration utility cannot express.
function emphasize(run: MailInlineRun, text: ReactNode): ReactNode {
    let emphasized = text;

    if (run.emphasis.monospace) {
        emphasized = <code className={inlineCodeShape}>{emphasized}</code>;
    }

    if (run.emphasis.strikethrough) {
        emphasized = <s className={strikethroughShape}>{emphasized}</s>;
    }

    if (run.emphasis.underline) {
        emphasized = <u>{emphasized}</u>;
    }

    if (run.emphasis.italic) {
        emphasized = <em>{emphasized}</em>;
    }

    return run.emphasis.bold ? <strong className={strongShape}>{emphasized}</strong> : emphasized;
}

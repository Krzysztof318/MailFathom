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

// The whole of what a message may draw, which is the closed catalogue the service reduces every body to. Each block is
// an ordinary element of the application's own document: nothing here is handed markup, nothing here writes any, and
// the only presentation a message contributes is an opaque colour, an alignment, and an emphasis flag — each applied
// by the component below rather than by anything the sender wrote.
//
// The catalogue is closed, so this switch is exhaustive by its own type: a block added to the contract fails to
// compile here until this file says how it is drawn.

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
            return <hr className="border-line" />;
        case 'preformatted':
            return <MessagePreformatted block={block} />;
        case 'unimplemented':
            return <UndrawnBlock />;
    }
}

function MessageHeading({ block }: { readonly block: MailHeadingBlock }) {
    const Heading = headingElements[block.level - 1] ?? 'h6';

    return (
        <Heading className={`font-semibold text-text ${alignments[block.alignment]}`}>
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

    return block.ordered ? <ol className="list-decimal ps-6">{items}</ol> : <ul className="list-disc ps-6">{items}</ul>;
}

function MessageTable({ block }: { readonly block: MailTableBlock }) {
    const { translate } = useLocalization();

    return (
        // A table is how mail layout is overwhelmingly built, so it is the one block that may be wider than the pane.
        // It scrolls inside its own box rather than making the page scroll sideways under everything else — and it is
        // focusable and named, because WebKit gives a scroll container holding nothing focusable no keyboard path at
        // all, which would leave the columns past the right edge unreachable on the head the desktop shell renders in.
        <div aria-label={translate('body.tableRegion')} className="overflow-x-auto" role="group" tabIndex={0}>
            <table className="w-full border-collapse text-start">
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
                        <tr key={rowPosition}>
                            {row.cells.map((cell, cellPosition) => {
                                const Cell = row.isHeader ? 'th' : 'td';

                                return (
                                    <Cell
                                        key={cellPosition}
                                        className={`border border-line px-3 py-2 align-top ${alignments[cell.alignment]}`}
                                        colSpan={cell.columnSpan}
                                        rowSpan={cell.rowSpan}
                                        scope={row.isHeader ? 'col' : undefined}
                                        style={
                                            cell.background === null ? undefined : { backgroundColor: cell.background }
                                        }
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
        <blockquote className="border-s-2 border-line ps-4 text-muted">
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
        <pre
            aria-label={translate('body.preformattedRegion')}
            className="overflow-x-auto font-mono text-sm"
            role="group"
            tabIndex={0}
        >
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

    return run.foreground === null ? emphasized : <span style={{ color: run.foreground }}>{emphasized}</span>;
}

// Emphasis is drawn with the elements that mean it rather than with a class, so the meaning survives a stylesheet and
// composes when a run carries several flags at once — which a single text-decoration utility cannot express.
function emphasize(run: MailInlineRun, text: ReactNode): ReactNode {
    let emphasized = text;

    if (run.emphasis.monospace) {
        emphasized = <code className="font-mono">{emphasized}</code>;
    }

    if (run.emphasis.strikethrough) {
        emphasized = <s>{emphasized}</s>;
    }

    if (run.emphasis.underline) {
        emphasized = <u>{emphasized}</u>;
    }

    if (run.emphasis.italic) {
        emphasized = <em>{emphasized}</em>;
    }

    return run.emphasis.bold ? <strong>{emphasized}</strong> : emphasized;
}

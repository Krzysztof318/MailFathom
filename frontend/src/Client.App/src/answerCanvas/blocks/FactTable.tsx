// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { AnswerBlock, FactTableCell } from '@mailfathom/client-backend';
import { useLocalization } from '../../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from '../AnswerBlockCard';
import { Citation } from './Citation';
import { factTableColumns, factTableRowCounts } from './blockWording';
import { citationOrder } from './citationOrder';

// Values compared across a known set of columns, where the comparison is the answer: what each of them quoted, how two
// versions of an agreement differ.
//
// **A cell cites itself.** The product's own interaction here is *check the evidence for this figure*, which a table
// citing itself as a whole cannot offer — a reader who distrusts one number would be handed the sources behind all of
// them. So the citations sit in the cell, and the cell is the smallest thing whose evidence is reachable in one press.
//
// **It is a real table**, which is what makes it navigable by cell: a screen reader announces the column a cell
// belongs to only when the element is a `td` under a `th`, and a grid of `div` elements that looked identical would
// leave somebody reading a figure with nothing saying what it is a figure of.
//
// **A heading is this client's word rather than the run's.** The column arrives as an identity out of a closed
// catalogue and the heading is drawn for it here, which is the only way a Polish reader is not shown an English
// header — and it is why a column outside the catalogue is refused at the boundary rather than drawn nameless.
//
// **A cell the correspondence says nothing about says so.** It is not a blank: presenting silence and an empty value
// alike is how a comparison quietly asserts that one side offered nothing when nobody asked them.
//
// **A value is never reformatted.** The contract carries what the correspondence wrote — "roughly €40k" as readily as
// "1200.00" — so what the column's kind decides is which edge the value is aligned to and whether its numerals are
// held in step, and never a parse this client would be inventing.
//
// **The rows are not windowed**, which is the repository's rule rather than an omission: the contract bounds a table
// at fifty rows and `frontend/src/AGENTS.md` § *Performance* windows a list that can exceed two hundred.

export function FactTable({ block }: { readonly block: AnswerBlock }) {
    const { locale, translate } = useLocalization();

    if (block.type !== 'factTable') {
        return <UnrecognisedAnswerBlock named={block.named} />;
    }

    const { columns, rows, evidence } = block;
    const cited = rows.flatMap((row) => row.cells).map((cell) => cell.sources);
    const positionOf = citationOrder(evidence.citations, ...cited);

    // A table with no column to compare across and one with no row to compare are the same thing to a reader: a
    // heading over nothing. Both are the empty state rather than a frame drawn around an absence.
    const empty = columns.length === 0 || rows.length === 0;
    const named = translate('answer.factTableCaption');

    return (
        <AnswerBlockCard
            label={translate('answer.factTableLabel')}
            meta={
                empty
                    ? undefined
                    : translate(factTableRowCounts[new Intl.PluralRules(locale).select(rows.length)], {
                          count: new Intl.NumberFormat(locale).format(rows.length),
                      })
            }
            note={empty ? translate('answer.factTableEmpty') : undefined}
            state={empty ? 'empty' : 'ready'}
        >
            {/* A table wider than the card scrolls on its own rather than squeezing every column into illegibility.
                A scrolling region is reachable from the keyboard and carries a name saying what it is, because a
                focus stop nothing announces is a place a screen reader lands on with nothing to say. */}
            <div aria-label={named} className="overflow-x-auto" role="region" tabIndex={0}>
                <table className="w-full border-collapse text-sm">
                    <thead>
                        <tr>
                            {columns.map((column) => (
                                <th
                                    className={`border-b border-line pb-2 text-xs font-normal text-muted ${
                                        factTableColumns[column].numeric ? 'ps-2.5 text-end' : 'pe-2.5 text-start'
                                    }`}
                                    key={column}
                                    scope="col"
                                >
                                    {translate(factTableColumns[column].heading)}
                                </th>
                            ))}
                        </tr>
                    </thead>

                    <tbody>
                        {rows.map((row, at) => (
                            <tr key={at}>
                                {columns.map((column, of) => {
                                    const cell = row.cells[of];

                                    return cell === undefined ? null : (
                                        <ComparedValue
                                            cell={cell}
                                            key={column}
                                            numeric={factTableColumns[column].numeric}
                                            positionOf={positionOf}
                                        />
                                    );
                                })}
                            </tr>
                        ))}
                    </tbody>
                </table>
            </div>
        </AnswerBlockCard>
    );
}

/** One cell: the value as the correspondence wrote it, what it rests on, or that the correspondence says nothing. */
function ComparedValue({
    cell,
    numeric,
    positionOf,
}: {
    readonly cell: FactTableCell;
    readonly numeric: boolean;
    readonly positionOf: (source: string) => number;
}) {
    const { translate } = useLocalization();

    return (
        <td className={`border-b border-line-soft py-2.5 ${numeric ? 'ps-2.5' : 'pe-2.5'}`}>
            <span className={`flex flex-wrap items-center gap-1.5 ${numeric ? 'justify-end tabular-nums' : ''}`}>
                {cell.value === null ? (
                    <span className="text-faint italic">{translate('answer.factCellUnstated')}</span>
                ) : (
                    <span className="text-pretty">{cell.value}</span>
                )}

                {cell.sources.map((source) => (
                    <Citation key={source} position={positionOf(source)} source={source} />
                ))}
            </span>
        </td>
    );
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useId, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SkeletonLines } from '../controls/Skeleton';
import { useLocalization } from '../localization/useLocalization';
import type { AnswerBlockState } from './answerBlocks';

// The one shape a block of an answer is drawn in, and therefore the one place its six states are written. A renderer
// says which state it is in and what its body holds; everything around that — the name over it, the waiting, the
// emptiness, the failure and the way out of one — is here, so that nine renderers cannot come to disagree about what a
// block that returned nothing looks like.
//
// It is a region a reader lands on rather than a control they press: the card takes focus so the keyboard moves from
// block to block without walking every citation inside one, and it carries the block's own name so that landing on it
// says which block it is.

// The lines a block of prose stands as while it is being composed, which is the design project's own raggedness rather
// than three bars of one length.
const waitingLines = [
    { fills: 92, height: 'h-3.25 mb-2' },
    { fills: 78, height: 'h-3.25 mb-2' },
    { fills: 55, height: 'h-3.25' },
];

const stateIcons: Readonly<Record<'empty' | 'error' | 'offline', IconName>> = {
    empty: 'inbox',
    error: 'error',
    offline: 'cloud_off',
};

/**
 * One block of an answer, in whichever of its six states it is.
 *
 * @param label What this block is called, already in the reader's language.
 * @param meta What the block says about itself beside its name — how much it holds, or that it is still working.
 * @param state What the block is doing, which decides everything below the name.
 * @param note What the failing or empty state says, where the caller knows more about it than the state does, and
 * nothing where the state's own sentence is the whole of it.
 * @param onRetry What the way out of a failure does, and nothing where this block has none to offer.
 * @param children The body, which is drawn for a block that is ready and for one that is partly so.
 */
export function AnswerBlockCard({
    label,
    meta,
    state,
    note,
    onRetry,
    children,
}: {
    readonly label: string;
    readonly meta?: string | undefined;
    readonly state: AnswerBlockState;
    readonly note?: string | undefined;
    readonly onRetry?: (() => void) | undefined;
    readonly children?: ReactNode;
}) {
    const { translate } = useLocalization();
    const named = useId();

    return (
        <BlockCard label={label} labelId={named} meta={meta}>
            {state === 'loading' ? (
                <>
                    <p className="sr-only" role="status">
                        {translate('answerBlock.loading', { block: label })}
                    </p>

                    <SkeletonLines lines={waitingLines} />
                </>
            ) : null}

            {state === 'ready' || state === 'partial' ? children : null}

            {state === 'partial' ? (
                <p className="flex items-start gap-1.75 rounded-md bg-warning-soft px-2.75 py-2 text-sm text-warning-text">
                    <Icon className="mt-0.25 size-3.75" name="hourglass_top" />
                    {translate('answerBlock.partial')}
                </p>
            ) : null}

            {state === 'empty' || state === 'error' || state === 'offline' ? (
                <div className="flex flex-col items-center gap-1.75 px-2.5 py-5.5 text-center">
                    <Icon
                        className={`size-6.5 ${state === 'error' ? 'text-warning' : 'text-faint'}`}
                        name={stateIcons[state]}
                    />

                    <p className="max-w-95 text-sm text-text-soft text-pretty">
                        {note ??
                            translate(
                                state === 'empty'
                                    ? 'answerBlock.empty'
                                    : state === 'error'
                                      ? 'answerBlock.error'
                                      : 'answerBlock.offline',
                            )}
                    </p>

                    {onRetry === undefined ? null : (
                        <button
                            className="mt-0.5 rounded-md bg-accent px-3.5 py-1.75 text-sm font-medium text-on-accent transition hover:bg-accent-strong"
                            type="button"
                            onClick={onRetry}
                        >
                            {translate('connection.retry')}
                        </button>
                    )}
                </div>
            ) : null}
        </BlockCard>
    );
}

/**
 * A block whose type this build cannot draw, which is both a type the contract does not carry and one whose renderer
 * has not been written yet.
 *
 * It is a card rather than a gap, because what the reader is owed is that the answer is missing a part and which part:
 * a block silently dropped is an answer that looks complete and is not.
 */
export function UnrecognisedAnswerBlock({ named }: { readonly named: string }) {
    const { translate } = useLocalization();
    const labelled = useId();

    return (
        <BlockCard
            label={translate('answerBlock.unrecognised')}
            labelId={labelled}
            meta={translate('answerBlock.unrecognisedType', { named })}
        >
            <div className="flex items-start gap-2.75">
                <Icon className="mt-0.5 size-6.5 shrink-0 text-faint" name="extension_off" />

                <div className="flex flex-col gap-1.25">
                    <p className="text-sm text-text-soft text-pretty">
                        {translate('answerBlock.unrecognisedBody', { named })}
                    </p>

                    <p className="text-xs text-faint">{translate('answerBlock.unrecognisedRemedy')}</p>
                </div>
            </div>
        </BlockCard>
    );
}

/** The chrome every block stands in: the card, the name over it, and what the block says about itself beside that. */
function BlockCard({
    label,
    labelId,
    meta,
    children,
}: {
    readonly label: string;
    readonly labelId: string;
    readonly meta?: string | undefined;
    readonly children: ReactNode;
}) {
    return (
        <article
            aria-labelledby={labelId}
            className="flex flex-col gap-3 rounded-xl border border-line bg-panel px-4.5 py-4"
            tabIndex={0}
        >
            <div className="flex flex-wrap items-center gap-2.5">
                <h3 className="text-2xs tracking-widest text-muted uppercase" id={labelId}>
                    {label}
                </h3>

                {meta === undefined ? null : <p className="ms-auto text-xs whitespace-nowrap text-muted">{meta}</p>}
            </div>

            {children}
        </article>
    );
}

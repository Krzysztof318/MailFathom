// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type { ClientFailureReason } from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { AnswerBlockCard, type BlockCardTone } from './AnswerBlockCard';
import type { AnswerBlockState } from './answerBlocks';
import { useAnswerSources } from './answerSources';
import { citedThread, useProposalAnswering, type ProposalLook, type ProposalPhase } from './proposalAnswering';

// The frame every block that proposes something stands in, and so the one place its phases are drawn. A renderer says
// what it proposes and which controls answer it; where the proposal stands — waiting, done, not done, turned down — and
// what that does to the card, the chip, the controls and the rows leading away from it is decided here, so four types
// cannot come to disagree about what a declined proposal looks like.
//
// **Read-only is the absence of a way to answer**, not a mode: the surface drawing the answer provides one or does not,
// and a proposal Discover draws is the same card with no controls and nothing but the chip saying it waits on somebody.

/** The four block types that propose something rather than report it. */
export type ProposalKind = 'eventProposal' | 'taskProposal' | 'draft' | 'suggestedAction';

/** One control a pending proposal offers, which the renderer states because each type is answered in its own words. */
export interface ProposalControl {
    /** What the control says on it. */
    readonly said: string;

    /** What it does to which object, which is what a screen reader announces instead of the label alone. */
    readonly name: string;

    readonly primary?: boolean;

    /** Held without being removed, where pressing it could not do what it says. */
    readonly held?: boolean;

    readonly run: () => void;
}

interface KindWording {
    readonly done: MessageKey;
    readonly failed: MessageKey;
    readonly empty: MessageKey;
    readonly emptyIcon: IconName;
    readonly error: MessageKey;
}

const kindWording: Readonly<Record<ProposalKind, KindWording>> = {
    eventProposal: {
        done: 'eventProposal.done',
        failed: 'eventProposal.failed',
        empty: 'eventProposal.empty',
        emptyIcon: 'event_busy',
        error: 'eventProposal.error',
    },
    taskProposal: {
        done: 'taskProposal.done',
        failed: 'taskProposal.failed',
        empty: 'taskProposal.empty',
        emptyIcon: 'task_alt',
        error: 'taskProposal.error',
    },
    draft: {
        done: 'draft.done',
        failed: 'draft.failed',
        empty: 'draft.empty',
        emptyIcon: 'edit_note',
        error: 'draft.error',
    },
    suggestedAction: {
        done: 'suggestedAction.done',
        failed: 'suggestedAction.failed',
        empty: 'suggestedAction.empty',
        emptyIcon: 'task_alt',
        error: 'suggestedAction.error',
    },
};

const refusals: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'proposal.refused.unauthenticated',
    unauthorized: 'proposal.refused.unauthorized',
    unavailable: 'proposal.refused.unavailable',
    unreadable: 'proposal.refused.unreadable',
    missing: 'proposal.refused.missing',
};

interface Chip {
    readonly icon: IconName;
    readonly said: MessageKey;
    readonly tint: string;
}

const waitingChip: Chip = {
    icon: 'help',
    said: 'proposal.needsConfirmation',
    tint: 'border-accent-line bg-panel text-accent-deep',
};

const settledChips: Readonly<Record<Exclude<ProposalPhase, 'pending'>, Chip>> = {
    accepted: { icon: 'check_circle', said: 'proposal.done', tint: 'border-line-strong bg-sunken text-muted' },
    failed: { icon: 'error', said: 'proposal.notDone', tint: 'border-highlight-line bg-highlight text-text' },
    declined: { icon: 'block', said: 'proposal.declined', tint: 'border-line-strong bg-sunken text-muted' },
};

const looks: Readonly<Record<ProposalLook['kind'], { readonly icon: IconName; readonly said: MessageKey }>> = {
    calendar: { icon: 'calendar_month', said: 'proposal.openCalendar' },
    thread: { icon: 'forum', said: 'proposal.openThread' },
};

/**
 * One block that proposes something, in whichever phase it stands.
 *
 * @param kind Which of the four it is, which decides what it says once answered and what it says when it has nothing.
 * @param label What the block is called, already in the reader's language.
 * @param title What the proposal is about, which a declined card keeps as the one line it still shows.
 * @param quotesMail Whether that title is text out of mail, which is read out in the language it was written in.
 * @param confirmationRequired Whether it waits on somebody's decision, which only a suggestion may not.
 * @param citations The sources the block cites, which is where the row back to its thread comes from.
 * @param controls What answers it while it is pending.
 * @param state What the block is doing, which a proposal shares with every other block.
 * @param onRetry What the way out of a failed block does.
 */
export function ProposalCard({
    kind,
    label,
    title,
    quotesMail = false,
    confirmationRequired = true,
    citations,
    controls,
    state = 'ready',
    onRetry,
    children,
}: {
    readonly kind: ProposalKind;
    readonly label: string;
    readonly title: string;
    readonly quotesMail?: boolean;
    readonly confirmationRequired?: boolean;
    readonly citations: readonly string[];
    readonly controls: readonly ProposalControl[];
    readonly state?: AnswerBlockState;
    readonly onRetry?: (() => void) | undefined;
    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();
    const { sources } = useAnswerSources();
    const answering = useProposalAnswering();
    const wording = kindWording[kind];
    const lang = quotesMail ? '' : undefined;

    const phase = answering?.phase ?? null;
    const look = answering?.look ?? null;
    const refused = answering?.refused ?? null;
    const chip =
        phase === null || phase === 'pending' ? (confirmationRequired ? waitingChip : null) : settledChips[phase];
    const tone: BlockCardTone =
        phase === 'declined' ? 'declined' : phase === 'pending' && confirmationRequired ? 'awaiting' : 'settled';

    const offered: readonly ProposalControl[] =
        answering === null
            ? []
            : phase === 'pending'
              ? controls
              : phase === 'failed'
                ? [
                      {
                          said: translate('proposal.tryAgain'),
                          name: translate('proposal.tryAgainName', { title }),
                          primary: true,
                          run: () => {
                              answering.answer('accepted');
                          },
                      },
                  ]
                : [];

    const thread = citedThread(citations, sources);
    const leadsTo: readonly ProposalLook[] =
        look === null || phase === 'declined'
            ? []
            : kind === 'eventProposal'
              ? [{ kind: 'calendar' }]
              : thread === null
                ? []
                : [{ kind: 'thread', email: thread.email }];

    return (
        <AnswerBlockCard
            chip={
                chip === null ? null : (
                    <span
                        className={`ms-auto flex items-center gap-1.25 rounded-full border px-2.25 py-0.75 text-xs whitespace-nowrap ${chip.tint}`}
                    >
                        <Icon className="size-3.25" name={chip.icon} />
                        {translate(chip.said)}
                    </span>
                )
            }
            emptyIcon={wording.emptyIcon}
            label={label}
            note={
                state === 'empty' ? translate(wording.empty) : state === 'error' ? translate(wording.error) : undefined
            }
            retryName={translate('proposal.retryName', { block: label })}
            state={state}
            tone={tone}
            onRetry={onRetry}
        >
            {phase === 'declined' ? (
                <p className="flex flex-wrap items-center gap-2 text-base text-muted">
                    <Icon className="size-4" name="block" />
                    <s className="min-w-0" lang={lang}>
                        {title}
                    </s>
                    <span>{translate('proposal.declinedLine')}</span>
                </p>
            ) : (
                <>
                    {children}

                    {phase === 'accepted' || phase === 'failed' ? (
                        <p
                            className={`flex items-center gap-1.75 text-base text-pretty ${
                                phase === 'accepted' ? 'text-accent-deep' : 'text-text'
                            }`}
                        >
                            <Icon
                                className={`size-4 shrink-0 ${phase === 'failed' ? 'text-highlight-line' : ''}`}
                                name={phase === 'accepted' ? 'check_circle' : 'error'}
                            />
                            {translate(phase === 'accepted' ? wording.done : wording.failed)}
                        </p>
                    ) : null}

                    {offered.length === 0 ? null : (
                        <div className="flex flex-wrap items-center gap-2 border-t border-line-soft pt-2.5 pointer-coarse:flex-col pointer-coarse:items-stretch">
                            {offered.map((control) => (
                                <button
                                    key={control.said}
                                    aria-label={control.name}
                                    aria-disabled={control.held === true ? true : undefined}
                                    className={`inline-flex items-center justify-center gap-1.75 rounded-lg border px-3.75 py-2.25 text-md whitespace-nowrap transition disabled:opacity-60 aria-disabled:opacity-60 ${
                                        control.primary === true
                                            ? 'border-accent bg-accent font-semibold text-on-accent hover:bg-accent-strong'
                                            : 'border-line-strong bg-panel text-text-soft hover:bg-hover'
                                    }`}
                                    disabled={answering?.answering === true}
                                    type="button"
                                    onClick={() => {
                                        if (control.held !== true) {
                                            control.run();
                                        }
                                    }}
                                >
                                    {control.said}
                                </button>
                            ))}
                        </div>
                    )}

                    {refused === null ? null : (
                        <p role="alert" className="text-sm text-warning-text text-pretty">
                            {translate(refusals[refused])}
                        </p>
                    )}

                    {leadsTo.length === 0 ? null : (
                        <div className="flex flex-wrap gap-2 pointer-coarse:flex-col">
                            {leadsTo.map((where) => {
                                const drawn = looks[where.kind];

                                return (
                                    <button
                                        key={where.kind}
                                        aria-label={translate('proposal.lookName', { look: translate(drawn.said) })}
                                        className="inline-flex items-center gap-1.75 rounded-full border border-line-strong px-3 py-1.5 text-base whitespace-nowrap text-text-soft transition hover:border-accent hover:text-accent-deep"
                                        type="button"
                                        onClick={() => {
                                            look?.(where);
                                        }}
                                    >
                                        <Icon className="size-4 text-muted" name={drawn.icon} />
                                        {translate(drawn.said)}
                                    </button>
                                );
                            })}
                        </div>
                    )}
                </>
            )}
        </AnswerBlockCard>
    );
}

/** What a proposal is about, set as the line a reader decides on. */
export function ProposalTitle({
    children,
    quotesMail = false,
}: {
    readonly children: ReactNode;
    readonly quotesMail?: boolean;
}) {
    return (
        <p className="text-lg font-semibold tracking-tight text-pretty" lang={quotesMail ? '' : undefined}>
            {children}
        </p>
    );
}

/** The facts a proposal is corrected against before it is approved, one name and one value to a row. */
export function ProposalFields({
    fields,
}: {
    readonly fields: readonly { readonly name: string; readonly value: ReactNode }[];
}) {
    return (
        <dl className="flex flex-col gap-1.25">
            {fields.map((field) => (
                <div key={field.name} className="flex items-baseline gap-2.5">
                    <dt className="w-16.5 shrink-0 text-xs tracking-wide text-muted workspace:w-19.5">{field.name}</dt>
                    <dd className="min-w-0 flex-1 text-md text-text text-pretty">{field.value}</dd>
                </div>
            ))}
        </dl>
    );
}

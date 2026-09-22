// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useMemo, type ReactNode } from 'react';
import { understoodPlanSchemaVersion, type DeclaredSource } from '@mailfathom/client-backend';
import { Containment } from '../containment/Containment';
import { Icon } from '../controls/Icon';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { AnswerBlockCard, UnrecognisedAnswerBlock } from './AnswerBlockCard';
import { answerBlockRenderers, type AnswerBlockState, type AnswerBlockRenderers } from './answerBlocks';
import { AnswerSourcesContext } from './answerSources';
import type { ArrivedAnswerBlock, RunReadFailure } from './followedRun';

// Where a presentation plan becomes a screen. It is a host rather than a switch statement, and three properties are
// why.
//
// **It renders what has arrived.** Blocks reach it one at a time as the run composes them, so it draws the ones it
// holds and says that more is coming, and it is never blank while a run is working. Nothing here waits for a live
// connection: the follower above it reads the run's own record, so a run that finished before anybody looked draws in
// full and a hub that is down costs the interval rather than the answer.
//
// **One block cannot cost the run.** Every block is drawn inside a boundary of its own, so a renderer that throws on
// one block's data leaves that block saying so and the rest of the answer standing.
//
// **It names what it cannot draw.** The plan carries a revision of its own, ahead of this client's whenever a
// deployment is updated first, so a block type with no renderer here is named to the reader rather than dropped and a
// newer plan is drawn as far as it goes rather than refused. That is what makes a deployment updatable without
// breaking the clients in front of it, which is why it is drawn rather than assumed.

// What each way of failing to read the run becomes at the end of the answer. A failure is drawn as the state it
// actually is — one deployment out of reach, three things wrong with this client's standing or with what came back —
// and it carries its own sentence, because what somebody does next differs in each: sign in again, say the grant is
// missing, wait for the deployment, report a defect.
const readFailures: Readonly<Record<RunReadFailure, { readonly state: AnswerBlockState; readonly note: MessageKey }>> =
    {
        unauthenticated: { state: 'error', note: 'answerBlock.unauthenticated' },
        unauthorized: { state: 'error', note: 'answerBlock.unauthorized' },
        unavailable: { state: 'offline', note: 'answerBlock.offline' },
        unreadable: { state: 'error', note: 'answerBlock.unreadable' },
    };

/**
 * A presentation plan as far as it has been composed.
 *
 * @param blocks The blocks the run has published, in the order it published them.
 * @param sources The sources the run has declared, which every block draws the citations it rests on out of.
 * @param onFollowSource What following one source does, and nothing where this surface offers nowhere to follow it to.
 * @param running Whether more is still coming, which is what the place held at the end of the list stands for.
 * @param planSchemaVersion The revision the run's plan was written against, and `null` before the run has said.
 * @param failure Why the last read of the run did not answer, which is what the place still coming says instead of
 * waiting for ever, and `null` where it answered.
 * @param onRetry What reading the run again does, and nothing where the surface offers no way to.
 * @param renderers Which component draws which block type, which is the build's own registry unless a caller says
 * otherwise.
 * @param evidence What stands beside the answer on a wide window and beneath it on a narrow one.
 */
export function AnswerCanvas({
    blocks,
    sources,
    onFollowSource = null,
    running,
    planSchemaVersion,
    failure = null,
    onRetry,
    renderers = answerBlockRenderers,
    evidence,
}: {
    readonly blocks: readonly ArrivedAnswerBlock[];
    readonly sources: ReadonlyMap<string, DeclaredSource>;
    readonly onFollowSource?: ((source: string) => void) | null;
    readonly running: boolean;
    readonly planSchemaVersion: number | null;
    readonly failure?: RunReadFailure | null;
    readonly onRetry?: (() => void) | undefined;
    readonly renderers?: AnswerBlockRenderers;
    readonly evidence?: ReactNode;
}) {
    const { translate } = useLocalization();

    const ahead = planSchemaVersion !== null && planSchemaVersion > understoodPlanSchemaVersion;
    const unread = failure === null ? undefined : readFailures[failure];

    // Memoized against the two values it is composed of rather than rebuilt per render, because every block renderer
    // reads it: a fresh object each time would redraw the whole answer whenever anything above it rendered.
    const declared = useMemo(() => ({ sources, follow: onFollowSource }), [sources, onFollowSource]);

    return (
        <AnswerSourcesContext value={declared}>
            <div className="flex flex-col gap-5.5 desktop:grid desktop:grid-cols-[minmax(0,1.25fr)_minmax(0,1fr)] desktop:items-start">
                <div className="flex min-w-0 flex-col gap-4">
                    {ahead ? (
                        <p
                            className="flex items-start gap-1.75 rounded-md bg-warning-soft px-2.75 py-2 text-sm text-warning-text"
                            role="status"
                        >
                            <Icon className="mt-0.25 size-3.75" name="hourglass_top" />

                            {translate('answerCanvas.planAhead', {
                                plan: String(planSchemaVersion),
                                client: String(understoodPlanSchemaVersion),
                            })}
                        </p>
                    ) : null}

                    {/* An ordered list, because the order the run composed the blocks in is the order the answer reads in:
                    a screen reader is told how many there are and which one it is on, and the keyboard moves from one
                    card to the next without walking everything inside the one it is leaving. */}
                    {blocks.length === 0 && !running ? null : (
                        <ol aria-label={translate('answerCanvas.blocks')} className="flex flex-col gap-4">
                            {blocks.map((arrived) => (
                                <li key={arrived.sequence}>
                                    {/* Contained one block at a time, and told which block it stands around: a boundary
                                    holds its failure until something clears it, so one drawing a different block later
                                    would otherwise go on saying that the first one failed. */}
                                    <Containment drawing={String(arrived.sequence)} region="answer_block">
                                        <DrawnBlock block={arrived} renderers={renderers} />
                                    </Containment>
                                </li>
                            ))}

                            {running ? (
                                <li>
                                    {/* Reading again is the way out of exactly one of the four, for the reason
                                    `shell/ConnectionSummary.tsx` gives: a refused credential, a missing grant and an
                                    answer this client cannot parse each repeat identically on a second attempt, so
                                    offering the button there hands somebody an action that cannot work. */}
                                    <AnswerBlockCard
                                        label={translate('answerCanvas.stillComing')}
                                        note={unread === undefined ? undefined : translate(unread.note)}
                                        state={unread?.state ?? 'loading'}
                                        onRetry={failure === 'unavailable' ? onRetry : undefined}
                                    />
                                </li>
                            ) : null}
                        </ol>
                    )}

                    {/* A run that ended having composed nothing is still an answer somebody waited for, so it says so
                    rather than leaving the space where the blocks would have been blank. */}
                    {!running && blocks.length === 0 ? (
                        <p className="text-sm text-muted" role="status">
                            {translate('answerCanvas.nothing')}
                        </p>
                    ) : null}
                </div>

                {evidence}
            </div>
        </AnswerSourcesContext>
    );
}

/** One block drawn by whatever this build registered for its type, and named where it registered nothing. */
export function DrawnBlock({
    block,
    renderers,
}: {
    readonly block: ArrivedAnswerBlock;
    readonly renderers: AnswerBlockRenderers;
}) {
    // Read out of the registry rather than composed here: what is rendered is a component this build declared once, so
    // its identity is the same on every render and a block keeps whatever state its renderer holds.
    const Registered = block.block.type === null ? undefined : renderers[block.block.type];

    return Registered === undefined ? (
        <UnrecognisedAnswerBlock named={block.block.named} />
    ) : (
        <Registered block={block.block} />
    );
}

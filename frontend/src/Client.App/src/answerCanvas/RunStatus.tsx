// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import { Icon } from '../controls/Icon';
import type { IconName } from '../controls/icons';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { wordInstant } from '../localization/instants';
import type { Locale } from '../localization/locale';
import { useLocalization } from '../localization/useLocalization';
import type { FollowedAnswer, RunEnding } from './followedRun';
import type { RunStopping } from './useRunStopping';

// Everything around the blocks: that the run started, how far its retrieval has got, what it is spending, the control
// that stops it, and — when it is over — which of the eleven ways it ended.
//
// **A run has one sentence at a time, and it stands in one place.** The working line and the ending notice are the same
// region rather than two that replace each other, because it is a live region: a reader who cannot see the screen is
// told that retrieval moved, that the run was stopped, and that a ceiling refused it, by the text of one node changing
// rather than by nodes appearing somewhere they were not.
//
// **A ceiling is not an error.** A deployment that has spent what it allows answering to cost is behaving exactly as
// its operator configured it, so both spend refusals read as states and neither offers a retry — the period one names
// the instant its allowance returns, and the run one names the one thing the person can change.
//
// **Nothing here is money.** The counts are the three the run will actually be stopped by, each against its own
// ceiling, and the token figure is what this deployment observed rather than what a provider will bill.

/** How loudly the run's own state reads, which follows whether anything went wrong rather than whether it ended. */
type RunTone = 'working' | 'noted' | 'warned';

const toneShells: Readonly<Record<RunTone, string>> = {
    working: 'border border-line bg-panel text-text-soft',
    noted: 'bg-sunken text-text-soft',
    warned: 'bg-warning-soft text-warning-text',
};

/** What each ending says and how it reads. Exhaustive by its own type, so a new ending fails to compile until it has one. */
const endings: Readonly<
    Record<RunEnding, { readonly note: MessageKey; readonly icon: IconName; readonly tone: RunTone }>
> = {
    completed: { note: 'run.ended.completed', icon: 'check_circle', tone: 'noted' },
    cancelled: { note: 'run.ended.cancelled', icon: 'cancel', tone: 'noted' },
    gone: { note: 'run.ended.gone', icon: 'info', tone: 'noted' },
    periodSpent: { note: 'run.ended.periodSpent', icon: 'schedule', tone: 'noted' },
    runSpent: { note: 'run.ended.runSpent', icon: 'insights', tone: 'noted' },
    timedOut: { note: 'run.ended.timedOut', icon: 'hourglass_top', tone: 'warned' },
    stopped: { note: 'run.ended.stopped', icon: 'warning', tone: 'warned' },
    unavailable: { note: 'run.ended.unavailable', icon: 'cloud_off', tone: 'warned' },
    temporarilyUnavailable: { note: 'run.ended.temporarilyUnavailable', icon: 'cloud_off', tone: 'warned' },
    retrievalRefused: { note: 'run.ended.retrievalRefused', icon: 'warning', tone: 'warned' },
    failed: { note: 'run.ended.failed', icon: 'error', tone: 'warned' },
};

/**
 * The run's own state, and the chrome that says it.
 *
 * @param answer The run as far as it has been read.
 * @param stopping How far asking the deployment to stop it has got.
 * @param onStop What the control that stops the run does, and nothing where the surface offers none.
 */
export function RunStatus({
    answer,
    stopping = 'idle',
    onStop,
}: {
    readonly answer: FollowedAnswer;
    readonly stopping?: RunStopping;
    readonly onStop?: (() => void) | undefined;
}) {
    const { locale, translate } = useLocalization();

    const region = useRef<HTMLDivElement>(null);
    const offeredStopping = useRef(false);

    // The control goes when the run ends, which is the moment a keyboard would otherwise be left on nothing at all. It
    // is the move a contained block makes when it recovers, and for its reason.
    const offersStopping = answer.running && onStop !== undefined && stopping !== 'asking';

    useEffect(() => {
        if (offeredStopping.current && !offersStopping) {
            region.current?.focus();
        }

        offeredStopping.current = offersStopping;
    }, [offersStopping]);

    const ended = answer.running ? null : endings[answer.ending ?? 'completed'];
    const tone = ended?.tone ?? 'working';

    const allowanceReturns =
        ended !== null && answer.ending === 'periodSpent' ? wordInstant(answer.retryAt, locale, 'full') : null;

    return (
        <div className="flex flex-col gap-2" ref={region} tabIndex={-1}>
            <div className={`flex items-start gap-3 rounded-xl px-4.5 py-3.5 ${toneShells[tone]}`}>
                {ended === null ? (
                    /* A pulse rather than a spinner: what a reader is owed is the sentence beside it, and the mark is
                       there to say the sentence is still being made true. It carries no name, the sentence being the
                       whole of what it means, and `prefers-reduced-motion` stops it moving at all. */
                    <span aria-hidden className="mt-1.5 size-2 shrink-0 animate-working rounded-full bg-accent" />
                ) : (
                    <Icon className="mt-0.5 size-4.5 shrink-0" name={ended.icon} />
                )}

                {/* The live region is the whole of what the run says rather than its first sentence, so the instant a
                    refused allowance returns is announced with the refusal that named it instead of arriving silently
                    beneath it. */}
                <div className="flex min-w-0 flex-1 flex-col gap-1" role="status">
                    <p className="text-md text-pretty">
                        {ended === null ? translate(...working(answer, stopping, locale)) : translate(ended.note)}
                    </p>

                    {allowanceReturns === null ? null : (
                        <p className="text-sm">{translate('run.allowanceReturns', { at: allowanceReturns })}</p>
                    )}
                </div>

                {offersStopping ? <SecondaryButton label={translate('run.stop')} onActivate={onStop} /> : null}
            </div>

            <RunMeter answer={answer} />
        </div>
    );
}

/** A message and what fills its holes, in the shape `translate` is spread over. */
type Sentence = readonly [MessageKey, Readonly<Record<string, string>>?];

/** What the run is doing, which asking it to stop replaces: a stop somebody pressed is the newer fact about the run. */
function working(answer: FollowedAnswer, stopping: RunStopping, locale: Locale): Sentence {
    if (stopping === 'asking') {
        return ['run.stopping'];
    }

    if (stopping === 'refused') {
        return ['run.stopNotReached'];
    }

    const retrieval = answer.retrieval;

    return retrieval === null
        ? ['run.working']
        : [
              'run.retrieval',
              {
                  lookups: counted(locale, retrieval.lookupsRun),
                  planned: counted(locale, retrieval.lookupsPlanned),
                  passages: counted(locale, retrieval.passagesFound),
              },
          ];
}

/**
 * What the run costs, against what one question may cost on this deployment, and what answered it.
 *
 * It sits with the run rather than in the frame, which is the placement the cost decision settles: a figure in
 * permanent chrome teaches people to watch a meter while they work, and one on the answer is legible exactly when
 * somebody asks whether that answer was expensive.
 */
function RunMeter({ answer }: { readonly answer: FollowedAnswer }) {
    const { locale, translate } = useLocalization();

    const envelope = answer.envelope;
    if (envelope === null) {
        return null;
    }

    const spend = answer.spend;
    const ceilings = envelope.ceilings;

    return (
        <div className="flex flex-wrap gap-x-3 gap-y-0.5 px-1 text-sm text-faint">
            {/* A deployment that answers no questions names no endpoint, and says so by ending the run rather than by
                a line here with nothing in it. */}
            {envelope.endpointAlias === '' ? null : (
                <p>
                    {envelope.publishedModel === ''
                        ? translate('run.answeredBy', { endpoint: envelope.endpointAlias })
                        : translate('run.answeredByModel', {
                              endpoint: envelope.endpointAlias,
                              model: envelope.publishedModel,
                          })}
                </p>
            )}

            {spend === null ? null : (
                <p>
                    {translate('run.spend', {
                        calls: counted(locale, spend.providerCalls),
                        mostCalls: counted(locale, ceilings.providerCalls),
                        tokens: counted(locale, spend.tokens),
                        mostTokens: counted(locale, ceilings.tokens),
                        characters: counted(locale, spend.retrievedCharacters),
                        mostCharacters: counted(locale, ceilings.retrievedCharacters),
                        messages: counted(locale, spend.messagesRetrieved),
                    })}
                </p>
            )}

            {/* Said once the run is over rather than while it works: a count that is still moving is plainly partial,
                and a final one is the one somebody would otherwise read as the whole of what was charged. */}
            {answer.running || spend === null ? null : <p>{translate('run.spendFloor')}</p>}
        </div>
    );
}

function counted(locale: Locale, count: number): string {
    return new Intl.NumberFormat(locale).format(count);
}

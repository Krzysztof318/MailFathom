// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    RunFollowingSchedule,
} from '@mailfathom/client-backend';
import * as discovery from '../../../../tests/fixtures/discovery';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { DiscoverSpace } from './DiscoverSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

// Not catalogue entries: each stands for whatever the frame composed and handed this space.
const theQuestionField = 'The question field the frame handed this space.';
const theConnection = 'The connection the frame handed this space.';

// Nothing is read again on the follower's own interval, so what each case drives is the read on mount and whatever a
// press causes.
const neverPolls: RunFollowingSchedule = { wait: () => new Promise<void>(() => undefined) };

// What the corpus answers, over the three routes one run is reached at. A case asserting what was asked reads the list.
function deploymentAnswering(tail: unknown = discovery.runTail(0)): {
    readonly transport: MailFathomTransport;
    readonly asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            if (request.method === 'POST') {
                return Promise.resolve({ status: 202, body: JSON.stringify(discovery.runStarted), headers: {} });
            }

            if (request.method === 'DELETE') {
                return Promise.resolve({ status: 204, body: '', headers: {} });
            }

            return Promise.resolve({ status: 200, body: JSON.stringify(tail), headers: {} });
        },
    };
}

function screenOf(transport: MailFathomTransport, onOpenMessage: (storedEmailId: string) => void = () => undefined) {
    return render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <DiscoverSpace
                    session={session}
                    transport={transport}
                    accounts={[]}
                    intent={<p>{theQuestionField}</p>}
                    status={<p>{theConnection}</p>}
                    schedule={neverPolls}
                    onOpenMessage={onOpenMessage}
                />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

/** Asks one of the questions the idle screen offers, which is how one is asked with the field standing outside. */
function askASuggestion(): string {
    const offered = within(screen.getByRole('list')).getAllByRole('button');
    const first = offered[0];

    if (first === undefined) {
        throw new Error('The idle screen offered no question to ask.');
    }

    const asked = first.textContent;

    fireEvent.click(first);

    return asked;
}

/** The answer's own sentence, which is what every case below reads as the result having arrived. */
const theAnswer = /The bays were confirmed at four/;

describe('DiscoverSpace', () => {
    it('opens on the questions it offers rather than on an empty screen', () => {
        const { transport, asked } = deploymentAnswering();

        screenOf(transport);

        expect(within(screen.getByRole('list')).getAllByRole('button').length).toBeGreaterThan(0);
        expect(asked).toHaveLength(0);
    });

    it('carries the field and the connection the frame handed it', () => {
        const { transport } = deploymentAnswering();

        screenOf(transport);

        expect(screen.getByText(theQuestionField)).toBeDefined();
        expect(screen.getByText(theConnection)).toBeDefined();
    });

    it('asks the question a suggestion states and draws the answer the run composed', async () => {
        const { transport, asked } = deploymentAnswering();

        screenOf(transport);
        const question = askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(theAnswer)).toBeDefined();
        });

        expect(JSON.parse(asked.find((request) => request.method === 'POST')?.body ?? '{}')).toMatchObject({ question });
    });

    it('draws a run that has ended as ended rather than as still working', async () => {
        const { transport } = deploymentAnswering();

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText('This run has finished.')).toBeDefined();
        });
    });

    it('offers stopping a run that is still working, and asks the deployment to stop the run itself', async () => {
        const { transport, asked } = deploymentAnswering(discovery.runWorkingTail(0));

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByRole('button', { name: 'Cancel run' })).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel run' }));

        await waitFor(() => {
            expect(asked.some((request) => request.method === 'DELETE')).toBe(true);
        });

        // What had already arrived is what somebody asked for, so stopping keeps it rather than clearing the screen.
        expect(screen.getByText(theAnswer)).toBeDefined();
    });

    it('opens the passage a citation rests on without the answer going anywhere', async () => {
        const { transport } = deploymentAnswering();

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(theAnswer)).toBeDefined();
        });

        const citation = screen.getAllByRole('button', { name: /A newsletter from Example/ })[0];

        if (citation === undefined) {
            throw new Error('The answer drew no citation to follow.');
        }

        fireEvent.click(citation);

        // The panel stands beside the answer whether or not anything is in it, so what says a citation was followed is
        // that it stopped resting and offers the way back. What it then draws of the passage is its own test's subject.
        const panel = within(screen.getByRole('region', { name: 'Evidence' }));

        await waitFor(() => {
            expect(panel.getByRole('button', { name: 'Back to the answer' })).toBeDefined();
        });

        expect(screen.getByText(theAnswer)).toBeDefined();
    });

    it('goes back to the questions it offers when another one is asked for', async () => {
        const { transport } = deploymentAnswering();

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(theAnswer)).toBeDefined();
        });

        fireEvent.click(screen.getByRole('button', { name: 'New question' }));

        expect(screen.queryByText(theAnswer)).toBeNull();
        expect(within(screen.getByRole('list')).getAllByRole('button').length).toBeGreaterThan(0);
    });

    it('says why a question was not accepted rather than drawing a run nothing started', async () => {
        const refusing: MailFathomTransport = (request) =>
            Promise.resolve(
                request.method === 'POST'
                    ? { status: 503, body: '', headers: {} }
                    : { status: 200, body: JSON.stringify(discovery.runTail(0)), headers: {} },
            );

        screenOf(refusing);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(/This question did not reach the server/)).toBeDefined();
        });
    });

    it('draws the rest of a plan carrying a block type it does not know, and names that type', async () => {
        const { transport } = deploymentAnswering(discovery.runWithAnUnknownBlock);

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(theAnswer)).toBeDefined();
        });

        expect(screen.getByText('type: riskScore')).toBeDefined();
    });

    // The endings past the resting one, each of which is a screen somebody has to be able to read and none of which can
    // be reached from a finished run by pressing anything.
    it.each([
        [
            'a run somebody stopped',
            discovery.runStopped,
            'You cancelled this run. What had arrived is kept; what it had spent stays spent.',
        ],
        [
            'a run that ended for a reason it does not publish',
            discovery.runFailed,
            'This run ended for a reason it does not publish. An operator can read why in the server logs.',
        ],
        [
            'a deployment whose allowance for the period is spent',
            discovery.runPeriodSpent,
            'This server has spent what it allows answering to cost for now. Nothing about your question caused it.',
        ],
    ])('says how %s ended', async (_unused, tail, ending) => {
        const { transport } = deploymentAnswering(tail);

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(ending)).toBeDefined();
        });
    });

    it('draws a plan a revision ahead of this client as far as it goes, and says so', async () => {
        const { transport } = deploymentAnswering(discovery.runAheadOfTheClient);

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText(/newer plan schema version \(v9\)/)).toBeDefined();
        });

        expect(screen.getByText(theAnswer)).toBeDefined();
    });

    it('says a run composed nothing rather than leaving the answer blank', async () => {
        const { transport } = deploymentAnswering(discovery.runComposedNothing);

        screenOf(transport);
        askASuggestion();

        await waitFor(() => {
            expect(screen.getByText('This run produced nothing to show.')).toBeDefined();
        });
    });
});

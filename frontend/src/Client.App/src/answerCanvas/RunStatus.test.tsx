// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import { LocalizationProvider } from '../localization/Localization';
import { nothingRead, type FollowedAnswer } from './followedRun';
import { RunStatus } from './RunStatus';
import type { RunStopping } from './useRunStopping';

const envelope = {
    ceilings: { retrievedCharacters: 20_000, providerCalls: 8, tokens: 80_000 },
    endpointAlias: 'house',
    publishedModel: '',
};

const spend = { providerCalls: 3, tokens: 1_200, retrievedCharacters: 900, messagesRetrieved: 4 };

function renderStatus(
    answer: Partial<FollowedAnswer> = {},
    status: { readonly stopping?: RunStopping; readonly onStop?: () => void } = {},
) {
    return render(
        <LocalizationProvider>
            <RunStatus answer={{ ...nothingRead, envelope, ...answer }} {...status} />
        </LocalizationProvider>,
    );
}

describe('RunStatus', () => {
    it('says the run is working rather than drawing a bare spinner', () => {
        renderStatus();

        expect(screen.getByRole('status').textContent).toContain('Working through your mail');
    });

    it('says how far retrieval has got once a lookup of the plan settles', () => {
        renderStatus({
            retrieval: { lookupsRun: 2, lookupsRefused: 0, lookupsPlanned: 5, passagesFound: 41 },
        });

        expect(screen.getByRole('status').textContent).toContain('2 of 5');
    });

    it('offers a control that stops the run while it is working', () => {
        const stop = vi.fn();
        renderStatus({}, { onStop: stop });

        fireEvent.click(screen.getByRole('button', { name: 'Cancel run' }));

        expect(stop).toHaveBeenCalledOnce();
    });

    it('says the run is being stopped, and offers the control no second time', () => {
        renderStatus({}, { stopping: 'asking', onStop: () => undefined });

        expect(screen.getByRole('status').textContent).toContain('Cancelling this run');
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('says the run is still going when the stop did not land, and offers the control again', () => {
        renderStatus({}, { stopping: 'refused', onStop: () => undefined });

        expect(screen.getByRole('status').textContent).toContain('still going');
        expect(screen.getByRole('button', { name: 'Cancel run' })).toBeDefined();
    });

    it('states that a stopped run was stopped rather than that it failed', () => {
        renderStatus({ running: false, ending: 'cancelled', spend }, { onStop: () => undefined });

        expect(screen.getByRole('status').textContent).toContain('You cancelled this run');
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('names when a spent period turns over instead of offering a retry that will be refused', () => {
        renderStatus({
            running: false,
            ending: 'periodSpent',
            retryAt: '2026-09-21T13:00:00+00:00',
            spend,
        });

        expect(screen.getByRole('status').textContent).toContain('allows answering to cost');
        expect(screen.getByText(/Questions can be asked again/u)).toBeDefined();
        expect(screen.queryByRole('button')).toBeNull();
    });

    it('names no instant for the ceiling one question reaches, because asking again reaches it the same way', () => {
        renderStatus({ running: false, ending: 'runSpent', spend });

        expect(screen.getByRole('status').textContent).toContain('one question may cost');
        expect(screen.queryByText(/Questions can be asked again/u)).toBeNull();
    });

    it('says a run the deployment no longer holds is gone rather than letting it disappear', () => {
        renderStatus({ running: false, ending: 'gone' });

        expect(screen.getByRole('status').textContent).toContain('no longer holds this run');
    });

    it('reads every count against the ceiling that will stop the run, while it is still working', () => {
        renderStatus({ spend, retrieval: { lookupsRun: 1, lookupsRefused: 0, lookupsPlanned: 5, passagesFound: 9 } });

        expect(screen.getByText(/3 of 8 calls/u).textContent).toContain('from 4 messages');
    });

    it('says the counted tokens are a floor once the run is over, and not while it is still moving', () => {
        const { unmount } = renderStatus({ spend });

        expect(screen.queryByText(/floor rather than a bill/u)).toBeNull();

        unmount();
        renderStatus({ running: false, ending: 'completed', spend });

        expect(screen.getByText(/floor rather than a bill/u)).toBeDefined();
    });

    it('names the endpoint the operator chose, and the model only where they declared one', () => {
        const { unmount } = renderStatus({ spend });

        expect(screen.getByText('Answered by house')).toBeDefined();

        unmount();
        renderStatus({ envelope: { ...envelope, publishedModel: 'gpt-4o' }, spend });

        expect(screen.getByText('Answered by house (gpt-4o)')).toBeDefined();
    });

    it('says nothing about cost before the run has said what it may spend', () => {
        renderStatus({ envelope: null });

        expect(screen.queryByText(/Answered by/u)).toBeNull();
    });

    it('takes the keyboard off the control that stopped the run when the run ends', () => {
        const { rerender } = render(
            <LocalizationProvider>
                <RunStatus answer={{ ...nothingRead, envelope }} onStop={() => undefined} />
            </LocalizationProvider>,
        );

        screen.getByRole('button', { name: 'Cancel run' }).focus();

        rerender(
            <LocalizationProvider>
                <RunStatus
                    answer={{ ...nothingRead, envelope, running: false, ending: 'completed' }}
                    onStop={() => undefined}
                />
            </LocalizationProvider>,
        );

        expect(document.activeElement).toBe(screen.getByRole('status').closest('[tabindex="-1"]'));
    });
});

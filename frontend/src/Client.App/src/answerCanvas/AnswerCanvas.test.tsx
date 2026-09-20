// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AnswerBlock } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { AnswerCanvas } from './AnswerCanvas';
import type { AnswerBlockRenderers } from './answerBlocks';
import type { ArrivedAnswerBlock } from './followedRun';

// Which of the drawn blocks is throwing this render. A module-level value rather than a prop, for the reason
// `Containment.test.tsx` keeps one: a retry changes no prop, so a region that recovers is one that stops throwing
// between two renders of the same element.
let failingBlock: string | null = null;

function arrival(sequence: number, named: string, known = true): ArrivedAnswerBlock {
    const block: AnswerBlock = { type: known ? 'answer' : null, named };

    return { sequence, block };
}

// What a registered renderer draws: the block's own name, which is what every assertion below reaches it by. It is a
// value rather than a sentence, so no catalogue entry is owed for it.
function Drawn({ block }: { readonly block: AnswerBlock }) {
    if (failingBlock === block.named) {
        throw new TypeError('A block this renderer cannot draw.');
    }

    return <p>{block.named}</p>;
}

const renderers: AnswerBlockRenderers = { answer: Drawn };

function renderCanvas(blocks: readonly ArrivedAnswerBlock[], canvas: Partial<Parameters<typeof AnswerCanvas>[0]> = {}) {
    return render(
        <LocalizationProvider>
            <AnswerCanvas blocks={blocks} planSchemaVersion={2} renderers={renderers} running={false} {...canvas} />
        </LocalizationProvider>,
    );
}

describe('AnswerCanvas', () => {
    beforeEach(() => {
        failingBlock = null;

        // React writes every contained failure to the console, which is a page of stack per test here and says
        // nothing a failing expectation would not.
        vi.spyOn(console, 'error').mockImplementation(() => undefined);
    });

    afterEach(() => {
        vi.restoreAllMocks();
    });

    it('draws each block through the renderer registered for its type', () => {
        renderCanvas([arrival(1, 'answer')]);

        expect(screen.getByText('answer')).toBeDefined();
    });

    it('announces the blocks in the order the run composed them', () => {
        renderCanvas([arrival(1, 'answer'), arrival(4, 'answer')]);

        expect(screen.getAllByRole('listitem')).toHaveLength(2);
    });

    it('names a block type this build has no renderer for, and draws the rest of the answer', () => {
        renderCanvas([arrival(1, 'RiskScore', false), arrival(2, 'answer')]);

        expect(screen.getByText('type: RiskScore')).toBeDefined();
        expect(screen.getByText('answer')).toBeDefined();
    });

    it('leaves the rest of the answer standing when one block fails to draw', () => {
        failingBlock = 'broken';

        renderCanvas([{ sequence: 1, block: { type: 'answer', named: 'broken' } }, arrival(2, 'answer')]);

        expect(screen.getByText('answer')).toBeDefined();
    });

    it('says in place that the block which failed did', () => {
        failingBlock = 'broken';

        renderCanvas([{ sequence: 1, block: { type: 'answer', named: 'broken' } }]);

        expect(screen.getByRole('alert')).toBeDefined();
    });

    it('holds a place for what is still coming while the run works', () => {
        renderCanvas([arrival(1, 'answer')], { running: true });

        expect(screen.getByRole('article', { name: 'More of this answer' })).toBeDefined();
    });

    it('holds none once the run has stopped', () => {
        renderCanvas([arrival(1, 'answer')]);

        expect(screen.queryByRole('article', { name: 'More of this answer' })).toBeNull();
    });

    it('says the deployment is out of reach rather than waiting on it in silence', () => {
        renderCanvas([arrival(1, 'answer')], { running: true, failure: 'unavailable' });

        expect(screen.getByText('No connection to the server — this block cannot be loaded.')).toBeDefined();
    });

    it('says which way the read failed rather than reporting all four as no connection', () => {
        renderCanvas([], { running: true, failure: 'unauthenticated' });

        expect(
            screen.getByText('The session ended while this answer was being read. Sign in again to see the rest.'),
        ).toBeDefined();
    });

    it('offers reading the run again when it could not be reached', () => {
        const retry = vi.fn();
        renderCanvas([], { running: true, failure: 'unavailable', onRetry: retry });

        fireEvent.click(screen.getByRole('button', { name: 'Try again' }));

        expect(retry).toHaveBeenCalledTimes(1);
    });

    it('offers no second attempt at a failure that would repeat identically', () => {
        for (const failure of ['unauthenticated', 'unauthorized', 'unreadable'] as const) {
            const { unmount } = renderCanvas([], { running: true, failure, onRetry: vi.fn() });

            expect(screen.queryByRole('button', { name: 'Try again' })).toBeNull();

            unmount();
        }
    });

    it('says a plan written against a newer revision was drawn as far as it went', () => {
        renderCanvas([arrival(1, 'answer')], { planSchemaVersion: 3 });

        expect(
            screen.getByText(
                'This result uses a newer plan schema version (v3) than this app supports (v2). Some new block types may have been skipped.',
            ),
        ).toBeDefined();
        expect(screen.getByText('answer')).toBeDefined();
    });

    it.each([2, 1, null])('says nothing about a plan at revision %s', (planSchemaVersion) => {
        renderCanvas([arrival(1, 'answer')], { planSchemaVersion });

        expect(screen.queryByText(/newer plan schema version/u)).toBeNull();
    });

    it('says a run that composed nothing produced nothing, rather than drawing a blank', () => {
        renderCanvas([]);

        expect(screen.getByText('This run produced nothing to show.')).toBeDefined();
    });
});

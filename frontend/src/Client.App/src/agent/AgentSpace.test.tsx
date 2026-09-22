// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it } from 'vitest';
import type {
    ClientRequest,
    ClientSession,
    MailFathomTransport,
    RunFollowingSchedule,
} from '@mailfathom/client-backend';
import * as agent from '../../../../tests/fixtures/agent';
import { LocalizationProvider } from '../localization/Localization';
import { WorkspaceProvider } from '../workspace/Workspace';
import { AgentSpace } from './AgentSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const conversations = 'https://mail.example.invalid/api/client/agent/conversations';

// Nothing is read again on the follower's own interval, so what each case drives is the read on opening and whatever a
// press causes.
const neverPolls: RunFollowingSchedule = { wait: () => new Promise<void>(() => undefined) };

// The history column is the wide composition's, and the setup answers every width query `false`, so the cases state
// the wide window they are about and put the declared one back afterwards.
const declaredMatchMedia = Object.getOwnPropertyDescriptor(window, 'matchMedia');

beforeEach(() => {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.includes('min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
});

afterEach(() => {
    if (declaredMatchMedia !== undefined) {
        Object.defineProperty(window, 'matchMedia', declaredMatchMedia);
    }
});

// What the corpus answers over the Agent's routes. A case asserting what was asked reads the list; `posting` is what a
// question or a steer is answered with.
function deploymentAnswering(posting: { readonly status: number; readonly body: string } | null = null): {
    readonly transport: MailFathomTransport;
    readonly asked: ClientRequest[];
} {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            if (request.method === 'POST') {
                const run = request.path.includes('/runs/') ? agent.composingQuestionId : agent.answeredQuestionId;

                return Promise.resolve({
                    ...(posting ?? { status: 202, body: JSON.stringify(agent.messagePosted(run)) }),
                    headers: {},
                });
            }

            if (request.method === 'DELETE') {
                return Promise.resolve({ status: 204, body: '', headers: {} });
            }

            const answer =
                request.path === conversations
                    ? agent.agentHistory
                    : request.path.startsWith(`${conversations}/${agent.composingConversationId}`)
                      ? agent.composingConversation
                      : agent.answeredConversation;

            return Promise.resolve({ status: 200, body: JSON.stringify(answer), headers: {} });
        },
    };
}

function screenOf(transport: MailFathomTransport) {
    return render(
        <LocalizationProvider>
            <WorkspaceProvider>
                <AgentSpace session={session} transport={transport} status={null} schedule={neverPolls} />
            </WorkspaceProvider>
        </LocalizationProvider>,
    );
}

async function opened(title: string): Promise<void> {
    fireEvent.click(await screen.findByRole('option', { name: new RegExp(title) }));
}

describe('AgentSpace', () => {
    it('lists the conversations the history holds', async () => {
        screenOf(deploymentAnswering().transport);

        const history = await screen.findByRole('listbox', { name: 'Conversations with the agent' });

        expect(
            within(history)
                .getAllByRole('option')
                .map((row) => row.textContent),
        ).toEqual([
            expect.stringContaining('What is waiting on me today'),
            expect.stringContaining('How many bays were confirmed'),
        ]);
    });

    it('draws a conversation it opened as the question and the answer it was given', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('How many bays were confirmed');

        const thread = screen.getByRole('log', { name: 'Conversation with the agent' });

        expect(await within(thread).findByText('Four bays were confirmed, held to the end of the week.')).toBeDefined();
        expect(within(thread).getByText('How many bays were confirmed?')).toBeDefined();
        expect(within(thread).queryByRole('status', { name: /Reading the confirmation/ })).toBeNull();
    });

    it('says what the agent is doing for as long as the answer is being composed', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('What is waiting on me today');

        const said = await screen.findByText('Looking through today’s mail');

        expect(said.getAttribute('role')).toBe('status');
        expect(said.getAttribute('aria-live')).toBe('polite');
    });

    it('offers Cancel only while an answer is being composed', async () => {
        screenOf(deploymentAnswering().transport);

        expect(screen.getByRole('button', { name: 'Cancel — nothing is running' }).getAttribute('aria-disabled')).toBe(
            'true',
        );

        await opened('What is waiting on me today');

        const cancel = await screen.findByRole('button', { name: 'Stop what the agent is doing' });

        expect(cancel.getAttribute('aria-disabled')).toBe('false');
    });

    it('stops the answer being composed when Cancel is pressed', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        await opened('What is waiting on me today');
        fireEvent.click(await screen.findByRole('button', { name: 'Stop what the agent is doing' }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'DELETE').map((request) => request.path)).toEqual([
                `${conversations}/${agent.composingConversationId}/runs/${agent.composingQuestionId}`,
            ]);
        });
    });

    it('steers the answer being composed rather than asking again when something is sent during it', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        await opened('What is waiting on me today');
        await screen.findByText('Looking through today’s mail');
        fireEvent.change(screen.getByRole('textbox', { name: 'Tell the agent what to do' }), {
            target: { value: 'Only this week' },
        });
        fireEvent.click(screen.getByRole('button', { name: /Send/ }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'POST').map((request) => request.path)).toEqual([
                `${conversations}/${agent.composingConversationId}/runs/${agent.composingQuestionId}/messages`,
            ]);
        });
    });

    it('opens a new conversation of its own naming with the first question asked', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        fireEvent.change(screen.getByRole('textbox', { name: 'Tell the agent what to do' }), {
            target: { value: 'How many bays?' },
        });
        fireEvent.click(screen.getByRole('button', { name: /Send/ }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'POST')).toHaveLength(1);
        });

        const [posted] = asked.filter((request) => request.method === 'POST');

        expect(posted?.path).toMatch(new RegExp(`^${conversations}/[0-9a-f-]{36}/messages$`));
        expect(JSON.parse(posted?.body ?? '{}')).toMatchObject({ text: 'How many bays?' });
    });

    it('says why a question was not sent and keeps what was typed', async () => {
        screenOf(deploymentAnswering({ status: 503, body: '' }).transport);

        const field = screen.getByRole('textbox', { name: 'Tell the agent what to do' });
        fireEvent.change(field, { target: { value: 'How many bays?' } });
        fireEvent.click(screen.getByRole('button', { name: /Send/ }));

        expect(await screen.findByRole('alert')).toBeDefined();
        expect((field as HTMLInputElement).value).toBe('How many bays?');
    });

    it('deletes a conversation only once the confirmation naming it is answered', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        fireEvent.keyDown(await screen.findByRole('option', { name: /How many bays were confirmed/ }), {
            key: 'ContextMenu',
        });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Delete conversation' }));

        const confirmation = await screen.findByRole('dialog', { name: 'Delete this conversation?' });

        expect(within(confirmation).getByText(/How many bays were confirmed/)).toBeDefined();
        expect(asked.some((request) => request.method === 'DELETE')).toBe(false);

        fireEvent.click(within(confirmation).getByRole('button', { name: 'Delete' }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'DELETE').map((request) => request.path)).toEqual([
                `${conversations}/${agent.answeredConversationId}`,
            ]);
        });
    });
});

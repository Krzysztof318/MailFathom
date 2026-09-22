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
import type { AgentHandOver } from '../routing/agentHandOver';
import { WorkspaceProvider } from '../workspace/Workspace';
import { AgentSpace } from './AgentSpace';

const session: ClientSession = { baseAddress: 'https://mail.example.invalid', authorization: 'Basic dGVzdA==' };

const conversations = 'https://mail.example.invalid/api/client/agent/conversations';

// Nothing is read again on the follower's own interval, so what each case drives is the read on opening and whatever a
// press causes.
const neverPolls: RunFollowingSchedule = { wait: () => new Promise<void>(() => undefined) };

// Every case here draws the wide, desktop composition — the history column and the tab strip are its — so every
// `min-width` query answers `true`, and the declared stub is put back afterwards.
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

            if (request.method === 'DELETE' || request.method === 'PUT') {
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

function drawn(transport: MailFathomTransport, handOver: AgentHandOver | null = null) {
    return (
        <LocalizationProvider>
            <WorkspaceProvider>
                <AgentSpace
                    session={session}
                    transport={transport}
                    status={null}
                    handOver={handOver}
                    schedule={neverPolls}
                />
            </WorkspaceProvider>
        </LocalizationProvider>
    );
}

function screenOf(transport: MailFathomTransport) {
    return render(drawn(transport));
}

const handedThread: AgentHandOver = {
    scope: { kind: 'thread', subject: '0198f4a1-0000-7000-8000-00000000b001' },
    title: 'Hall lease',
};

async function sent(asked: readonly ClientRequest[], question: string): Promise<unknown> {
    fireEvent.change(screen.getByRole('textbox', { name: 'Tell the agent what to do' }), {
        target: { value: question },
    });
    fireEvent.click(screen.getByRole('button', { name: /Send/ }));

    await waitFor(() => {
        expect(asked.filter((request) => request.method === 'POST')).toHaveLength(1);
    });

    return JSON.parse(asked.find((request) => request.method === 'POST')?.body ?? '{}');
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

    it('puts one conversation in the tab order and walks the history with the arrow keys', async () => {
        screenOf(deploymentAnswering().transport);

        const history = await screen.findByRole('listbox', { name: 'Conversations with the agent' });
        const [first, second] = within(history).getAllByRole('option');

        expect(
            within(history)
                .getAllByRole('option')
                .map((row) => row.tabIndex),
        ).toEqual([0, -1]);

        fireEvent.keyDown(first ?? history, { key: 'ArrowDown' });

        expect(document.activeElement).toBe(second);
        expect(second?.tabIndex).toBe(0);
    });

    it('puts focus back on the history when the selection is put down', async () => {
        screenOf(deploymentAnswering().transport);

        const row = await screen.findByRole('option', { name: /How many bays were confirmed/ });
        fireEvent.keyDown(row, { key: 'ContextMenu' });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Select conversations' }));
        fireEvent.click(await screen.findByRole('button', { name: 'Cancel selection' }));

        expect(screen.queryByRole('toolbar')).toBeNull();
        expect(document.activeElement?.getAttribute('role')).toBe('option');
    });

    it('draws the conversations held open as tabs the arrow keys walk', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('How many bays were confirmed');
        await opened('What is waiting on me today');

        const strip = await screen.findByRole('tablist', { name: 'Open conversations' });
        const tabs = within(strip).getAllByRole('tab');

        expect(tabs.map((tab) => tab.getAttribute('aria-selected'))).toEqual(['false', 'true']);

        fireEvent.keyDown(tabs[1] ?? strip, { key: 'ArrowLeft' });

        expect(document.activeElement).toBe(tabs[0]);
    });

    it('puts focus in the field when closing a tab takes the strip with it', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('How many bays were confirmed');
        await opened('What is waiting on me today');

        fireEvent.click(await screen.findByRole('button', { name: 'Close What is waiting on me today' }));

        expect(screen.queryByRole('tablist')).toBeNull();
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Tell the agent what to do' }));
        });
    });

    it('lets go of a conversation the deployment no longer holds', async () => {
        const { transport } = deploymentAnswering();
        screenOf((request) =>
            request.method === 'GET' && request.path.startsWith(`${conversations}/${agent.answeredConversationId}`)
                ? Promise.resolve({ status: 404, body: '', headers: {} })
                : transport(request),
        );

        const row = await screen.findByRole('option', { name: /How many bays were confirmed/ });
        fireEvent.click(row);

        expect(row.getAttribute('aria-current')).toBe('true');

        await waitFor(() => {
            expect(row.getAttribute('aria-current')).toBeNull();
        });
        expect(screen.getByText(/I can write a message, lay out your day/)).toBeDefined();
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('draws a conversation it opened as the question and the answer it was given', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('How many bays were confirmed');

        const thread = screen.getByRole('log', { name: 'Conversation with the agent' });

        expect(await within(thread).findByText('Four bays were confirmed, held to the end of the week.')).toBeDefined();
        expect(within(thread).getByText('How many bays were confirmed?')).toBeDefined();
        expect(within(thread).queryByRole('status', { name: /Reading the confirmation/ })).toBeNull();
    });

    it('asks what the last answer suggested, as it reads, when the suggestion is pressed', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        await opened('How many bays were confirmed');
        const suggested = await screen.findByRole('list', { name: 'Proposed next' });
        fireEvent.click(within(suggested).getByRole('button', { name: 'Ask the agent to: Who confirmed the bays?' }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'POST')).toHaveLength(1);
        });

        const [posted] = asked.filter((request) => request.method === 'POST');

        expect(posted?.path).toBe(`${conversations}/${agent.answeredConversationId}/messages`);
        expect(JSON.parse(posted?.body ?? '{}')).toMatchObject({ text: 'Who confirmed the bays?' });
    });

    it('asks a suggestion once however often it is pressed while it is being sent', async () => {
        const { transport } = deploymentAnswering();
        let posts = 0;
        let answer: () => void = () => undefined;
        screenOf((request) => {
            if (request.method !== 'POST') {
                return transport(request);
            }

            posts += 1;

            return new Promise((resolve) => {
                answer = () => {
                    void transport(request).then(resolve);
                };
            });
        });

        await opened('How many bays were confirmed');
        const suggestion = within(await screen.findByRole('list', { name: 'Proposed next' })).getByRole('button', {
            name: 'Ask the agent to: Who confirmed the bays?',
        });
        fireEvent.click(suggestion);
        fireEvent.click(suggestion);

        expect(suggestion.hasAttribute('disabled')).toBe(true);
        expect(posts).toBe(1);

        answer();
        await waitFor(() => {
            expect(suggestion.isConnected && suggestion.hasAttribute('disabled')).toBe(false);
        });
    });

    it('draws what an opened conversation already suggested without animating it in', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('How many bays were confirmed');
        const suggested = await screen.findByRole('list', { name: 'Proposed next' });

        expect(suggested.parentElement?.className).not.toContain('animate-arrival');
    });

    it('suggests nothing under an answer still being composed', async () => {
        screenOf(deploymentAnswering().transport);

        await opened('What is waiting on me today');
        await screen.findByText('Looking through today’s mail');

        expect(screen.queryByRole('list', { name: 'Proposed next' })).toBeNull();
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

    it('opens a new conversation carrying what another space handed over, and asks under it', async () => {
        const { transport, asked } = deploymentAnswering();
        const { rerender } = render(drawn(transport));

        await opened('How many bays were confirmed');
        rerender(drawn(transport, handedThread));

        expect(screen.getByRole('heading', { level: 1 }).textContent).toBe('Agent');
        expect(screen.getByText('Context: thread “Hall lease”')).toBeDefined();
        expect(await sent(asked, 'Who owes what?')).toMatchObject({
            text: 'Who owes what?',
            scope: { kind: 'Thread', subject: handedThread.scope.subject },
        });
    });

    it('asks about everything again once the context is cleared', async () => {
        const { transport, asked } = deploymentAnswering();
        const { rerender } = render(drawn(transport));
        rerender(drawn(transport, handedThread));

        fireEvent.click(screen.getByRole('button', { name: 'Remove context' }));

        expect(screen.queryByText(/Context:/u)).toBeNull();
        expect(await sent(asked, 'Who owes what?')).not.toHaveProperty('scope');
    });

    it('opens on nothing a hand-over made before it was drawn, which belongs to an earlier screen', () => {
        render(drawn(deploymentAnswering().transport, handedThread));

        expect(screen.queryByText(/Context:/u)).toBeNull();
    });

    it('draws no chip for a conversation opened with nothing handed over', () => {
        screenOf(deploymentAnswering().transport);

        expect(screen.queryByText(/Context:/u)).toBeNull();
        expect(screen.queryByRole('button', { name: 'Remove context' })).toBeNull();
    });

    it('says why a question was not sent and keeps what was typed', async () => {
        screenOf(deploymentAnswering({ status: 503, body: '' }).transport);

        const field = screen.getByRole('textbox', { name: 'Tell the agent what to do' });
        fireEvent.change(field, { target: { value: 'How many bays?' } });
        fireEvent.click(screen.getByRole('button', { name: /Send/ }));

        expect(await screen.findByRole('alert')).toBeDefined();
        expect((field as HTMLInputElement).value).toBe('How many bays?');
    });

    it('keeps what was typed while a question was being sent', async () => {
        const { transport } = deploymentAnswering();
        let answer: () => void = () => undefined;
        screenOf((request) =>
            request.method === 'POST'
                ? new Promise((resolve) => {
                      answer = () => {
                          void transport(request).then(resolve);
                      };
                  })
                : transport(request),
        );

        const field = screen.getByRole('textbox', { name: 'Tell the agent what to do' });
        fireEvent.change(field, { target: { value: 'How many bays?' } });
        fireEvent.click(screen.getByRole('button', { name: /Send/ }));
        fireEvent.change(field, { target: { value: 'And by when?' } });
        answer();

        await waitFor(() => {
            expect(screen.getByRole('button', { name: /Send/ }).hasAttribute('disabled')).toBe(false);
        });
        expect((field as HTMLInputElement).value).toBe('And by when?');
    });

    it('puts an unsent draft down when another conversation is opened', async () => {
        screenOf(deploymentAnswering().transport);

        const field = screen.getByRole('textbox', { name: 'Tell the agent what to do' });
        fireEvent.change(field, { target: { value: 'Meant for a new conversation' } });

        await opened('How many bays were confirmed');

        expect((field as HTMLInputElement).value).toBe('');
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

    it('draws the archived conversations folded under the rest, and opens them on a press', async () => {
        screenOf(deploymentAnswering().transport);

        const section = await screen.findByRole('button', { name: 'Show the archived conversations' });

        expect(section.textContent).toBe('Archived (1)');
        expect(section.getAttribute('aria-expanded')).toBe('false');
        expect(screen.queryByRole('listbox', { name: 'Archived conversations' })).toBeNull();

        fireEvent.click(section);

        const archive = screen.getByRole('listbox', { name: 'Archived conversations' });

        expect(section.getAttribute('aria-expanded')).toBe('true');
        expect(
            within(archive)
                .getAllByRole('option')
                .map((row) => row.textContent),
        ).toEqual([expect.stringContaining('Invoice 08/2026 — what is missing')]);
    });

    it('archives a conversation from its menu and puts it down where it was open', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        await opened('How many bays were confirmed');
        const row = screen.getByRole('option', { name: /How many bays were confirmed/ });

        expect(row.getAttribute('aria-current')).toBe('true');

        fireEvent.keyDown(row, { key: 'ContextMenu' });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'PUT').map((request) => request.path)).toEqual([
                `${conversations}/${agent.answeredConversationId}/archive`,
            ]);
        });
        await waitFor(() => {
            expect(
                screen.getByRole('option', { name: /How many bays were confirmed/ }).getAttribute('aria-current'),
            ).toBeNull();
        });
    });

    it('keeps focus in the history when the row that held it is archived out of the list', async () => {
        const { transport } = deploymentAnswering();
        const archived = new Set<string>();
        screenOf((request) => {
            if (request.method === 'PUT') {
                archived.add(agent.answeredConversationId);
            }

            if (request.method === 'GET' && request.path === conversations) {
                const listed = agent.agentHistory.conversations.map((line) =>
                    archived.has(line.id) ? { ...line, archived: true } : line,
                );

                return Promise.resolve({ status: 200, body: JSON.stringify({ conversations: listed }), headers: {} });
            }

            return transport(request);
        });

        const row = await screen.findByRole('option', { name: /How many bays were confirmed/ });
        row.focus();
        fireEvent.keyDown(row, { key: 'ContextMenu' });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));

        await waitFor(() => {
            expect(screen.queryByRole('option', { name: /How many bays were confirmed/ })).toBeNull();
        });
        await waitFor(() => {
            expect(document.activeElement).toBe(screen.getByRole('option', { name: /What is waiting on me today/ }));
        });
    });

    it('puts down a conversation the deployment no longer holds when archiving it finds it gone', async () => {
        const { transport } = deploymentAnswering();
        screenOf((request) =>
            request.method === 'PUT' ? Promise.resolve({ status: 404, body: '', headers: {} }) : transport(request),
        );

        await opened('How many bays were confirmed');
        fireEvent.keyDown(screen.getByRole('option', { name: /How many bays were confirmed/ }), { key: 'ContextMenu' });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));

        await waitFor(() => {
            expect(
                screen.getByRole('option', { name: /How many bays were confirmed/ }).getAttribute('aria-current'),
            ).toBeNull();
        });
        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('offers restoring rather than archiving on an archived conversation', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        fireEvent.click(await screen.findByRole('button', { name: 'Show the archived conversations' }));
        fireEvent.keyDown(screen.getByRole('option', { name: /Invoice 08\/2026/ }), { key: 'ContextMenu' });

        expect(screen.queryByRole('menuitem', { name: 'Archive' })).toBeNull();

        fireEvent.click(await screen.findByRole('menuitem', { name: 'Restore from archive' }));

        await waitFor(() => {
            expect(asked.filter((request) => request.method === 'DELETE').map((request) => request.path)).toEqual([
                `${conversations}/0198f4a1-0000-7000-8000-00000000a9e3/archive`,
            ]);
        });
    });

    it('archives every conversation picked out from the selection bar', async () => {
        const { transport, asked } = deploymentAnswering();
        screenOf(transport);

        fireEvent.keyDown(await screen.findByRole('option', { name: /How many bays were confirmed/ }), {
            key: 'ContextMenu',
        });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Select conversations' }));
        fireEvent.click(screen.getByRole('option', { name: /What is waiting on me today/ }));
        fireEvent.click(within(screen.getByRole('toolbar')).getByRole('button', { name: 'Archive' }));

        await waitFor(() => {
            expect(
                asked
                    .filter((request) => request.method === 'PUT')
                    .map((request) => request.path)
                    .toSorted(),
            ).toEqual([
                `${conversations}/${agent.answeredConversationId}/archive`,
                `${conversations}/${agent.composingConversationId}/archive`,
            ]);
        });
        expect(screen.queryByRole('toolbar')).toBeNull();
    });

    it('says why the archive did not change', async () => {
        const { transport } = deploymentAnswering();
        screenOf((request) =>
            request.method === 'PUT' ? Promise.resolve({ status: 503, body: '', headers: {} }) : transport(request),
        );

        fireEvent.keyDown(await screen.findByRole('option', { name: /How many bays were confirmed/ }), {
            key: 'ContextMenu',
        });
        fireEvent.click(await screen.findByRole('menuitem', { name: 'Archive' }));

        expect((await screen.findByRole('alert')).textContent).toBe(
            'The deployment could not be reached, so the archive did not change. Try again.',
        );
    });
});

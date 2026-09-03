// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailAccount, MailFathomTransport } from '@mailfathom/client-backend';
import { AttachmentUploadContext, type AttachmentUpload } from '../deployment/attachmentUpload';
import { LocalizationProvider } from '../localization/Localization';
import { Composer } from './Composer';
import type { ComposerOpening } from './composition';
import { rememberComposition, rememberedComposition } from './keptComposition';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const messageId = '00000000-0000-4000-8000-000000000000';

const work: MailAccount = {
    id: 'work',
    displayName: 'Work',
    synchronizationState: 'Synchronized',
    lastSynchronizedAt: null,
    behind: false,
};

const home: MailAccount = { ...work, id: 'home', displayName: 'Home' };

// What the deployment answers a save with, which is the draft as it now stands.
function draftBody(overrides: Readonly<Record<string, unknown>> = {}): string {
    return JSON.stringify({
        draftId: 'd1',
        account: 'work',
        subject: 'Invoice',
        recipients: [],
        attachments: [],
        revision: 1,
        sizeOctets: 512,
        ...overrides,
    });
}

// The message an answer is written against, which the composer reads before it can address one.
function messageBody(): string {
    return JSON.stringify({
        storedEmailId: messageId,
        account: 'work',
        folder: 'INBOX',
        threadId: null,
        sizeOctets: 40_960,
        headers: {
            subject: 'Quarterly invoice',
            sentAt: '2026-08-31T09:41:00+00:00',
            receivedAt: '2026-08-31T09:41:10+00:00',
            participants: [
                { role: 'From', address: 'billing@example.invalid', displayName: 'Billing' },
                { role: 'To', address: 'reader@example.invalid', displayName: null },
                { role: 'Cc', address: 'auditor@example.invalid', displayName: null },
            ],
            messageId: 'abc@example.invalid',
            inReplyTo: null,
            references: [],
        },
        body: { availability: 'Readable', plainText: true, html: false },
        sender: { authorAuthentication: 'Authenticated', deploymentTrust: 'Trusted', authenticatedDomain: null },
        attachments: [],
        carried: null,
        unread: true,
        flagged: false,
        answered: false,
    });
}

/** What each route answers with, so one test states only the answer it is about. */
interface Answers {
    readonly message?: { readonly status: number; readonly body: string };
    readonly save?: { readonly status: number; readonly body: string };
    readonly send?: { readonly status: number; readonly body: string };
    readonly withdrawal?: { readonly status: number; readonly body: string };
}

function deployment(answers: Answers = {}): { transport: MailFathomTransport; asked: ClientRequest[] } {
    const asked: ClientRequest[] = [];

    return {
        asked,
        transport: (request) => {
            asked.push(request);

            const answer = answerFor(request, answers);

            return Promise.resolve({ status: answer.status, body: answer.body, headers: {} });
        },
    };
}

function answerFor(request: ClientRequest, answers: Answers): { status: number; body: string } {
    if (request.path.includes('/messages/')) {
        return answers.message ?? { status: 200, body: messageBody() };
    }

    if (request.path.endsWith('/send')) {
        return answers.send ?? { status: 200, body: JSON.stringify({ outgoingEmail: 'o1' }) };
    }

    if (request.path.endsWith('/outbox/cancellation')) {
        return answers.withdrawal ?? { status: 200, body: JSON.stringify({ outcome: 'Accepted' }) };
    }

    return answers.save ?? { status: 200, body: draftBody() };
}

const uploadsOneFile: AttachmentUpload = () =>
    Promise.resolve({
        status: 200,
        body: JSON.stringify({
            attachmentId: 'a1',
            fileName: 'invoice.pdf',
            mediaType: 'application/pdf',
            sizeOctets: 2_048,
        }),
        headers: {},
    });

function drawComposer(
    opening: ComposerOpening = { kind: 'new' },
    answers: Answers = {},
    accounts: readonly MailAccount[] = [work],
    online = true,
): { closed: ReturnType<typeof vi.fn>; asked: ClientRequest[]; upload: AttachmentUpload } {
    const closed = vi.fn();
    const { transport, asked } = deployment(answers);

    render(
        <LocalizationProvider>
            <AttachmentUploadContext value={uploadsOneFile}>
                <Composer
                    session={session}
                    transport={transport}
                    accounts={accounts}
                    opening={opening}
                    online={online}
                    onClosed={closed}
                />
            </AttachmentUploadContext>
        </LocalizationProvider>,
    );

    return { closed, asked, upload: uploadsOneFile };
}

function write(label: string, text: string): void {
    fireEvent.change(screen.getByLabelText(label), { target: { value: text } });
}

function address(text: string): void {
    fireEvent.change(screen.getByLabelText('To'), { target: { value: text } });
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Enter' });
}

// The confirmation's own control reads *Send anyway* where the message would go out without something, which every
// case below that is not about the confirmation itself leaves it doing.
function confirmSend(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));

    fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: /^Send( anyway)?$/u }));
}

afterEach(() => {
    window.sessionStorage.clear();
});

describe('Composer, a message of its own', () => {
    it('opens empty, named for what is being written', () => {
        drawComposer();

        expect(screen.getByRole('region', { name: 'New message' })).toBeDefined();
        expect(screen.getByLabelText('Message')).toHaveProperty('value', '');
    });

    it('offers no account to send from where there is one, that being the only answer', () => {
        drawComposer();

        expect(screen.queryByLabelText('From')).toBeNull();
    });

    it('offers the account to send from where there is more than one', () => {
        drawComposer({ kind: 'new' }, {}, [work, home]);

        expect(screen.getByLabelText('From')).toHaveProperty('value', 'work');
    });

    it('keeps what is being written on this device, so a reload returns to it', async () => {
        drawComposer();

        write('Message', 'Here it is.');

        await waitFor(() => {
            expect(rememberedComposition()?.words).toBe('Here it is.');
        });
    });

    it('starts from what this tab was writing rather than from an empty message', () => {
        rememberComposition({
            answering: null,
            account: 'work',
            subject: 'Invoice',
            to: ['ada@example.invalid'],
            cc: [],
            bcc: [],
            words: 'Half a sentence',
        });

        drawComposer();

        expect(screen.getByLabelText('Message')).toHaveProperty('value', 'Half a sentence');
        expect(screen.getByRole('button', { name: 'Remove ada@example.invalid from To' })).toBeDefined();
    });

    it('offers the copy headers only once they are asked for, the design drawing one row', () => {
        drawComposer();

        expect(screen.queryByLabelText('Cc')).toBeNull();

        fireEvent.click(screen.getByRole('button', { name: 'Write a copy or a blind copy as well' }));

        expect(screen.getByLabelText('Cc')).toBeDefined();
        expect(screen.getByLabelText('Bcc')).toBeDefined();
    });

    it('asks the send question from the shortcut the design draws, rather than sending from it', () => {
        const { asked } = drawComposer();

        address('ada@example.invalid');
        fireEvent.keyDown(screen.getByLabelText('Message'), { key: 'Enter', ctrlKey: true });

        expect(screen.getByRole('dialog').textContent).toContain('Send this message?');
        expect(asked).toHaveLength(0);
    });

    it('files the draft in the owner’s own drafts when that is asked for, and says it did', async () => {
        const { asked } = drawComposer();

        write('Subject', 'Invoice');
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        expect(await screen.findByText('Draft filed in your own drafts.')).toBeDefined();

        const save = asked.at(-1);

        expect(save?.method).toBe('POST');
        expect(save?.path).toBe('https://mail.example.invalid/api/client/drafts');
        expect(JSON.parse(save?.body ?? '{}')).toMatchObject({ account: 'work', subject: 'Invoice' });
    });

    it('revises the draft it already wrote rather than filing a second one', async () => {
        const { asked } = drawComposer();

        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
        expect(await screen.findByText('Draft filed in your own drafts.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        await waitFor(() => {
            expect(asked.at(-1)?.method).toBe('PUT');
        });

        expect(asked.at(-1)?.path).toBe('https://mail.example.invalid/api/client/drafts/d1');
    });

    it('sends nothing until the send is confirmed', () => {
        const { asked } = drawComposer();

        address('ada@example.invalid');
        fireEvent.click(screen.getByRole('button', { name: 'Send' }));

        expect(asked).toHaveLength(0);
    });

    it('queues the message once the send is confirmed, and offers to take it back', async () => {
        drawComposer();

        address('ada@example.invalid');
        confirmSend();

        expect(await screen.findByText('Queued to go out.')).toBeDefined();
        expect(screen.getByRole('button', { name: 'Take it back' })).toBeDefined();
    });

    it('takes a queued send back, and says what became of it', async () => {
        drawComposer();

        address('ada@example.invalid');
        confirmSend();

        fireEvent.click(await screen.findByRole('button', { name: 'Take it back' }));

        expect(await screen.findByText('Taken back before it went out.')).toBeDefined();
    });

    it('says a message already going out could not be taken back', async () => {
        drawComposer(
            { kind: 'new' },
            { withdrawal: { status: 200, body: JSON.stringify({ outcome: 'StageDoesNotAllowIt' }) } },
        );

        address('ada@example.invalid');
        confirmSend();

        fireEvent.click(await screen.findByRole('button', { name: 'Take it back' }));

        expect(await screen.findByText('It has gone out, so it cannot be taken back.')).toBeDefined();
    });

    it.each([
        [56_003, 'This deployment does not send mail. Whoever runs it can turn sending on.'],
        [
            53_006,
            'Your deployment refused one of the addresses. Whoever runs it decides which addresses mail may go to.',
        ],
        [
            57_002,
            'A spending ceiling has been reached, so nothing goes out until the window it counts over turns over or whoever runs the deployment raises it.',
        ],
        [
            59_001,
            'Screening refused what this message carries. Changing what it says, or what it attaches, is what would change that.',
        ],
        [
            59_002,
            'Part of this message could not be screened, so it was not sent. Taking off what could not be read is what would change that.',
        ],
        [81_001, 'Screening is not answering, so nothing goes out until it does. The message is still here.'],
        [12, 'Your deployment refused to send it. Whoever runs it can say why from its own log.'],
    ])('says what refused the send and what would change it: %i', async (code, said) => {
        drawComposer({ kind: 'new' }, { send: { status: 409, body: JSON.stringify({ errorCode: code }) } });

        address('ada@example.invalid');
        confirmSend();

        expect(await screen.findByText(said)).toBeDefined();
    });

    it('says which of the four failures a save met, rather than that something went wrong', async () => {
        drawComposer({ kind: 'new' }, { save: { status: 401, body: '' } });

        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        expect(await screen.findByText(/no longer signed in/u)).toBeDefined();
    });

    it('says what is kept while the machine is offline rather than offering a send that cannot happen', () => {
        drawComposer({ kind: 'new' }, {}, [work], false);

        expect(screen.getByText(/This machine is offline/u)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Send' }).hasAttribute('disabled')).toBe(true);
    });

    it('stages a file against the draft and draws what it is called and how large', async () => {
        drawComposer();

        const file = new File(['0123'], 'invoice.pdf', { type: 'application/pdf' });
        const picker = document.querySelector<HTMLInputElement>('input[type=file]');

        if (picker === null) {
            throw new Error('The composer drew no file picker to attach with.');
        }

        await act(() => {
            fireEvent.change(picker, { target: { files: [file] } });

            return Promise.resolve();
        });

        const staged = await screen.findByRole('list', { name: 'Attached files' });

        expect(staged.textContent).toContain('invoice.pdf');
        expect(within(staged).getByRole('button', { name: 'Remove invoice.pdf' })).toBeDefined();
    });

    it('takes a staged file off the draft at the deployment as well as off the screen', async () => {
        const { asked } = drawComposer();

        const picker = document.querySelector<HTMLInputElement>('input[type=file]');

        if (picker === null) {
            throw new Error('The composer drew no file picker to attach with.');
        }

        await act(() => {
            fireEvent.change(picker, { target: { files: [new File(['0123'], 'invoice.pdf')] } });

            return Promise.resolve();
        });

        const staged = await screen.findByRole('list', { name: 'Attached files' });

        await act(() => {
            fireEvent.click(within(staged).getByRole('button', { name: 'Remove invoice.pdf' }));

            return Promise.resolve();
        });

        expect(screen.queryByRole('list', { name: 'Attached files' })).toBeNull();
        expect(asked.some((request) => request.method === 'DELETE' && request.path.endsWith('/attachments/a1'))).toBe(
            true,
        );
    });

    it('closes without asking where nothing has been written', () => {
        const { closed } = drawComposer();

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        expect(closed).toHaveBeenCalledTimes(1);
        expect(rememberedComposition()).toBeNull();
    });

    it('gives the draft up as well as the words when what was written is discarded', async () => {
        const { asked, closed } = drawComposer();

        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
        expect(await screen.findByText('Draft filed in your own drafts.')).toBeDefined();

        write('Message', 'Never mind.');
        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Discard' }));

        await waitFor(() => {
            expect(asked.at(-1)?.method).toBe('DELETE');
        });

        expect(closed).toHaveBeenCalledTimes(1);
    });

    it('stays open where the draft it was asked to keep could not be filed', async () => {
        const { closed } = drawComposer({ kind: 'new' }, { save: { status: 503, body: '' } });

        write('Message', 'Keep this.');
        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
        fireEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Save draft' }));

        expect(await screen.findByText(/did not answer/u)).toBeDefined();
        expect(closed).not.toHaveBeenCalled();
    });
});

describe('Composer, an answer', () => {
    const replying: ComposerOpening = { kind: 'answer', answers: 'everyone', storedEmailId: messageId };

    it('says it is reading the message before there is anything to write in', () => {
        drawComposer(replying);

        expect(screen.getByText('Reading the message you are answering…')).toBeDefined();
    });

    it('opens addressed to the conversation, under the subject it answers', async () => {
        drawComposer(replying);

        expect(await screen.findByRole('button', { name: 'Remove billing@example.invalid from To' })).toBeDefined();
        expect(screen.getByText('Re: Quarterly invoice')).toBeDefined();
    });

    it('does not offer the subject of an answer for editing, the deployment writing it', async () => {
        drawComposer(replying);

        await screen.findByText('Re: Quarterly invoice');

        expect(screen.queryByRole('textbox', { name: 'Subject' })).toBeNull();
    });

    it('states the message it answers rather than an account and a subject', async () => {
        const { asked } = drawComposer(replying);

        await screen.findByText('Re: Quarterly invoice');
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        await waitFor(() => {
            expect(asked.at(-1)?.method).toBe('POST');
        });

        expect(JSON.parse(asked.at(-1)?.body ?? '{}')).toMatchObject({
            answeredEmailId: messageId,
            answers: 'everyone',
        });
    });

    it('offers the people in the conversation to complete an address from', async () => {
        drawComposer(replying);

        await screen.findByText('Re: Quarterly invoice');

        const offered = [...document.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));

        expect(offered).toContain('auditor@example.invalid');
    });

    it('says the message could not be read, and offers the way out, rather than an empty composer', async () => {
        const { closed } = drawComposer(replying, { message: { status: 503, body: '' } });

        expect(await screen.findByText(/did not answer/u)).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        expect(closed).toHaveBeenCalledTimes(1);
    });

    it('reads nothing where this tab was already writing that answer', () => {
        rememberComposition({
            answering: { storedEmailId: messageId, answers: 'everyone' },
            account: 'work',
            subject: 'Re: Quarterly invoice',
            to: ['billing@example.invalid'],
            cc: [],
            bcc: [],
            words: 'Half an answer',
        });

        drawComposer(replying);

        expect(screen.getByLabelText('Message')).toHaveProperty('value', 'Half an answer');
    });
});

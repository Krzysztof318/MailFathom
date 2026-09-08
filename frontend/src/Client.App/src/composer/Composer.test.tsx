// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, ClientSession, MailAccount, MailFathomTransport } from '@mailfathom/client-backend';
import { AttachmentUploadContext, type AttachmentUpload } from '../deployment/attachmentUpload';
import { LocalizationProvider } from '../localization/Localization';
import { ToastsProvider } from '../toasts/Toasts';
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
    readonly discard?: { readonly status: number; readonly body: string };
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

    if (request.method === 'DELETE' && answers.discard !== undefined) {
        return answers.discard;
    }

    return answers.save ?? { status: 200, body: draftBody() };
}

/** One file as the deployment records it, which is both what an upload answers and what a save lists back. */
const stagedFile = {
    attachmentId: 'a1',
    fileName: 'invoice.pdf',
    mediaType: 'application/pdf',
    sizeOctets: 2_048,
};

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
    upload: AttachmentUpload = uploadsOneFile,
): { closed: ReturnType<typeof vi.fn>; asked: ClientRequest[]; upload: AttachmentUpload } {
    const closed = vi.fn();
    const { transport, asked } = deployment(answers);

    render(
        <LocalizationProvider>
            <ToastsProvider>
                <AttachmentUploadContext value={upload}>
                    <Composer
                        session={session}
                        transport={transport}
                        accounts={accounts}
                        opening={opening}
                        online={online}
                        onClosed={closed}
                    />
                </AttachmentUploadContext>
            </ToastsProvider>
        </LocalizationProvider>,
    );

    return { closed, asked, upload };
}

// The composer itself. Under jsdom the window reads narrow, where the composer stands over the whole screen and is
// therefore a dialog rather than a region — so this names it by the label it carries in either shape.
function composerFrame(): HTMLElement {
    return screen.getByRole('dialog', { name: /^(New message|Reply|Reply to everyone|Forward)$/u });
}

/** The send confirmation, named rather than taken by role alone: the composer around it is a dialog too. */
function sendQuestion(): HTMLElement {
    return screen.getByRole('dialog', { name: 'Send this message?' });
}

function write(label: string, text: string): void {
    fireEvent.change(screen.getByLabelText(label), { target: { value: text } });
}

/** The words of the message, which are an editable region rather than a field and are typed into as one. */
function writeWords(text: string): void {
    const words = screen.getByRole('textbox', { name: 'Message' });

    words.textContent = text;
    fireEvent.input(words);
}

function wordsWritten(): string {
    return screen.getByRole('textbox', { name: 'Message' }).textContent;
}

function address(text: string): void {
    fireEvent.change(screen.getByLabelText('To'), { target: { value: text } });
    fireEvent.keyDown(screen.getByLabelText('To'), { key: 'Enter' });
}

// The confirmation's own control reads *Send anyway* where the message would go out without something, which every
// case below that is not about the confirmation itself leaves it doing.
function confirmSend(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Send' }));

    fireEvent.click(within(sendQuestion()).getByRole('button', { name: /^Send( anyway)?$/u }));
}

describe('Composer, a message of its own', () => {
    it('opens empty, named for what is being written', () => {
        drawComposer();

        expect(composerFrame()).toBeDefined();
        expect(wordsWritten()).toBe('');
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

        writeWords('Here it is.');

        await waitFor(() => {
            expect(rememberedComposition()?.words).toEqual([{ text: 'Here it is.' }]);
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
            words: [{ text: 'Half a sentence' }],
        });

        drawComposer();

        expect(wordsWritten()).toBe('Half a sentence');
        expect(screen.getByRole('button', { name: 'Remove ada@example.invalid from To' })).toBeDefined();
    });

    it('draws every header on one grid, each field named in the column beside what is written in it', () => {
        drawComposer({ kind: 'new' }, {}, [work, home]);

        fireEvent.click(screen.getByRole('button', { name: 'Write a copy or a blind copy as well' }));

        // Every row is a name bound to the one thing written under it, which is what makes the header a grid rather
        // than four rows each starting somewhere of its own. Where each column falls is decided by looking at the
        // screen against the design; what a test can hold is that no row states its name any other way.
        for (const header of ['From', 'To', 'Cc', 'Bcc', 'Subject']) {
            expect(screen.getByLabelText(header)).toBeDefined();
        }
    });

    it('sends both parts of one message: the markup its author gave it and the reading every client has', async () => {
        const { asked } = drawComposer();

        address('ada@example.invalid');
        write('Subject', 'The quarterly figures');
        writeWords('They are attached.');
        confirmSend();

        await waitFor(() => {
            expect(asked.some((request) => request.path.endsWith('/send'))).toBe(true);
        });

        const written = JSON.parse(asked.find((request) => request.method === 'POST')?.body ?? '{}') as Record<
            string,
            unknown
        >;

        expect(written['plainTextBody']).toBe('They are attached.');
        expect(written['htmlBody']).toBe('They are attached.');
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
        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Message' }), { key: 'Enter', ctrlKey: true });

        expect(sendQuestion().textContent).toContain('Send this message?');
        expect(asked).toHaveLength(0);
    });

    it('files the draft in the user’s own drafts when that is asked for, and says it did', async () => {
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
            'Part of this message could not be screened, so it was not sent. Try again in case the read ran out of time; if it is refused again, taking off what could not be read is what would change that.',
        ],
        [
            59_003,
            'One of the attached files could not be read, so nothing screened what would have gone out with it and the message was not sent. Try again in case the read ran out of time; if it is refused again, sending without that file, or attaching it in a form that can be read, is what would change that.',
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

    // The deployment screens the draft book by the same rules as the outbox and answers the same codes, so a save can
    // meet a refusal rather than a failure of the request — and the words have to be a save's, not a send's.
    it.each([
        [
            59_003,
            'One of the attached files could not be read, so nothing screened what would have been filed with it and the message was not filed. Try again in case the read ran out of time; if it is refused again, saving without that file, or attaching it in a form that can be read, is what would change that.',
        ],
        [
            59_001,
            'Screening refused what this message carries, so it was not filed. Changing what it says, or what it attaches, is what would change that. Nothing you wrote has been lost.',
        ],
    ])('says a refused save in the words of a save rather than of a send: %i', async (code, said) => {
        drawComposer({ kind: 'new' }, { save: { status: 409, body: JSON.stringify({ errorCode: code }) } });

        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        expect(await screen.findByText(said)).toBeDefined();
    });

    // A send writes the draft before it posts it, and the deployment screens the draft book by the same rules, so the
    // refusal a send meets is the one the write answered — never the send's own request. Worded as a save's, it would
    // tell somebody who pressed Send that the message was not filed, and never that it was not sent.
    it('says a send refused at the write it performs first in the words of a send', async () => {
        drawComposer({ kind: 'new' }, { save: { status: 409, body: JSON.stringify({ errorCode: 59_003 }) } });

        address('ada@example.invalid');
        confirmSend();

        expect(
            await screen.findByText(
                'One of the attached files could not be read, so nothing screened what would have gone out with it and the message was not sent. Try again in case the read ran out of time; if it is refused again, sending without that file, or attaching it in a form that can be read, is what would change that.',
            ),
        ).toBeDefined();
    });

    it('says what is kept while the machine is offline rather than offering a send that cannot happen', () => {
        drawComposer({ kind: 'new' }, {}, [work], false);

        expect(screen.getByText(/This machine is offline/u)).toBeDefined();
        expect(screen.getByRole('button', { name: 'Send' }).hasAttribute('disabled')).toBe(true);
    });

    it('offers no second send once one is queued, from the control or from the shortcut', async () => {
        drawComposer();

        address('ada@example.invalid');
        write('Subject', 'The quarterly figures');
        writeWords('They are attached.');
        confirmSend();

        expect(await screen.findByText('Queued to go out.')).toBeDefined();

        // The message has gone as far as this screen can send it, so neither way of asking may start a second one.
        expect(screen.getByRole('button', { name: 'Send' }).hasAttribute('disabled')).toBe(true);

        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Message' }), { key: 'Enter', ctrlKey: true });

        expect(screen.queryByRole('dialog', { name: 'Send this message?' })).toBeNull();
    });

    // The send is the one act whose outcome has to reach somebody who has already turned to something else, so it is
    // said in a toast standing over whatever they turned to rather than at the foot of a window they are done with.
    // The toast that follows the send becomes the outcome in place, which is why both are read from one surface.
    it('says a send is on its way where somebody who looked away would still read it, and then what it came to', async () => {
        drawComposer();

        address('ada@example.invalid');
        write('Subject', 'The quarterly figures');
        writeWords('They are attached.');
        confirmSend();

        expect(await screen.findByText('Sending your message…')).toBeDefined();
        expect(await screen.findByText('Message sent')).toBeDefined();
        expect(screen.queryByText('Sending your message…')).toBeNull();
    });

    // A refusal is titled there and said at the foot of the composer, where the words the deployment refused still
    // are: a toast stands for a few seconds, and what somebody has to act on cannot be the thing that goes away.
    it('titles a refused send in the toast and leaves what would change it beside what was written', async () => {
        drawComposer({ kind: 'new' }, { send: { status: 409, body: JSON.stringify({ errorCode: 56_003 }) } });

        address('ada@example.invalid');
        confirmSend();

        expect(await screen.findByText('Message not sent')).toBeDefined();
        expect(
            screen.getByText('This deployment does not send mail. Whoever runs it can turn sending on.'),
        ).toBeDefined();
    });

    it('leaves nothing that would write over a queued send and take the way to withdraw with it', async () => {
        drawComposer();

        address('ada@example.invalid');
        write('Subject', 'The quarterly figures');
        writeWords('They are attached.');
        confirmSend();

        expect(await screen.findByText('Queued to go out.')).toBeDefined();

        // Both of these say what is happening the moment they are pressed, so either one would draw over the queued
        // state and leave the message going out with nothing on the screen able to stop it.
        expect(screen.getByRole('button', { name: 'Save draft' }).hasAttribute('disabled')).toBe(true);
        expect(screen.getByRole('button', { name: 'Attach' }).hasAttribute('disabled')).toBe(true);
        expect(screen.getByRole('button', { name: 'Take it back' })).toBeDefined();
    });

    it('stays open and says so where the deployment would not give the draft up', async () => {
        const { closed } = drawComposer({ kind: 'new' }, { discard: { status: 503, body: '' } });

        writeWords('Something worth keeping.');
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        expect(await screen.findByText('Draft filed in your own drafts.')).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
        fireEvent.click(
            within(screen.getByRole('dialog', { name: 'Discard this message?' })).getByRole('button', {
                name: 'Discard',
            }),
        );

        expect(await screen.findByText(/did not answer/u)).toBeDefined();
        expect(closed).not.toHaveBeenCalled();
    });

    it('files one draft for two saves asked for before the first has answered', async () => {
        const { asked } = drawComposer();

        writeWords('Something worth keeping.');

        await act(() => {
            fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
            fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

            return Promise.resolve();
        });

        expect(asked.filter((request) => request.method === 'POST' && request.path.endsWith('/drafts'))).toHaveLength(
            1,
        );
    });

    it('refuses the shortcut wherever it refuses the control, so neither asks what the other would not', () => {
        drawComposer({ kind: 'new' }, {}, [work], false);

        fireEvent.keyDown(screen.getByRole('textbox', { name: 'Message' }), { key: 'Enter', ctrlKey: true });

        expect(screen.queryByRole('dialog', { name: 'Send this message?' })).toBeNull();
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

    it('stages several chosen files against one draft rather than filing a draft for each', async () => {
        // Where the octets went, which is the whole question: the draft one file is staged against is written by
        // whichever save answers first, so uploads started together would each carry a draft of their own and every
        // file but the last would hang off one nothing ever sends.
        const stagedAgainst: string[] = [];

        const uploadsEachFile: AttachmentUpload = (request) => {
            stagedAgainst.push(request.path);

            return Promise.resolve({
                status: 200,
                body: JSON.stringify({
                    attachmentId: `a${String(stagedAgainst.length)}`,
                    fileName: `file-${String(stagedAgainst.length)}.pdf`,
                    mediaType: 'application/pdf',
                    sizeOctets: 2_048,
                }),
                headers: {},
            });
        };

        const { asked } = drawComposer({ kind: 'new' }, {}, [work], true, uploadsEachFile);

        const picker = document.querySelector<HTMLInputElement>('input[type=file]');

        if (picker === null) {
            throw new Error('The composer drew no file picker to attach with.');
        }

        await act(() => {
            fireEvent.change(picker, {
                target: { files: [new File(['0'], 'one.pdf'), new File(['1'], 'two.pdf')] },
            });

            return Promise.resolve();
        });

        await waitFor(() => {
            expect(stagedAgainst).toHaveLength(2);
        });

        expect(asked.filter((request) => request.method === 'POST' && request.path.endsWith('/drafts'))).toHaveLength(
            1,
        );
        expect(new Set(stagedAgainst.map((path) => path.split('/attachments')[0])).size).toBe(1);
    });

    it('asks before giving up a message whose only work so far is an attached file', async () => {
        drawComposer();

        const picker = document.querySelector<HTMLInputElement>('input[type=file]');

        if (picker === null) {
            throw new Error('The composer drew no file picker to attach with.');
        }

        await act(() => {
            fireEvent.change(picker, { target: { files: [new File(['0'], 'invoice.pdf')] } });

            return Promise.resolve();
        });

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        expect(screen.getByRole('dialog', { name: 'Discard this message?' })).toBeDefined();
    });

    it('holds the keyboard inside itself while it stands over the whole screen', () => {
        drawComposer();

        const reachable = [...composerFrame().querySelectorAll<HTMLElement>('button, input, textarea, select')].filter(
            (control) => control.tabIndex !== -1 && control.closest('dialog:not([open])') === null,
        );

        const first = reachable[0];
        const last = reachable[reachable.length - 1];

        if (first === undefined || last === undefined) {
            throw new Error('The composer drew nothing a keyboard can reach.');
        }

        last.focus();
        fireEvent.keyDown(last, { key: 'Tab' });

        expect(document.activeElement).toBe(first);

        fireEvent.keyDown(first, { key: 'Tab', shiftKey: true });

        expect(document.activeElement).toBe(last);
    });

    it('does not put a removed file back when an older save answers after the removal', async () => {
        // A save answers with the whole list the deployment held when it was asked, so one still in flight while a
        // file is taken off would otherwise draw that file back onto a message that no longer carries it.
        let releaseTheSave = (): void => undefined;
        const stagedAtTheDeployment = JSON.parse(draftBody({ attachments: [stagedFile] })) as unknown;

        const asked: ClientRequest[] = [];

        const transport: MailFathomTransport = (request) => {
            asked.push(request);

            if (request.method === 'PUT') {
                return new Promise((answer) => {
                    releaseTheSave = () => {
                        answer({ status: 200, body: JSON.stringify(stagedAtTheDeployment), headers: {} });
                    };
                });
            }

            const answer = answerFor(request, {});

            return Promise.resolve({ status: answer.status, body: answer.body, headers: {} });
        };

        render(
            <LocalizationProvider>
                <ToastsProvider>
                    <AttachmentUploadContext value={uploadsOneFile}>
                        <Composer
                            session={session}
                            transport={transport}
                            accounts={[work]}
                            opening={{ kind: 'new' }}
                            online
                            onClosed={vi.fn()}
                        />
                    </AttachmentUploadContext>
                </ToastsProvider>
            </LocalizationProvider>,
        );

        const picker = document.querySelector<HTMLInputElement>('input[type=file]');

        if (picker === null) {
            throw new Error('The composer drew no file picker to attach with.');
        }

        await act(() => {
            fireEvent.change(picker, { target: { files: [new File(['0'], 'invoice.pdf')] } });

            return Promise.resolve();
        });

        const staged = await screen.findByRole('list', { name: 'Attached files' });

        // A save in flight, and the file taken off while it is.
        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));

        await act(() => {
            fireEvent.click(within(staged).getByRole('button', { name: 'Remove invoice.pdf' }));

            return Promise.resolve();
        });

        expect(screen.queryByRole('list', { name: 'Attached files' })).toBeNull();

        await act(() => {
            releaseTheSave();

            return Promise.resolve();
        });

        expect(screen.queryByRole('list', { name: 'Attached files' })).toBeNull();
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

    it('closes without asking where nothing has been written', async () => {
        const { closed } = drawComposer();

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        await waitFor(() => {
            expect(closed).toHaveBeenCalledTimes(1);
        });
        expect(rememberedComposition()).toBeNull();
    });

    it('gives the draft up as well as the words when what was written is discarded', async () => {
        const { asked, closed } = drawComposer();

        fireEvent.click(screen.getByRole('button', { name: 'Save draft' }));
        expect(await screen.findByText('Draft filed in your own drafts.')).toBeDefined();

        writeWords('Never mind.');
        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
        fireEvent.click(screen.getByRole('button', { name: 'Discard' }));

        await waitFor(() => {
            expect(asked.at(-1)?.method).toBe('DELETE');
        });

        expect(closed).toHaveBeenCalledTimes(1);
    });

    it('stays open where the draft it was asked to keep could not be filed', async () => {
        const { closed } = drawComposer({ kind: 'new' }, { save: { status: 503, body: '' } });

        writeWords('Keep this.');
        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));
        fireEvent.click(
            within(screen.getByRole('dialog', { name: 'Discard this message?' })).getByRole('button', {
                name: 'Save draft',
            }),
        );

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

    it('offers the people in the conversation to complete an address from, each of them once', async () => {
        // The sender copied in as well, which is what puts one address in two headers — and what would otherwise
        // offer it twice and give the completion list two options under one key.
        const message = JSON.parse(messageBody()) as {
            headers: { participants: { role: string; address: string; displayName: string | null }[] };
        };

        message.headers.participants.push({ role: 'Cc', address: 'billing@example.invalid', displayName: 'Billing' });

        drawComposer(replying, { message: { status: 200, body: JSON.stringify(message) } });

        await screen.findByText('Re: Quarterly invoice');

        const offered = [...document.querySelectorAll('datalist option')].map((option) => option.getAttribute('value'));

        expect(offered).toContain('auditor@example.invalid');
        expect(offered).toStrictEqual([...new Set(offered)]);
    });

    it('closes an answer nobody has written in without asking, since its recipients are not work anybody did', async () => {
        const { closed } = drawComposer(replying);

        await screen.findByText('Re: Quarterly invoice');

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        await waitFor(() => {
            expect(closed).toHaveBeenCalledTimes(1);
        });
        expect(screen.queryByRole('dialog', { name: 'Discard this message?' })).toBeNull();
    });

    it('names the subject it will not let anybody edit, there being no field for a label to point at', async () => {
        drawComposer(replying);

        const subject = await screen.findByText('Re: Quarterly invoice');

        expect(subject.getAttribute('aria-labelledby')).not.toBeNull();
        expect(document.getElementById(subject.getAttribute('aria-labelledby') ?? '')?.textContent).toBe('Subject');
    });

    it('says the message could not be read, and offers the way out, rather than an empty composer', async () => {
        const { closed } = drawComposer(replying, { message: { status: 503, body: '' } });

        expect(await screen.findByText(/did not answer/u)).toBeDefined();

        fireEvent.click(screen.getByRole('button', { name: 'Close the message' }));

        await waitFor(() => {
            expect(closed).toHaveBeenCalledTimes(1);
        });
    });

    // The reading pane lets go of a message the deployment no longer holds, because the pane is what holds what is
    // open. The composer is drawn *about* that message rather than holding it, so it says the message is gone and
    // waits: closing itself would take away the only thing that said so, and it is a surface somebody opened.
    it('says the message it was answering is gone, and stays open on it', async () => {
        const { closed } = drawComposer(replying, { message: { status: 404, body: '' } });

        expect(await screen.findByText(/nothing here to answer/u)).toBeDefined();
        expect(closed).not.toHaveBeenCalled();
        expect(screen.getByRole('button', { name: 'Close the message' })).toBeDefined();
    });

    it('reads nothing where this tab was already writing that answer', () => {
        rememberComposition({
            answering: { storedEmailId: messageId, answers: 'everyone' },
            account: 'work',
            subject: 'Re: Quarterly invoice',
            to: ['billing@example.invalid'],
            cc: [],
            bcc: [],
            words: [{ text: 'Half an answer' }],
        });

        drawComposer(replying);

        expect(wordsWritten()).toBe('Half an answer');
    });
});

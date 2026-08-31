// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ClientRequest, DeploymentAddress, MailFathomTransport } from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ConnectDeployment } from './ConnectDeployment';

// A deployment that is there and wants a credential, which is what one somebody actually runs answers a client that
// has not signed in. Everything below reaches this rather than a network: the screen takes its transport from its
// caller, so nothing here patches a global or stands up a server.
const reachable: MailFathomTransport = () =>
    Promise.resolve({ status: 401, body: '', headers: { 'www-authenticate': 'Bearer realm="MailFathom"' } });

const nothingThere: MailFathomTransport = () => Promise.reject(new TypeError('Failed to fetch'));

const somethingElse: MailFathomTransport = () =>
    Promise.resolve({ status: 200, body: '<!doctype html><title>Sign in</title>', headers: {} });

// The screen is handed a transport per attempt rather than one transport, because giving up on an attempt is what
// abandons it. Collecting the signals it was given is how a test sees whether an attempt was actually called off,
// which is the difference between the screen ignoring an answer and the request being cancelled.
function renderScreen(send: MailFathomTransport): { reached: DeploymentAddress[]; attempts: AbortSignal[] } {
    const reached: DeploymentAddress[] = [];
    const attempts: AbortSignal[] = [];

    render(
        <LocalizationProvider>
            <ConnectDeployment
                send={(abandoned) => {
                    attempts.push(abandoned);

                    return send;
                }}
                onReached={(deployment) => {
                    reached.push(deployment);
                }}
            />
        </LocalizationProvider>,
    );

    return { reached, attempts };
}

function type(entry: string): void {
    fireEvent.change(screen.getByRole('textbox', { name: 'Deployment address' }), { target: { value: entry } });
}

function connect(): void {
    fireEvent.click(screen.getByRole('button', { name: 'Connect' }));
}

function permitClearText(): void {
    fireEvent.click(screen.getByRole('checkbox', { name: 'Reach this deployment over plain HTTP' }));
}

describe('ConnectDeployment', () => {
    it('puts the cursor in the address, because the view changed and focus is placed rather than left behind', () => {
        renderScreen(reachable);

        expect(document.activeElement).toBe(screen.getByRole('textbox', { name: 'Deployment address' }));
    });

    it('says what plain HTTP costs beside the control that permits it, rather than after it is chosen', () => {
        renderScreen(reachable);

        expect(
            screen.getByText(
                'Your password is encoded rather than encrypted, on every request. Anybody between this client and the deployment can read it. Leave this off unless the network between them is yours.',
            ),
        ).toBeDefined();
    });

    it('asks for an address rather than reaching nowhere when nothing was typed', () => {
        const { reached } = renderScreen(reachable);

        connect();

        expect(screen.getByRole('alert').textContent).toBe('Name the deployment that holds your mail.');
        expect(reached).toEqual([]);
    });

    it('says what an address is when what was typed is not one', () => {
        renderScreen(reachable);

        type('my mail server');
        connect();

        expect(screen.getByRole('alert').textContent).toBe(
            'That is not an address. Name the host it answers on, and a port where it uses one.',
        );
    });

    it('will not carry a password over plain HTTP until somebody says it may', () => {
        const { reached } = renderScreen(reachable);

        type('http://mail.example.test');
        connect();

        expect(screen.getByRole('alert').textContent).toBe(
            'That address is plain HTTP, which this client will not send a password over until you say it may.',
        );
        expect(reached).toEqual([]);
    });

    it('takes the refusal away as soon as the address is being corrected, rather than after the next attempt', () => {
        renderScreen(reachable);

        type('my mail server');
        connect();
        type('mail.example.test');

        expect(screen.queryByRole('alert')).toBeNull();
    });

    it('sends nothing over plain HTTP until that is declared, and then reaches it', async () => {
        const asked: ClientRequest[] = [];
        const { reached } = renderScreen((request) => {
            asked.push(request);

            return reachable(request);
        });

        type('http://mail.example.test');
        connect();
        permitClearText();
        connect();

        await vi.waitFor(() => {
            expect(reached).toEqual([{ baseAddress: 'http://mail.example.test' }]);
        });

        // One request rather than two: the refusal ran before anything went out, so nothing was sent over the
        // transport the first attempt was refused for using.
        expect(asked.map((request) => request.path)).toEqual(['http://mail.example.test/api/client/session']);
    });

    it('says it is reaching the deployment while the answer has not arrived', () => {
        renderScreen(() => new Promise(() => undefined));

        type('mail.example.test');
        connect();

        expect(screen.getByRole('status').textContent).toBe('Reaching the deployment…');
    });

    it('lets an attempt that never answers be given up on, rather than holding the screen on it', () => {
        const { attempts } = renderScreen(() => new Promise(() => undefined));

        type('mail.example.test');
        connect();

        fireEvent.click(screen.getByRole('button', { name: 'Stop trying' }));

        // Back where the person left it: no attempt is reported as running, the address can be corrected and tried
        // again, and the request the abandoned attempt started was cancelled rather than left on the wire.
        expect(screen.queryByRole('status')).toBeNull();
        expect(screen.getByRole('button', { name: 'Connect' }).hasAttribute('disabled')).toBe(false);
        expect(attempts.map((abandoned) => abandoned.aborted)).toEqual([true]);
    });

    it('offers nothing to give up on before an attempt has been started', () => {
        renderScreen(reachable);

        expect(screen.queryByRole('button', { name: 'Stop trying' })).toBeNull();
    });

    it('takes the deployment once it answers as one, under the scheme the client supplied', async () => {
        const { reached } = renderScreen(reachable);

        type('mail.example.test:8443');
        connect();

        await vi.waitFor(() => {
            expect(reached).toEqual([{ baseAddress: 'https://mail.example.test:8443' }]);
        });
    });

    it('says nothing answered rather than handing over an address it could not reach', async () => {
        const { reached } = renderScreen(nothingThere);

        type('mail.example.test');
        connect();

        expect((await screen.findByRole('alert')).textContent).toBe(
            'Nothing answered there. Check the address, and check that the deployment is running.',
        );
        expect(reached).toEqual([]);
    });

    it('tries once and never again without the transport security the first attempt asked for', async () => {
        const asked: string[] = [];
        renderScreen((request) => {
            asked.push(request.path);

            return nothingThere(request);
        });

        type('mail.example.test');
        connect();

        await screen.findByRole('alert');
        expect(asked).toEqual(['https://mail.example.test/api/client/session']);
    });

    it('says what answered was not MailFathom rather than taking anything that replies', async () => {
        const { reached } = renderScreen(somethingElse);

        type('mail.example.test');
        connect();

        expect((await screen.findByRole('alert')).textContent).toBe('Something answered there, but not as MailFathom.');
        expect(reached).toEqual([]);
    });
});

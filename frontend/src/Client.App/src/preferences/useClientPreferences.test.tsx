// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { ThemeProvider } from '../theme/Theme';
import { useTheme } from '../theme/useTheme';
import { useClientPreferences } from './useClientPreferences';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

// The whole document, because the route answers with nothing less and the package refuses an answer missing a field.
// Marking read is defaulted rather than named at each call, since only the tests below that are about it say anything.
function stored(preferences: {
    telemetryEnabled: boolean;
    theme: string;
    openMailInTabs: boolean;
    markReadOnOpen?: boolean;
}): string {
    return JSON.stringify({ markReadOnOpen: true, ...preferences });
}

// The transport is the network boundary and the whole of what these tests fake, exactly as in the package that reads
// through it. Every answer is the stored document, because both routes answer with it.
function recording(body: string, status = 200): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            return Promise.resolve({ status, body, headers: {} });
        },
    };
}

// The theme is read beside the settings because it is the one of them the device also holds, so what the deployment
// did to it is only visible through the provider that paints it.
function reading(transport: MailFathomTransport, asked: ClientSession | null = session) {
    return renderHook(
        ({ session: presenting }: { session: ClientSession | null }) => ({
            preferences: useClientPreferences(presenting, transport),
            theme: useTheme(),
        }),
        { initialProps: { session: asked }, wrapper: ThemeProvider },
    );
}

describe('useClientPreferences', () => {
    afterEach(() => {
        window.localStorage.clear();
    });

    it('reads what the person set once there is a session to read it with', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: true }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.openMailInTabs).toBe(true);
        });

        expect(requests[0]?.method).toBe('GET');
        expect(requests[0]?.path).toBe('https://mail.example.invalid/api/client/preferences');
    });

    it('answers marking read as on before anything has been read, which is what ADR 0026 defaults it to', () => {
        const { transport } = recording(stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: true }));
        const { result } = reading(transport);

        expect(result.current.preferences.markReadOnOpen).toBe(true);
    });

    it('answers marking read as off once the person has turned it off', async () => {
        const { transport } = recording(
            stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false, markReadOnOpen: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.markReadOnOpen).toBe(false);
        });
    });

    it('lets the deployment’s theme replace what the device opened in', async () => {
        const { transport } = recording(stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: false }));
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('dark');
        });
    });

    it('reads nothing while there is nothing to ask with, and leaves the device’s theme standing', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: true }),
        );
        const { result } = reading(transport, null);

        await waitFor(() => {
            expect(requests).toStrictEqual([]);
        });

        expect(result.current.theme.choice).toBe('system');
        expect(result.current.preferences.openMailInTabs).toBe(false);
    });

    it('leaves both settings at what the device says where the deployment would not answer', async () => {
        const { transport, requests } = recording('', 503);
        const { result } = reading(transport);

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        expect(result.current.theme.choice).toBe('system');
        expect(result.current.preferences.openMailInTabs).toBe(false);
        expect(result.current.preferences.notStated).toBe(false);
    });

    it('states the whole document on a change, carrying back what no control here offers', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: false, theme: 'light', openMailInTabs: false }),
        );
        const { result } = reading(transport);

        // Waited for the answer to be applied rather than for the request to be recorded: the two are not the same
        // moment, and what this is about is the document a change is composed out of.
        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseTabMode(true);
        });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });

        expect(requests[1]?.method).toBe('POST');
        expect(requests[1]?.headers['Content-Type']).toBe('application/json');
        expect(JSON.parse(requests[1]?.body ?? '')).toStrictEqual({
            telemetryEnabled: false,
            theme: 'light',
            openMailInTabs: true,
            markReadOnOpen: true,
        });
    });

    it('writes a chosen theme to the deployment and paints it on the device at once', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        act(() => {
            result.current.preferences.chooseTheme('light');
        });

        expect(result.current.theme.choice).toBe('light');
        expect(window.localStorage.getItem('mailfathom.theme')).toBe('light');

        await waitFor(() => {
            expect(JSON.parse(requests[1]?.body ?? '')).toStrictEqual({
                telemetryEnabled: true,
                theme: 'light',
                openMailInTabs: false,
                markReadOnOpen: true,
            });
        });
    });

    it('says a change the deployment refused was not stated', async () => {
        const { transport } = recording('', 503);
        const { result } = reading(transport);

        act(() => {
            result.current.preferences.chooseTabMode(true);
        });

        await waitFor(() => {
            expect(result.current.preferences.notStated).toBe(true);
        });
    });

    it('reads whether this deployment may be told what the client is doing', async () => {
        const { transport } = recording(stored({ telemetryEnabled: false, theme: 'system', openMailInTabs: false }));
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.telemetryEnabled).toBe(false);
        });
    });

    it('states the whole document when the telemetry decision is the one that changed', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: true }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.telemetryEnabled).toBe(true);
        });

        act(() => {
            result.current.preferences.chooseTelemetry(false);
        });

        await waitFor(() => {
            expect(result.current.preferences.telemetryEnabled).toBe(false);
        });

        // The write is the whole document rather than the field that moved, so a deployment reading it back finds the
        // theme and the tab mode as they were rather than as an empty record over somebody's answers.
        const stating = requests.find((request) => request.method === 'POST');

        expect(JSON.parse(stating?.body ?? '')).toStrictEqual({
            telemetryEnabled: false,
            theme: 'dark',
            openMailInTabs: true,
            markReadOnOpen: true,
        });
    });

    it('reads again as somebody else once the credential changes', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }),
        );
        const { rerender } = reading(transport);

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        rerender({ session: { ...session, authorization: 'Basic b3RoZXI=' } });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });

        expect(requests[1]?.headers['Authorization']).toBe('Basic b3RoZXI=');
    });

    it('holds nothing of the person who signed out, so the next one starts on their own answers', async () => {
        const { transport } = recording(stored({ telemetryEnabled: false, theme: 'dark', openMailInTabs: true }));
        const { result, rerender } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.openMailInTabs).toBe(true);
        });

        rerender({ session: null });

        expect(result.current.preferences.openMailInTabs).toBe(false);
    });

    it('does not carry the previous person’s document into the next one’s first write', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: false, theme: 'dark', openMailInTabs: true }),
        );
        const { result, rerender } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.openMailInTabs).toBe(true);
        });

        rerender({ session: null });
        rerender({ session: { ...session, authorization: 'Basic b3RoZXI=' } });

        act(() => {
            result.current.preferences.chooseTabMode(true);
        });

        await waitFor(() => {
            expect(requests.some((asked) => asked.method === 'POST')).toBe(true);
        });

        // The whole document, composed out of nothing but the defaults and the one thing that was just chosen — the
        // first person's telemetry decision is not in it.
        const written = requests.find((asked) => asked.method === 'POST');

        expect(JSON.parse(written?.body ?? '')).toStrictEqual({
            telemetryEnabled: true,
            theme: 'system',
            openMailInTabs: true,
            markReadOnOpen: true,
        });
    });

    it('lets a choice made while the read is still out stand rather than being read over', async () => {
        const answered: ((answer: { status: number; body: string; headers: Record<string, string> }) => void)[] = [];
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return new Promise((settle) => {
                answered.push(settle);
            });
        };

        const { result } = reading(transport);

        await waitFor(() => {
            expect(requests).toHaveLength(1);
        });

        act(() => {
            result.current.preferences.chooseTabMode(true);
        });

        // The read answers after the choice, with what the deployment held before it. A client that applied it would
        // put the switch back and leave the write it just sent describing something nobody can see.
        act(() => {
            answered[0]?.({
                status: 200,
                body: stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: false }),
                headers: {},
            });
        });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });

        expect(result.current.preferences.openMailInTabs).toBe(true);
        expect(result.current.theme.choice).toBe('system');
    });

    it('does not read again because its own answer changed the theme', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('dark');
        });

        // Recorded by the time that answer has been applied: the effect calls the transport before its first await, so
        // a second run would already have pushed a second request rather than being about to.
        expect(requests).toHaveLength(1);
    });

    // The telemetry answer is the one setting a client has to honour before it has been read, because the seconds
    // between opening and the first answer are seconds a client that had been turned off would otherwise record in.
    describe('the telemetry answer this device remembers', () => {
        it('keeps what the deployment answered, so the next start honours it before it answers again', async () => {
            const { transport } = recording(
                stored({ telemetryEnabled: false, theme: 'system', openMailInTabs: false }),
            );
            const { result } = reading(transport);

            await waitFor(() => {
                expect(result.current.preferences.telemetryEnabled).toBe(false);
            });

            expect(window.localStorage.getItem('mailfathom.telemetry')).toBe('false');
        });

        it('keeps what somebody chose here, without waiting for the deployment to confirm it', async () => {
            const { transport } = recording(stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }));
            const { result } = reading(transport);

            await waitFor(() => {
                expect(result.current.preferences.telemetryEnabled).toBe(true);
            });

            act(() => {
                result.current.preferences.chooseTelemetry(false);
            });

            expect(window.localStorage.getItem('mailfathom.telemetry')).toBe('false');
        });

        it('answers it while there is no session to read one with, rather than the unset answer', () => {
            window.localStorage.setItem('mailfathom.telemetry', 'false');

            const { transport } = recording(stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }));
            const { result } = reading(transport, null);

            expect(result.current.preferences.telemetryEnabled).toBe(false);
        });

        it('is replaced by what the deployment answers, holding no second opinion beyond that', async () => {
            window.localStorage.setItem('mailfathom.telemetry', 'false');

            const { transport } = recording(stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }));
            const { result } = reading(transport);

            await waitFor(() => {
                expect(result.current.preferences.telemetryEnabled).toBe(true);
            });

            expect(window.localStorage.getItem('mailfathom.telemetry')).toBe('true');
        });
    });
});

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

function stored(preferences: { telemetryEnabled: boolean; theme: string; openMailInTabs: boolean }): string {
    return JSON.stringify(preferences);
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
});

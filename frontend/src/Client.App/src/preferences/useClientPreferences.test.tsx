// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

import { act, renderHook, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it } from 'vitest';
import type { ClientRequest, ClientSession, MailFathomTransport } from '@mailfathom/client-backend';
import { telemetryKey } from '../device/deviceStore';
import { TelemetryContext, noTelemetry, type ClientTelemetry } from '../telemetry/clientTelemetry';
import { recordingTelemetry, recordsOf } from '../telemetry/telemetry.harness';
import { ThemeProvider } from '../theme/Theme';
import { useTheme } from '../theme/useTheme';
import { useClientPreferences } from './useClientPreferences';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

// Who is signed in, which this hook is told beside the session because the device's remembered telemetry answer is
// held under the person rather than under the machine.
const anna = 'anna';
const bartek = 'bartek';

// The whole document, because the route answers with nothing less and the package refuses an answer missing a field.
// The four preferences no control in this hook's own tests moves are defaulted rather than named at each call, so
// only the tests that are about one of them say anything about it.
function stored(preferences: {
    telemetryEnabled: boolean;
    theme: string;
    openMailInTabs: boolean;
    markReadOnOpen?: boolean;
    expandWholeThread?: boolean;
    aiFiltersShown?: boolean;
    messageView?: string;
    notificationSeconds?: number;
}): string {
    return JSON.stringify({
        markReadOnOpen: true,
        expandWholeThread: false,
        aiFiltersShown: true,
        messageView: 'reduced',
        notificationSeconds: 5,
        ...preferences,
    });
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

// A deployment whose settings policy forces the telemetry switch on, answering as the routes do: the forced value and
// who holds it on every read, a refusal to a write stating the other answer, and a write stating the forced answer or
// none taken with the rest of what it stated.
function forcingTelemetryOn(): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];
    let theme = 'light';

    const answering = (): string =>
        JSON.stringify({
            ...(JSON.parse(stored({ telemetryEnabled: true, theme, openMailInTabs: false })) as object),
            telemetryHeldBy: 'Organization',
        });

    return {
        requests,
        transport: (request) => {
            requests.push(request);

            if (request.method !== 'POST') {
                return Promise.resolve({ status: 200, body: answering(), headers: {} });
            }

            const stating = JSON.parse(request.body ?? '') as { telemetryEnabled: boolean | null; theme: string };

            if (stating.telemetryEnabled === false) {
                return Promise.resolve({ status: 400, body: '', headers: {} });
            }

            theme = stating.theme;

            return Promise.resolve({ status: 200, body: answering(), headers: {} });
        },
    };
}

// The theme is read beside the settings because it is the one of them the device also holds, so what the deployment
// did to it is only visible through the provider that paints it.
function reading(
    transport: MailFathomTransport,
    asked: ClientSession | null = session,
    person: string | null = anna,
    telemetry: ClientTelemetry = noTelemetry,
) {
    return renderHook(
        ({ session: presenting, person: whose }: { session: ClientSession | null; person: string | null }) => ({
            preferences: useClientPreferences(presenting, transport, whose),
            theme: useTheme(),
        }),
        {
            initialProps: { session: asked, person },
            wrapper: ({ children }) => (
                <TelemetryContext value={telemetry}>
                    <ThemeProvider>{children}</ThemeProvider>
                </TelemetryContext>
            ),
        },
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

    it('answers the message view as the reduced document before anything has been read', () => {
        const { transport } = recording(stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: true }));
        const { result } = reading(transport);

        expect(result.current.preferences.messageView).toBe('reduced');
    });

    it('answers the message view as the sender’s own markup once the person has chosen it', async () => {
        const { transport } = recording(
            stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false, messageView: 'embeddedHtml' }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.messageView).toBe('embeddedHtml');
        });
    });

    // The third of the three is the one this change added, and it travels as itself rather than as the absence of the
    // other two: a client reading it as anything else would draw the reduced document for somebody who asked for a
    // cleaning and report the setting as having had no effect.
    it('answers the message view as the cleaned rendering once the person has chosen it', async () => {
        const { transport } = recording(
            stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false, messageView: 'cleaned' }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.messageView).toBe('cleaned');
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
            telemetryEnabled: null,
            theme: 'light',
            openMailInTabs: true,
            markReadOnOpen: true,
            expandWholeThread: false,
            aiFiltersShown: true,
            messageView: 'reduced',
            notificationSeconds: 5,
        });
    });

    it('answers the standing views as drawn before anything has been read, the tree offering them by default', () => {
        const { transport } = recording(stored({ telemetryEnabled: true, theme: 'dark', openMailInTabs: true }));
        const { result } = reading(transport);

        expect(result.current.preferences.aiFiltersShown).toBe(true);
    });

    it('answers the standing views as gone once the person has taken them out of the tree', async () => {
        const { transport } = recording(
            stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false, aiFiltersShown: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.preferences.aiFiltersShown).toBe(false);
        });
    });

    it('states the whole document when the standing views are the decision that changed', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: false, theme: 'light', openMailInTabs: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseAiFilters(false);
        });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });

        expect(JSON.parse(requests[1]?.body ?? '')).toStrictEqual({
            telemetryEnabled: null,
            theme: 'light',
            openMailInTabs: false,
            markReadOnOpen: true,
            expandWholeThread: false,
            aiFiltersShown: false,
            messageView: 'reduced',
            notificationSeconds: 5,
        });
    });

    it('states the whole document when the notification time is the decision that changed', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: false, theme: 'light', openMailInTabs: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseNotificationSeconds(12);
        });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });

        expect(JSON.parse(requests[1]?.body ?? '')).toStrictEqual({
            telemetryEnabled: null,
            theme: 'light',
            openMailInTabs: false,
            markReadOnOpen: true,
            expandWholeThread: false,
            aiFiltersShown: true,
            messageView: 'reduced',
            notificationSeconds: 12,
        });
    });

    // The choice made after the two refused ones is the first thing written, which is what says the refused ones sent
    // nothing rather than merely arriving later.
    it('writes nothing for a notification time outside the bound, rather than bringing it inside', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: false, theme: 'light', openMailInTabs: false }),
        );
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseNotificationSeconds(0);
            result.current.preferences.chooseNotificationSeconds(31);
        });
        act(() => {
            result.current.preferences.chooseNotificationSeconds(7);
        });

        await waitFor(() => {
            expect(requests).toHaveLength(2);
        });

        expect(JSON.parse(requests[1]?.body ?? '')).toMatchObject({ notificationSeconds: 7 });
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
                telemetryEnabled: null,
                theme: 'light',
                openMailInTabs: false,
                markReadOnOpen: true,
                expandWholeThread: false,
                aiFiltersShown: true,
                messageView: 'reduced',
                notificationSeconds: 5,
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

    // A settings policy may hold the telemetry switch, and the route then refuses a document stating the other answer.
    // The refused document is not what the next write may be composed from: every later change would restate the
    // refused answer beside its own, and a theme nobody may be stopped from choosing would be refused with it.
    it('reads again after a refused change, so the next change is not refused with it', async () => {
        const { transport, requests } = forcingTelemetryOn();
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseTelemetry(false);
        });

        // The switch is back on what the deployment holds and the refusal is still said, which is the read that
        // followed the refused write having been applied.
        await waitFor(() => {
            expect(result.current.preferences.notStated).toBe(true);
            expect(result.current.preferences.telemetryEnabled).toBe(true);
        });

        act(() => {
            result.current.preferences.chooseTheme('dark');
        });

        await waitFor(() => {
            expect(result.current.preferences.notStated).toBe(false);
        });

        // The forced value the client read back is not the person's to state, so the accepted write says nothing
        // about the switch — which is what leaves the refusal they stored standing once the policy is gone.
        expect(JSON.parse(requests.at(-1)?.body ?? '')).toMatchObject({ telemetryEnabled: null, theme: 'dark' });
        expect(window.localStorage.getItem(telemetryKey(anna))).toBe('true');
    });

    // The read that follows a refused write answers an older question than a choice made while it was out. Applying
    // it on arrival would put the screen back on what the deployment held before that choice, and the next write
    // would carry the reverted document.
    it('lets a choice made while that read is still out stand rather than being read over', async () => {
        const held = stored({ telemetryEnabled: true, theme: 'light', openMailInTabs: false });
        const requests: ClientRequest[] = [];
        const releases: (() => void)[] = [];
        const recorded = recordingTelemetry();
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            // The first read answers, the first write is refused, and the read that follows it is held unanswered.
            if (requests.length === 2) {
                return Promise.resolve({ status: 503, body: '', headers: {} });
            }

            if (requests.length === 3) {
                return new Promise((resolve) => {
                    releases.push(() => {
                        resolve({ status: 200, body: held, headers: {} });
                    });
                });
            }

            return Promise.resolve({ status: 200, body: held, headers: {} });
        };
        const { result } = reading(transport, session, anna, recorded.telemetry);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseTabMode(true);
        });

        await waitFor(() => {
            expect(requests).toHaveLength(3);
        });

        act(() => {
            result.current.preferences.chooseTheme('dark');
        });

        // The held read answers, and one more change goes out behind it: by the time the deployment has taken that
        // one, the answer released ahead of it has been either applied or let go.
        act(() => {
            releases[0]?.();
            result.current.preferences.chooseThreadExpansion(true);
        });

        await waitFor(() => {
            expect(recordsOf(recorded.recorded, 'preferences_stated')).toHaveLength(3);
        });

        expect(result.current.theme.choice).toBe('dark');
        expect(result.current.preferences.openMailInTabs).toBe(true);
    });

    // A deployment that cannot be reached answers nothing to the read either. The choice is then all there is, so it
    // stays on the screen marked as not saved, and a switch the person moved is still owed: the next write states it
    // again rather than leaving the deployment on the answer they moved away from.
    it('leaves a refused choice standing where the deployment cannot be read again, and states it again', async () => {
        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return Promise.resolve(
                requests.length === 1
                    ? {
                          status: 200,
                          body: stored({ telemetryEnabled: true, theme: 'light', openMailInTabs: false }),
                          headers: {},
                      }
                    : { status: 503, body: '', headers: {} },
            );
        };
        const { result } = reading(transport);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseTelemetry(false);
        });

        // The read, the refused write, and the read that followed it.
        await waitFor(() => {
            expect(requests).toHaveLength(3);
            expect(result.current.preferences.notStated).toBe(true);
        });

        expect(result.current.preferences.telemetryEnabled).toBe(false);

        act(() => {
            result.current.preferences.chooseTheme('dark');
        });

        expect(JSON.parse(requests.at(-1)?.body ?? '')).toMatchObject({ telemetryEnabled: false, theme: 'dark' });
    });

    // The switch is stated once, by the write the person moved it with. Every write after the deployment has taken
    // that answer says nothing about it, so no later change can restate a value the deployment may since have been
    // told to hold differently.
    it('states the switch a person moved, and nothing about it once the deployment has taken it', async () => {
        const { transport, requests } = recording(
            stored({ telemetryEnabled: false, theme: 'light', openMailInTabs: false }),
        );
        const recorded = recordingTelemetry();
        const { result } = reading(transport, session, anna, recorded.telemetry);

        await waitFor(() => {
            expect(result.current.theme.choice).toBe('light');
        });

        act(() => {
            result.current.preferences.chooseTelemetry(true);
        });

        await waitFor(() => {
            expect(recordsOf(recorded.recorded, 'preferences_stated')).toHaveLength(1);
        });

        act(() => {
            result.current.preferences.chooseTabMode(true);
        });

        const switches = requests
            .filter((asked) => asked.method === 'POST')
            .map((asked) => (JSON.parse(asked.body ?? '') as { telemetryEnabled: unknown }).telemetryEnabled);

        expect(switches).toStrictEqual([true, null]);
    });

    // Which preference somebody changed and what they set it to is not written down: a preference document is what a
    // person chose about their own client, and a collector is the deployment's operator rather than them. What an
    // operator needs is that the write happened and whether it stuck, a setting that silently does not stick being
    // the defect nobody reports as one.
    it.each<{ named: string; status: number; stated: string }>([
        { named: 'held', status: 200, stated: 'held' },
        { named: 'refused', status: 503, stated: 'refused' },
    ])(
        'reports a preference write the deployment $named, and nothing about which one it was',
        async ({ status, stated }) => {
            const body = stored({ telemetryEnabled: false, theme: 'light', openMailInTabs: false });
            const { transport } = recording(body, status);
            const recorded = recordingTelemetry();
            const { result } = reading(transport, session, anna, recorded.telemetry);

            act(() => {
                result.current.preferences.chooseTabMode(true);
            });

            await waitFor(() => {
                expect(recordsOf(recorded.recorded, 'preferences_stated')).toStrictEqual([
                    { event: 'preferences_stated', attributes: { 'mailfathom.client.stated': stated } },
                ]);
            });
        },
    );

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
            expandWholeThread: false,
            aiFiltersShown: true,
            messageView: 'reduced',
            notificationSeconds: 5,
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

        rerender({ session: { ...session, authorization: 'Basic b3RoZXI=' }, person: bartek });

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

        rerender({ session: null, person: anna });

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

        rerender({ session: null, person: anna });
        rerender({ session: { ...session, authorization: 'Basic b3RoZXI=' }, person: bartek });

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
            telemetryEnabled: null,
            theme: 'system',
            openMailInTabs: true,
            markReadOnOpen: true,
            expandWholeThread: false,
            aiFiltersShown: true,
            messageView: 'reduced',
            notificationSeconds: 5,
        });
    });

    // A write states the whole document, and the account menu offers the theme from the moment it is drawn — so this
    // is the window between signing in and the read coming back, in which every other value is the route's own unset
    // answer. Telemetry may not be: the device is holding the answer this person actually gave, and standing on the
    // unset one would turn their refusal into permission in the cache and on the screen, silently. What the device
    // remembers is not theirs to state to the deployment either — it may be a forced value an earlier session read —
    // so the write says nothing about the switch and the deployment keeps what it holds.
    it('stands on what this device remembers for a write made before the read returns, and states no switch', () => {
        window.localStorage.setItem(telemetryKey(anna), 'false');

        const requests: ClientRequest[] = [];
        const transport: MailFathomTransport = (request) => {
            requests.push(request);

            return new Promise(() => undefined);
        };

        const { result } = reading(transport);

        act(() => {
            result.current.preferences.chooseTheme('dark');
        });

        const written = requests.find((asked) => asked.method === 'POST');

        expect(JSON.parse(written?.body ?? '')).toStrictEqual({
            telemetryEnabled: null,
            theme: 'dark',
            openMailInTabs: false,
            markReadOnOpen: true,
            expandWholeThread: false,
            aiFiltersShown: true,
            messageView: 'reduced',
            notificationSeconds: 5,
        });
        expect(window.localStorage.getItem(telemetryKey(anna))).toBe('false');
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

            expect(window.localStorage.getItem(telemetryKey(anna))).toBe('false');
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

            expect(window.localStorage.getItem(telemetryKey(anna))).toBe('false');
        });

        it('answers it while there is no session to read one with, rather than the unset answer', () => {
            window.localStorage.setItem(telemetryKey(anna), 'false');

            const { transport } = recording(stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }));
            const { result } = reading(transport, null);

            expect(result.current.preferences.telemetryEnabled).toBe(false);
        });

        it('is replaced by what the deployment answers, holding no second opinion beyond that', async () => {
            window.localStorage.setItem(telemetryKey(anna), 'false');

            const { transport } = recording(stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }));
            const { result } = reading(transport);

            await waitFor(() => {
                expect(result.current.preferences.telemetryEnabled).toBe(true);
            });

            expect(window.localStorage.getItem(telemetryKey(anna))).toBe('true');
        });

        // Signing out and back in as somebody else on one tab keeps this hook mounted, which is the case the whole
        // derivation above exists for. The second person's own answer has not arrived yet, so what is drawn is what
        // the device holds — and holding it for the machine rather than for the person would hand them the first
        // person's permission for exactly as long as their own read takes, which is long enough to export under their
        // credential something they had declined.
        it('never answers one person with what this device remembers about another', () => {
            window.localStorage.setItem(telemetryKey(anna), 'true');
            window.localStorage.setItem(telemetryKey(bartek), 'false');

            const { transport } = recording(stored({ telemetryEnabled: true, theme: 'system', openMailInTabs: false }));
            const { result, rerender } = reading(transport, null, anna);

            expect(result.current.preferences.telemetryEnabled).toBe(true);

            rerender({ session: null, person: bartek });

            expect(result.current.preferences.telemetryEnabled).toBe(false);
        });

        it('keeps one person’s answer without writing anything about another', async () => {
            const { transport } = recording(
                stored({ telemetryEnabled: false, theme: 'system', openMailInTabs: false }),
            );
            const { result } = reading(transport, session, bartek);

            await waitFor(() => {
                expect(result.current.preferences.telemetryEnabled).toBe(false);
            });

            expect(window.localStorage.getItem(telemetryKey(bartek))).toBe('false');
            expect(window.localStorage.getItem(telemetryKey(anna))).toBeNull();
        });
    });
});

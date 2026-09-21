// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { act, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { useState } from 'react';
import type {
    AnswerBlock,
    BlockEvidence,
    ClientRequest,
    ClientSession,
    DeclaredSource,
    MailFathomTransport,
} from '@mailfathom/client-backend';
import { LocalizationProvider } from '../localization/Localization';
import { ReadingZoneContext } from '../localization/useReadingZone';
import { AnswerCanvas } from './AnswerCanvas';
import { EvidenceInspector } from './EvidenceInspector';
import type { ArrivedAnswerBlock } from './followedRun';

const session: ClientSession = {
    baseAddress: 'https://mail.example.invalid',
    authorization: 'Basic dGVzdA==',
};

const email = '0198f4a1-0000-7000-8000-000000000001';

const agreement: DeclaredSource = {
    id: 'c-1',
    target: { kind: 'fragment', email, fragment: 'f-4' },
    label: 'Master agreement.pdf',
    medium: 'Written',
    unreadable: null,
};

// The message the source stands in, with both dates on it: what a reader is owed is when it reached them, so the two
// are here to prove which of them the panel words.
const standingIn = {
    storedEmailId: email,
    account: 'work',
    folder: 'Archive',
    subject: 'Renewal terms',
    sentAt: '2026-07-01T12:00:00+00:00',
    receivedAt: '2026-08-26T09:00:00+00:00',
};

const passage = { fragmentId: 'f-4', ordinal: 3, text: 'Monthly remuneration is EUR 1,200 net' };

const file = {
    position: 0,
    fileName: 'Master agreement.pdf',
    wasFileNameNormalized: false,
    mediaType: 'application/pdf',
    sizeOctets: 12_000,
};

/** One resolution as the deployment answers it, which is the whole body this route ever returns to the panel. */
function resolved(citation: Record<string, unknown>): string {
    return JSON.stringify({ citations: [citation] });
}

const quotedPassage = resolved({ outcome: 'Resolved', message: standingIn, fragment: passage });

function answering(...bodies: readonly string[]): { transport: MailFathomTransport; requests: ClientRequest[] } {
    const requests: ClientRequest[] = [];

    return {
        requests,
        transport: (request) => {
            const body = bodies[Math.min(requests.length, bodies.length - 1)] ?? '{}';
            requests.push(request);

            return Promise.resolve({ status: 200, body, headers: {} });
        },
    };
}

/** A deployment that has been asked and has not answered yet, so the order two answers arrive in is the test's own. */
function holding(): {
    transport: MailFathomTransport;
    answer: (at: number, body: string) => void;
} {
    const answers: ((body: string) => void)[] = [];

    return {
        answer: (at, body) => {
            answers[at]?.(body);
        },
        transport: () =>
            new Promise((settle) => {
                answers.push((body) => {
                    settle({ status: 200, body, headers: {} });
                });
            }),
    };
}

// The answer the trip to a source must not cost, standing in for the blocks a run composes. It is named here rather
// than written into the markup below for the reason every string is: what a screen draws is a catalogue entry, and the
// lint rule that holds that cannot tell a test's own prop from a sentence somebody reads.
const theAnswer = 'The rate was agreed in April 2021.';

/** What the control pressing one source is called, which is how every test below reaches it. */
function checkingFor(source: DeclaredSource): string {
    return `Check ${source.label}`;
}

/**
 * The panel as a screen holds it: an answer, the citations in it, and whichever source somebody last pressed.
 *
 * It is the arrangement the inspector is written for rather than a prop being set by a test — what the panel promises
 * is that checking a fact costs the answer nothing, and only something drawing both can be held to that.
 */
function Checking({
    transport,
    sources = [agreement],
    signedIn = session,
    onOpenMessage,
}: {
    readonly transport: MailFathomTransport;
    readonly sources?: readonly DeclaredSource[];
    readonly signedIn?: ClientSession | null;
    readonly onOpenMessage?: ((storedEmailId: string) => void) | undefined;
}) {
    const [followed, setFollowed] = useState<DeclaredSource | null>(null);

    return (
        <LocalizationProvider>
            <p>{theAnswer}</p>

            {sources.map((source) => (
                <button
                    key={source.id}
                    type="button"
                    onClick={() => {
                        setFollowed(source);
                    }}
                >
                    {checkingFor(source)}
                </button>
            ))}

            <EvidenceInspector
                session={signedIn}
                source={followed}
                transport={transport}
                onDismiss={() => {
                    setFollowed(null);
                }}
                onOpenMessage={onOpenMessage}
            />
        </LocalizationProvider>
    );
}

function checking(
    transport: MailFathomTransport,
    panel: Omit<Parameters<typeof Checking>[0], 'transport'> = {},
): ReturnType<typeof render> {
    return render(<Checking transport={transport} {...panel} />);
}

/** Presses the citation naming one source, which is the one action reaching the panel from anywhere on the answer. */
function check(source: DeclaredSource = agreement): void {
    fireEvent.click(screen.getByRole('button', { name: checkingFor(source) }));
}

/** Lets an answer the deployment has just given reach the screen, which is several turns of parsing away from it. */
async function settled(): Promise<void> {
    await act(async () => {
        await Promise.resolve();
    });
}

// jsdom evaluates no media query, and the suite's own stand-in answers `false` to every one — which is the narrow
// window, and is why the panel is a dialog unless a test says otherwise. A width is stated by answering the width
// queries the way a wide window would, and put back afterwards for the reason a pinned zone is.
const mediaBefore = Object.getOwnPropertyDescriptor(window, 'matchMedia');

function windowIsWide(): void {
    Object.defineProperty(window, 'matchMedia', {
        configurable: true,
        value: (query: string) => ({
            media: query,
            matches: query.startsWith('(min-width'),
            addEventListener: () => undefined,
            removeEventListener: () => undefined,
        }),
    });
}

// The zone an instant is placed against, which is the reader's own and never one this client names.
const zoneBefore = process.env['TZ'];

afterEach(() => {
    process.env['TZ'] = zoneBefore;

    if (mediaBefore !== undefined) {
        Object.defineProperty(window, 'matchMedia', mediaBefore);
    }
});

// One character of an instant's spelling is the runtime's rather than the client's: ICU writes the space before `AM`
// as a narrow no-break one from version 72, and the pipeline's Node and a developer's need not carry the same ICU.
function spelled(said: string): (content: string) => boolean {
    return (content) => content.replaceAll('\u202f', ' ') === said;
}

describe('EvidenceInspector', () => {
    it('says what the panel is for while nothing is being checked', () => {
        checking(answering(quotedPassage).transport);

        expect(screen.getByText('Press a citation to see the passage it rests on.')).toBeDefined();
    });

    it('asks the deployment for the place the citation the reader pressed points at', async () => {
        const asked = answering(quotedPassage);
        checking(asked.transport);

        check();

        await waitFor(() => {
            expect(asked.requests).toHaveLength(1);
        });

        const [request] = asked.requests;

        expect(request?.path).toContain('/citations/resolution');
        expect(JSON.parse(request?.body ?? 'null')).toEqual({
            citations: [{ kind: 'fragment', email, fragment: 'f-4' }],
        });
    });

    it('quotes the passage the fact was drawn from, beside the message it stands in', async () => {
        checking(answering(quotedPassage).transport);

        check();

        expect(await screen.findByText(passage.text)).toBeDefined();
        expect(screen.getByText(passage.text).tagName).toBe('Q');
        expect(screen.getByText('Renewal terms')).toBeDefined();
        expect(screen.getByText('work · Archive')).toBeDefined();
    });

    it.each([
        ['Europe/Warsaw', 'August 26, 2026 at 11:00 AM'],
        ['America/Los_Angeles', 'August 26, 2026 at 2:00 AM'],
    ])('places the message against the day the reader is actually having, in %s', async (zone, said) => {
        process.env['TZ'] = zone;
        checking(answering(quotedPassage).transport);

        check();

        expect(await screen.findByText(passage.text)).toBeDefined();
        expect(screen.getByText(spelled(said))).toBeDefined();
    });

    it('places the message in the zone the reader’s own record states, not the one this machine reports', async () => {
        process.env['TZ'] = 'America/Los_Angeles';

        render(
            <ReadingZoneContext value="Europe/Warsaw">
                <Checking transport={answering(quotedPassage).transport} />
            </ReadingZoneContext>,
        );

        check();

        expect(await screen.findByText(passage.text)).toBeDefined();
        expect(screen.getByText(spelled('August 26, 2026 at 11:00 AM'))).toBeDefined();
    });

    it('words the date the sender wrote where no receiving hop recorded one', async () => {
        process.env['TZ'] = 'Europe/Warsaw';
        checking(
            answering(
                resolved({
                    outcome: 'Resolved',
                    message: { ...standingIn, receivedAt: null },
                    fragment: passage,
                }),
            ).transport,
        );

        check();

        expect(await screen.findByText(passage.text)).toBeDefined();
        expect(screen.getByText(spelled('July 1, 2026 at 2:00 PM'))).toBeDefined();
    });

    it('states this deployment’s account of a picture plainly, so it is not read as a quotation', async () => {
        const described: DeclaredSource = { ...agreement, medium: 'Depicted' };

        checking(answering(quotedPassage).transport, { sources: [described] });

        check(described);

        expect((await screen.findByText(passage.text)).tagName).toBe('P');
    });

    it('names the file a fact was drawn from and how large it is', async () => {
        const carried: DeclaredSource = { ...agreement, target: { kind: 'attachment', email, attachmentPosition: 0 } };

        checking(answering(resolved({ outcome: 'Resolved', message: standingIn, attachment: file })).transport, {
            sources: [carried],
        });

        check(carried);

        expect(await screen.findByText('12 kB')).toBeDefined();
        expect(screen.getByText('Renewal terms')).toBeDefined();
    });

    it('says a fact resting on the message itself rather than on one passage of it', async () => {
        const whole: DeclaredSource = { ...agreement, target: { kind: 'email', email } };

        checking(answering(resolved({ outcome: 'Resolved', message: standingIn })).transport, { sources: [whole] });

        check(whole);

        expect(
            await screen.findByText('This rests on the message itself rather than on one passage of it.'),
        ).toBeDefined();
    });

    it('says a passage cut again since the answer was composed, and keeps the message it came from', async () => {
        checking(answering(resolved({ outcome: 'Unresolvable', message: standingIn })).transport);

        check();

        expect(
            await screen.findByText(
                'The passage this rests on has been cut again since the answer was composed, so it cannot be shown. The message it came from is above.',
            ),
        ).toBeDefined();
        expect(screen.getByText('Renewal terms')).toBeDefined();
    });

    it('says a stored copy this deployment could not read, which is neither a private source nor a moved passage', async () => {
        checking(answering(resolved({ outcome: 'Unresolvable' })).transport);

        check();

        expect(
            await screen.findByText(
                'This deployment could not read its own stored copy of the message behind this source. The fact rests on what was read when the answer was composed.',
            ),
        ).toBeDefined();
    });

    it('keeps a source this sign-in may not read private, and says what reaching it would take', async () => {
        checking(answering(resolved({ outcome: 'PrivateSource' })).transport);

        check();

        expect(
            await screen.findByText(
                'Ask whoever administers this deployment for access to the account it was read from.',
            ),
        ).toBeDefined();
        expect(screen.getByText('private source')).toBeDefined();
        expect(screen.queryByText(passage.text)).toBeNull();
    });

    it('says it is opening the source while the read is out, rather than waiting in silence', async () => {
        const waiting = holding();
        checking(waiting.transport);

        check();

        expect(await screen.findByText('Opening the source…')).toBeDefined();
    });

    // `missing` is in the panel's own table because the reasons are a closed set, and no status this route answers
    // reaches it: a `404` here is the route being unreachable rather than one message the deployment has let go of.
    it.each([
        [401, '', 'The session ended while this source was being opened. Sign in again to see it.'],
        [403, '', 'This account is not allowed to read this source.'],
        [500, '', 'No connection to the server — this source cannot be opened.'],
        [
            200,
            'not a resolution',
            'The source arrived in a form this client could not read, which is a defect worth reporting.',
        ],
    ] as const)(
        'says what a read that did not happen at all failed on, and what to do about it (%i)',
        async (status, body, said) => {
            checking(() => Promise.resolve({ status, body, headers: {} }));

            check();

            expect(await screen.findByText(said)).toBeDefined();
        },
    );

    it('says the session ended rather than asking a deployment with nobody signed in', async () => {
        const asked = answering(quotedPassage);
        checking(asked.transport, { signedIn: null });

        check();

        expect(
            await screen.findByText('The session ended while this source was being opened. Sign in again to see it.'),
        ).toBeDefined();
        expect(asked.requests).toHaveLength(0);
    });

    it('says a source pointing at a kind this build cannot open, rather than reading nothing', async () => {
        const unfollowable: DeclaredSource = { ...agreement, target: null };
        const asked = answering(quotedPassage);

        checking(asked.transport, { sources: [unfollowable] });

        check(unfollowable);

        expect(
            await screen.findByText('This answer points at a kind of source this client cannot open.'),
        ).toBeDefined();
        expect(asked.requests).toHaveLength(0);
    });

    it('reaches the whole message in one further action', async () => {
        const opened = vi.fn();
        checking(answering(quotedPassage).transport, { onOpenMessage: opened });

        check();

        fireEvent.click(await screen.findByRole('button', { name: 'Open in mail' }));

        expect(opened).toHaveBeenCalledWith(email);
    });

    it('offers no way into mail where the surface drawing the answer has nowhere to go', async () => {
        checking(answering(quotedPassage).transport);

        check();

        expect(await screen.findByText(passage.text)).toBeDefined();
        expect(screen.queryByRole('button', { name: 'Open in mail' })).toBeNull();
    });

    it('never draws an older source’s answer under the one being checked', async () => {
        const second: DeclaredSource = {
            ...agreement,
            id: 'c-2',
            label: 'SLA addendum.pdf',
            target: { kind: 'fragment', email, fragment: 'f-9' },
        };
        const later = { fragmentId: 'f-9', ordinal: 1, text: 'Response within four business hours' };

        const waiting = holding();
        checking(waiting.transport, { sources: [agreement, second] });

        check();
        check(second);

        waiting.answer(1, resolved({ outcome: 'Resolved', message: standingIn, fragment: later }));
        await settled();

        expect(screen.getByText(later.text)).toBeDefined();

        waiting.answer(0, quotedPassage);
        await settled();

        expect(screen.queryByText(passage.text)).toBeNull();
        expect(screen.getByText(later.text)).toBeDefined();
    });

    it('stands beside the answer where the window has room for it', async () => {
        windowIsWide();
        checking(answering(quotedPassage).transport);

        check();

        expect(await screen.findByRole('region', { name: 'Evidence' })).toBeDefined();
        expect(screen.queryByRole('dialog')).toBeNull();
    });

    it('stands over the answer where the window has not', async () => {
        checking(answering(quotedPassage).transport);

        check();

        expect(await screen.findByRole('dialog', { name: 'Evidence' })).toBeDefined();
    });

    it('returns the reader to the citation they pressed, with the answer still on the screen', async () => {
        windowIsWide();
        checking(answering(quotedPassage).transport);

        const citation = screen.getByRole('button', { name: checkingFor(agreement) });

        // Focused before it is pressed, because a browser focuses a button a pointer presses and jsdom does not: what
        // the panel is asserted to give back is whatever had the keyboard when the source arrived.
        citation.focus();
        fireEvent.click(citation);

        expect(await screen.findByText(passage.text)).toBeDefined();
        expect(document.activeElement).toBe(screen.getByRole('region', { name: 'Evidence' }));

        fireEvent.click(screen.getByRole('button', { name: 'Back to the answer' }));

        await waitFor(() => {
            expect(document.activeElement).toBe(citation);
        });

        expect(screen.getByText(theAnswer)).toBeDefined();
        expect(screen.getByText('Press a citation to see the passage it rests on.')).toBeDefined();
    });
});

// What the whole arrangement is for: every citation in every block leads to the same panel, so checking a fact is one
// press wherever the fact is drawn and reaching the message behind it is the second. It is asserted over the canvas and
// the build's own renderers rather than over a citation rendered on its own, because what is being proved is that the
// blocks and the panel are wired together at all.
describe('reaching evidence from a block', () => {
    const backed: BlockEvidence = {
        support: 'Supported',
        citations: [agreement.id],
        freshness: { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' },
        conflictingClaims: [],
    };

    const declared = new Map([[agreement.id, agreement]]);

    const freshness = { staleness: 'Current', observedAt: '2026-09-20T08:00:00+00:00' } as const;

    // One block of each type the build registers a renderer for, each resting on the one source above, because what is
    // being proved is that every one of them leads to the same panel.
    const answer: AnswerBlock = {
        type: 'answer',
        named: 'answer',
        evidence: backed,
        answer: { text: theAnswer, confidence: 'High' },
    };

    const evidenceList: AnswerBlock = {
        type: 'evidenceList',
        named: 'evidenceList',
        evidence: backed,
        entries: [{ source: agreement.id, fragment: passage.text, relevance: 0.94, freshness }],
    };

    const timeline: AnswerBlock = {
        type: 'timeline',
        named: 'timeline',
        evidence: backed,
        entries: [
            {
                occurredAt: '2026-08-26T09:00:00+00:00',
                summary: 'The rate was agreed.',
                subject: 'Renewal',
                sources: [agreement.id],
            },
        ],
    };

    const factTable: AnswerBlock = {
        type: 'factTable',
        named: 'factTable',
        evidence: backed,
        columns: ['amount'],
        rows: [{ cells: [{ value: 'EUR 1,200', sources: [agreement.id] }] }],
    };

    const attachmentGallery: AnswerBlock = {
        type: 'attachmentGallery',
        named: 'attachmentGallery',
        evidence: backed,
        entries: [
            {
                source: agreement.id,
                name: 'Master agreement.pdf',
                mediaType: 'application/pdf',
                sizeOctets: 12_000,
                availability: 'Stored',
            },
        ],
    };

    function Drawn({ block, transport }: { readonly block: AnswerBlock; readonly transport: MailFathomTransport }) {
        const [followed, setFollowed] = useState<DeclaredSource | null>(null);
        const blocks: readonly ArrivedAnswerBlock[] = [{ sequence: 1, block }];

        return (
            <LocalizationProvider>
                <AnswerCanvas
                    blocks={blocks}
                    evidence={
                        <EvidenceInspector
                            session={session}
                            source={followed}
                            transport={transport}
                            onDismiss={() => {
                                setFollowed(null);
                            }}
                        />
                    }
                    planSchemaVersion={2}
                    running={false}
                    sources={declared}
                    onFollowSource={(source) => {
                        setFollowed(declared.get(source) ?? null);
                    }}
                />
            </LocalizationProvider>
        );
    }

    const citation = 'Citation 1: Master agreement.pdf';

    it.each([
        ['a citation beside the words it backs', answer, citation],
        ['an entry in the list of messages the answer rests on', evidenceList, /^Evidence: Master agreement\.pdf/u],
        ['a citation under an event on the timeline', timeline, citation],
        ['a citation inside one cell of a fact table', factTable, citation],
        ['a file in the gallery', attachmentGallery, /^Attachment: Master agreement\.pdf/u],
    ])('opens the source behind %s in one press', async (_named, block, pressed) => {
        render(<Drawn block={block} transport={answering(quotedPassage).transport} />);

        fireEvent.click(screen.getByRole('button', { name: pressed }));

        expect(await screen.findByText('Renewal terms')).toBeDefined();
        expect(screen.getByText('work · Archive')).toBeDefined();
    });
});

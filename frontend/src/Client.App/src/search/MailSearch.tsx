// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useState, type ReactNode } from 'react';
import {
    calendarDayOf,
    longestSearchText,
    phraseNotRead,
    readMailSearchPhrase,
    readsMailSearchPhrases,
    type ClientSession,
    type MailAccount,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { controlShapes } from '../controls/controlShapes';
import { Icon } from '../controls/Icon';
import { SurfaceControl } from '../controls/SurfaceControl';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { ListHeadRowContext } from '../mailSpace/listHeadRow';
import { useMailboxesDrawer } from '../mailSpace/mailboxesDrawer';
import type { MailScope } from '../workspace/mailScope';
import { mostRecentSearches } from '../workspace/rememberedWorkspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { SearchFilters } from './SearchFilters';
import { SearchResults } from './SearchResults';
import {
    askable,
    askFromPhrase,
    askIn,
    askKey,
    narrowings,
    widened,
    withoutCriterion,
    type MailSearchAsk,
} from './searchAsk';

// Finding a message, which stands at the top of the mail a person is looking at rather than on a screen of its own.
// That is where somebody reaches for it — they are looking at a folder and the message is not in front of them — and
// it is why what stands under this is the folder's own list until a search is in force and the results afterwards: one
// column, one row shape, and no view to navigate away to and back from.
//
// It searches the scope somebody is looking at, and the scope is copied onto the search as filters they can see and
// take off. That is the whole answer to the hardest part of this screen: a search inside a folder, a search across one
// account, and a search across everything are three different questions, and somebody who cannot see which one they
// asked reads an empty result as an absence rather than as something to widen.
//
// The field takes a description as well as words, on a deployment that reads one. What that means here is one extra
// step in front of the search: the sentence goes to the deployment, comes back as filters and criteria, and those are
// what the search runs under — visibly, as objects on this screen, rather than as a query nobody can see. A search
// that silently reinterpreted would be one nobody can correct, and a wrong interpretation then reads as an absence of
// mail rather than as something to fix.
//
// What the field promises follows what the deployment can do, and it is asked before anybody types. A field offering
// to take a description over a deployment that can only match words fails a person at the one moment they trusted it,
// so the promise is made from an answer rather than from hope: no reading, no description, and the plain word search
// is exactly what it always was.

// Reading the clock is the caller's, so the day a sentence's *last week* is resolved against is one a test decided
// rather than the day the suite happened to run on. Declared once rather than defaulted inline, for the reason
// `useConnection.ts` gives: a new function on every render is a new dependency on every render, and the effect that
// sends a sentence to be read would restart forever.
const systemClock = (): Date => new Date();

export function MailSearch({
    session,
    transport,
    scope,
    accounts,
    online,
    children,
    onOpen,
    now = systemClock,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** What the client is looking at, which is what a search started now is scoped to. */
    readonly scope: MailScope;

    readonly accounts: readonly MailAccount[];
    readonly online: boolean;

    /** What stands in this column while no search is in force, which is the mail in scope. */
    readonly children: ReactNode;

    /** Opens a result, handed straight to the results below — the reason `MessageList` gives. */
    readonly onOpen: (storedEmailId: string, subject: string | null) => void;

    /**
     * What the current instant is, which is the calendar day the deployment resolves a relative expression against.
     * It travels with the sentence rather than being taken at the other end, because the day somebody means by
     * *yesterday* is the one where they are standing rather than the one the deployment is standing in.
     */
    readonly now?: () => Date;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const openMailboxes = useMailboxesDrawer();

    const [typed, setTyped] = useState('');

    // The end of the head row, where the list below renders the control that opens its filters — `listHeadRow.ts`
    // says why the place crosses rather than the state. Held as state rather than a ref because the list reads it
    // during render, and a ref written after this row mounted would be read as `null` on the render that mattered.
    // The element leaving is not written back: that happens while this tree is being taken down, and a state change
    // scheduled then commits once more after the root has been cleared — over the document `main.tsx` puts in front
    // of somebody when a failure escaped every boundary, which would wipe it.
    const [filtersPlace, setFiltersPlace] = useState<HTMLElement | null>(null);

    // Why the last submission was not run, or `null` where it was. Two sentences rather than one, because a person who
    // pressed the button with nothing typed and one who pasted a document into the field have to do different things.
    const [refused, setRefused] = useState<MessageKey | null>(null);

    // The search being read, held rather than derived, because a search is something somebody submitted rather than
    // something the field is: a filter taken off replaces it, and typing into the field does not.
    const [ask, setAsk] = useState<MailSearchAsk | null>(null);

    // Whether this deployment turns a sentence into filters, and `null` while nobody has asked yet. It is what the
    // field promises rather than something a search discovers, which is why it is asked once on mount and held.
    const [readsPhrases, setReadsPhrases] = useState<boolean | null>(null);

    // The sentence submitted and not yet read. It stands where the results will, so somebody who typed a sentence sees
    // that something is happening to it rather than an empty column and a field that stopped responding.
    const [beingRead, setBeingRead] = useState<string | null>(null);

    useEffect(() => {
        let listening = true;

        void readsMailSearchPhrases(session, transport).then((result) => {
            if (listening) {
                // A deployment that could not be asked reads no sentence as far as this screen is concerned: the field
                // then promises words, which is a promise every deployment keeps.
                setReadsPhrases(result.outcome === 'read' && result.value);
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport]);

    // The one effect that sends a sentence to be read. An answer to a submission this screen has moved on from is
    // discarded rather than cancelled, exactly as the results below discard a page of a search nobody is reading.
    useEffect(() => {
        if (beingRead === null) {
            return;
        }

        let listening = true;

        void readMailSearchPhrase(session, transport, beingRead, calendarDayOf(now())).then((result) => {
            if (!listening) {
                return;
            }

            // A reading that failed is not a search that failed. What is left is the words somebody typed, which is
            // the search this screen ran before any of this existed and the one a deployment with no provider runs.
            setAsk(askFromPhrase(scope, beingRead, result.outcome === 'read' ? result.value : phraseNotRead));
            setBeingRead(null);
        });

        return () => {
            listening = false;
        };
    }, [session, transport, scope, beingRead, now]);

    function search(text: string): void {
        if (!askable(text, longestSearchText)) {
            setRefused(text.trim().length === 0 ? 'search.blank' : 'search.tooLong');

            return;
        }

        const words = text.trim();

        setRefused(null);
        setTyped(words);
        revise({ recentSearches: withRecent(workspace.recentSearches, words) });

        // A deployment that has not answered yet is one that reads nothing, for the same reason one that answered no
        // is: the search runs now, over the words, rather than waiting on a capability nobody promised.
        if (readsPhrases === true) {
            setAsk(null);
            setBeingRead(words);
        } else {
            setBeingRead(null);
            setAsk(askIn(scope, words));
        }
    }

    function stopSearching(): void {
        setRefused(null);
        setTyped('');
        setBeingRead(null);
        setAsk(null);
    }

    return (
        <div className="flex min-h-0 flex-1 flex-col">
            <form
                className="flex flex-wrap items-center gap-2.25 border-b border-line px-3 py-2.5"
                onSubmit={(event) => {
                    event.preventDefault();
                    search(typed);
                }}
            >
                {/* The way to the mailboxes stands first in this row wherever they are behind a drawer, which is the
                    design's own head row: the drawer, the field, and what searches it, on one line. */}
                {openMailboxes === null ? null : (
                    <SurfaceControl label={translate('mailboxes.open')} icon="menu" edged onActivate={openMailboxes} />
                )}

                {/* One pill across the column, which is where the design puts finding a message and what it draws it
                    as. Its name is carried rather than drawn: a placeholder is not an accessible name — it leaves as
                    soon as somebody types — and the design's shape has no room for a visible one. */}
                <label className="min-w-0 flex-1 text-sm">
                    <span className="sr-only">{translate('search.label')}</span>
                    <input
                        type="search"
                        className="min-h-12 w-full rounded-full border border-line bg-rail px-4 text-md text-text transition placeholder:text-faint hover:border-line-strong workspace:min-h-0 workspace:px-3.25 workspace:py-2.25 workspace:text-base"
                        placeholder={translate(
                            readsPhrases === true ? 'search.placeholderDescribed' : 'search.placeholder',
                        )}
                        value={typed}
                        onChange={(event) => {
                            setRefused(null);
                            setTyped(event.target.value);
                        }}
                    />
                </label>

                {/* Its symbol alone, as the design draws it; the name is on the control for whoever is not looking. */}
                <button
                    type="submit"
                    aria-label={translate('search.submit')}
                    title={translate('search.submit')}
                    className={`flex shrink-0 items-center whitespace-nowrap text-muted transition ${controlShapes.symbol}`}
                >
                    <Icon name="search" className="size-4.75" />
                </button>

                {ask === null && beingRead === null ? null : (
                    <SecondaryButton label={translate('search.stop')} onActivate={stopSearching} />
                )}

                {/* Where the list puts the control that opens its filters, as the last thing on the row. */}
                <span
                    ref={(place) => {
                        if (place !== null) {
                            setFiltersPlace(place);
                        }
                    }}
                    className="contents"
                />
            </form>

            {refused === null ? null : (
                <p className="px-3 pt-2 text-sm text-warning" role="alert">
                    {translate(refused, { longest: String(longestSearchText) })}
                </p>
            )}

            {/* Offered where there is nothing else in the column's own controls to read, which is where somebody is
                about to type. They are this tab's own and they go with the credential, which is what the workspace
                already promises everything it holds. */}
            {ask === null && beingRead === null && workspace.recentSearches.length > 0 ? (
                <RecentSearches
                    searches={workspace.recentSearches}
                    onSearch={search}
                    onForget={() => {
                        revise({ recentSearches: [] });
                    }}
                />
            ) : null}

            {/* What stands under the row while a sentence is being read. It is a line rather than a spinner over the
                folder's own list, because the list below is about to be replaced by results and a column that goes on
                showing the inbox reads as a search that did nothing. */}
            {beingRead === null ? null : (
                <p className="px-3 pt-2 text-sm text-muted" role="status">
                    {translate('search.readingPhrase')}
                </p>
            )}

            {ask === null ? (
                beingRead !== null ? null : (
                    <ListHeadRowContext value={filtersPlace}>{children}</ListHeadRowContext>
                )
            ) : (
                <>
                    <div className="px-3 pt-2">
                        <SearchFilters
                            ask={ask}
                            accounts={accounts}
                            onNarrow={setAsk}
                            onRemoveCriterion={(criterion) => {
                                setAsk(withoutCriterion(ask, criterion));
                            }}
                        />
                    </div>

                    {/* Keyed by the search, so changing a word or a filter starts a search rather than reconciles one:
                        a cursor belongs to the ranked list it was issued in, and a relevance order is recomputed for
                        every search. */}
                    <SearchResults
                        key={askKey(ask)}
                        session={session}
                        transport={transport}
                        ask={ask}
                        online={online}
                        narrowed={narrowings(ask).length > 0}
                        onWiden={() => {
                            setAsk(widened(ask));
                        }}
                        onOpen={onOpen}
                    />
                </>
            )}
        </div>
    );
}

// What was searched for before, offered back so that a search somebody ran an hour ago is one press rather than
// something to retype. Forgetting them is offered beside them: what a person looked for is theirs, and a list of it
// they cannot clear is one they did not choose to keep.
function RecentSearches({
    searches,
    onSearch,
    onForget,
}: {
    readonly searches: readonly string[];
    readonly onSearch: (text: string) => void;
    readonly onForget: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex flex-col gap-1 px-3">
            <p className="text-sm text-muted">{translate('search.recent')}</p>

            <ul aria-label={translate('search.recent')} className="flex flex-wrap items-center gap-2">
                {searches.map((text) => (
                    <li key={text}>
                        <SecondaryButton
                            label={text}
                            onActivate={() => {
                                onSearch(text);
                            }}
                        />
                    </li>
                ))}

                <li>
                    <SecondaryButton label={translate('search.forgetRecent')} onActivate={onForget} />
                </li>
            </ul>
        </div>
    );
}

/**
 * The recent searches with this one at the front, held to what one tab may accumulate.
 *
 * A search run again moves rather than repeating, which is what keeps the list short without anything having to look
 * for a duplicate before writing one.
 */
function withRecent(searches: readonly string[], text: string): readonly string[] {
    return [text, ...searches.filter((searched) => searched !== text)].slice(0, mostRecentSearches);
}

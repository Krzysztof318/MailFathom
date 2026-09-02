// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useLayoutEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';
import {
    mostSearchResults,
    readMailSearch,
    type ClientFailure,
    type ClientFailureReason,
    type ClientSession,
    type MailFathomTransport,
    type MailSearchPage,
    type MailSearchRanking,
    type MailSearchResult,
    type MailSearchRetrieval,
    type MailSemanticSearch,
} from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { MessageRow } from '../messageRows/MessageRow';
import { estimatedRowHeight, offsetOfRow, windowOf } from '../messageRows/rowWindow';
import { useWorkspace } from '../workspace/useWorkspace';
import { matchedRuns } from './matchedRuns';
import { queryFor, type MailSearchAsk } from './searchAsk';

// What one search found, paged forward through the ranked list and drawn with the list's own row.
//
// It is mounted with the search as its key, so changing a word or a filter starts a search rather than reconciles one:
// a cursor belongs to the ranked list it was issued in, the ranking is recomputed for every search, and there is no
// correct way to carry a page of one search into another.
//
// Unlike the folder's list this holds every page it has read rather than a window of them. A relevance order is not
// somewhere a reader can scroll back to and read again — the deployment issues no backward cursor here and the list
// stops at two hundred — so pages are kept and the document is what is windowed.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

// Why a page ranked by words alone was ranked that way, which is the difference between a deployment that embeds
// nothing by choice and one whose provider is refusing. `Available` is not here because a page ranked by words alone on
// a deployment ranking by meaning is not a state this surface produces.
const wordsOnlyReasons: Readonly<Record<MailSemanticSearch, MessageKey | null>> = {
    Inactive: 'search.wordsOnlyInactive',
    Degraded: 'search.wordsOnlyDegraded',
    Available: null,
};

/** Everything one search has answered so far, which grows a page at a time and is replaced by nothing. */
interface FoundMail {
    readonly results: readonly MailSearchResult[];

    /** The cursor the next page is asked with, or `null` where the ranked list ends here. */
    readonly nextCursor: string | null;

    readonly retrievalMode: MailSearchRetrieval;
    readonly semanticSearch: MailSemanticSearch;
}

export function SearchResults({
    session,
    transport,
    ask,
    online,
    narrowed,
    onWiden,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** The search being read. Held by the caller across renders, because a fresh object would restart the read. */
    readonly ask: MailSearchAsk;

    readonly online: boolean;

    /** Whether the search has anything on it to take off, which is what an empty result offers. */
    readonly narrowed: boolean;

    readonly onWiden: () => void;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();

    const [found, setFound] = useState<FoundMail | null>(null);
    const [failure, setFailure] = useState<ClientFailure | null>(null);

    const [scrollTop, setScrollTop] = useState(0);
    const [viewport, setViewport] = useState(0);
    const [rowHeight, setRowHeight] = useState(estimatedRowHeight);
    const [focusedRow, setFocusedRow] = useState(0);

    const scroller = useRef<HTMLDivElement>(null);
    const elements = useRef(new Map<number, HTMLLIElement>());
    const wantsFocus = useRef(false);

    const results = found?.results ?? [];
    const rowCount = results.length;
    const drawn = windowOf(rowCount, rowHeight, scrollTop, viewport);
    const lastDrawn = drawn.first + drawn.count - 1;

    // Whether the ranked list goes on past what is held. The count is checked as well as the cursor, because how far a
    // ranked list reaches is this surface's bound rather than a promise about the answer: a deployment that kept
    // answering with a cursor would otherwise be a screen reading pages until one of them stopped.
    const moreToRead = found !== null && found.nextCursor !== null && rowCount < mostSearchResults;

    // Which page is wanted is worked out during render rather than kept beside what was found, for the reason the
    // folder's list works it out: two pieces of state that have to agree are one piece of state and a function. A read
    // in flight reads as the same cursor being wanted, so the effect below does not run again until an answer has
    // changed what is wanted — which is what starts one read per page rather than one per render.
    const wanted =
        !online || failure !== null
            ? null
            : found === null
              ? { cursor: null }
              : moreToRead && lastDrawn >= rowCount - 1
                ? { cursor: found.nextCursor }
                : null;

    const wantedCursor = wanted?.cursor ?? null;
    const wanting = wanted !== null;

    // The one effect that puts a request on the wire. An answer to a read this screen has moved on from is discarded
    // rather than cancelled, which is what a screen that may be showing another search by then actually needs.
    useEffect(() => {
        if (!wanting) {
            return;
        }

        let listening = true;

        void readMailSearch(session, transport, queryFor(ask, wantedCursor)).then((result) => {
            if (!listening) {
                return;
            }

            if (result.outcome === 'failed') {
                setFailure(result.failure);
            } else {
                setFound((current) => withPage(current, result.value));
            }
        });

        return () => {
            listening = false;
        };
    }, [session, transport, ask, wantedCursor, wanting]);

    // The two measurements the window is arithmetic over, taken after the browser has laid the results out rather than
    // written down as numbers here, for the reason the folder's list measures them: the row's height is a token
    // decision this must not hold a second copy of.
    useLayoutEffect(() => {
        const element = scroller.current;

        if (element === null) {
            return;
        }

        if (element.clientHeight !== viewport) {
            setViewport(element.clientHeight);
        }

        const measured = elements.current.get(drawn.first)?.offsetHeight ?? 0;

        if (measured > 0 && measured !== rowHeight) {
            setRowHeight(measured);
        }

        if (wantsFocus.current) {
            const row = elements.current.get(focusedRow);

            if (row !== undefined) {
                row.focus();
                wantsFocus.current = false;
            }
        }
    }, [viewport, rowHeight, rowCount, drawn.first, focusedRow]);

    // A window resized changes how many rows are drawn, and a resize is not a commit.
    useEffect(() => {
        function remeasure(): void {
            setViewport(scroller.current?.clientHeight ?? 0);
        }

        window.addEventListener('resize', remeasure);

        return () => {
            window.removeEventListener('resize', remeasure);
        };
    }, []);

    function tryAgain(): void {
        setFailure(null);
    }

    function reveal(row: number): void {
        const element = scroller.current;

        if (element === null) {
            return;
        }

        const top = offsetOfRow(row, rowHeight);

        if (top < element.scrollTop) {
            element.scrollTop = top;
        } else if (top + rowHeight > element.scrollTop + element.clientHeight) {
            element.scrollTop = top + rowHeight - element.clientHeight;
        }
    }

    function moveTo(row: number): void {
        const reached = Math.min(Math.max(row, 0), Math.max(rowCount - 1, 0));

        reveal(reached);
        setFocusedRow(reached);
        wantsFocus.current = true;
    }

    function open(row: number): void {
        const result = results[row];

        if (result !== undefined) {
            setFocusedRow(row);
            revise({ selection: result.id });
        }
    }

    function onKeyDown(event: KeyboardEvent<HTMLUListElement>): void {
        switch (event.key) {
            case 'ArrowDown':
                moveTo(focusedRow + 1);
                break;
            case 'ArrowUp':
                moveTo(focusedRow - 1);
                break;
            case 'Home':
                moveTo(0);
                break;
            case 'End':
                moveTo(rowCount - 1);
                break;
            case 'Enter':
                open(focusedRow);
                break;
            default:
                return;
        }

        event.preventDefault();
    }

    if (!online) {
        return <Note>{translate('connection.offline')}</Note>;
    }

    if (rowCount === 0 && failure !== null) {
        return (
            <div className="flex flex-col items-start gap-2">
                <p className="text-sm text-warning" role="alert">
                    {translate('search.failed', { reason: translate(failureLabels[failure.reason]) })}
                </p>

                {/* Searching again is the way out of exactly one of the four failures, for the reason
                    `shell/ConnectionSummary.tsx` gives: the other three repeat identically on a second attempt. */}
                {failure.reason === 'unavailable' ? (
                    <SecondaryButton label={translate('connection.retry')} onActivate={tryAgain} />
                ) : null}
            </div>
        );
    }

    if (rowCount === 0 && wanting) {
        return <Note announced>{translate('search.searching')}</Note>;
    }

    // A search that matched nothing says so and offers the way out, which is taking a filter off rather than typing
    // the same words again. The filters that produced it are drawn above this by the screen that owns them, so what is
    // missing here is only the act of widening.
    if (rowCount === 0) {
        return (
            <div className="flex flex-col items-start gap-2">
                <p className="text-sm text-muted" role="status">
                    {translate('search.nothingFound')}
                </p>

                {narrowed ? <SecondaryButton label={translate('search.widen')} onActivate={onWiden} /> : null}
            </div>
        );
    }

    const wordsOnly = found?.retrievalMode === 'Lexical' ? wordsOnlyReasons[found.semanticSearch] : null;

    return (
        <div className="flex min-h-0 flex-1 flex-col gap-2 px-3">
            {/* A narrower answer says it is narrower, rather than being a quieter one. Which of the two sentences it is
                separates what this deployment does not do from what is not working on it, and neither of them is what
                a credential may not do — that is the notice the frame draws. */}
            {wordsOnly === null ? null : (
                <p className="text-sm text-muted" role="status">
                    {translate(wordsOnly)}
                </p>
            )}

            {failure === null ? null : (
                <div className="flex flex-wrap items-center gap-2">
                    <p className="text-sm text-warning" role="alert">
                        {translate('search.partiallyFailed', { reason: translate(failureLabels[failure.reason]) })}
                    </p>

                    {failure.reason === 'unavailable' ? (
                        <SecondaryButton label={translate('connection.retry')} onActivate={tryAgain} />
                    ) : null}
                </div>
            )}

            <div
                ref={scroller}
                className="min-h-0 flex-1 overflow-y-auto overscroll-contain"
                onScroll={(event) => {
                    setScrollTop(event.currentTarget.scrollTop);
                }}
            >
                {/* The rows that are not in the document, as the space they take. Outside the list rather than in it,
                    because a listbox holds options and nothing else. */}
                <div aria-hidden="true" style={{ height: `${String(drawn.above)}px` }} />

                <ul
                    aria-label={translate('search.resultsLabel')}
                    aria-busy={wanting}
                    role="listbox"
                    className="flex flex-col"
                    onKeyDown={onKeyDown}
                >
                    {Array.from({ length: drawn.count }, (_, at) => drawn.first + at).map((row) => {
                        const result = results[row];

                        if (result === undefined) {
                            return null;
                        }

                        return (
                            <MessageRow
                                key={result.id}
                                email={result}
                                position={row + 1}
                                open={workspace.selection === result.id}
                                selected={workspace.selection === result.id}
                                focusable={row === focusedRow}
                                note={<WhyItMatched result={result} />}
                                onOpen={() => {
                                    open(row);
                                }}
                                onPoint={() => {
                                    open(row);
                                }}
                                onPointerEnter={() => {
                                    // A search result is opened rather than swept over: picking several out is what
                                    // the folder's list is for, and a drag here would select nothing.
                                }}
                                onElement={(element) => {
                                    if (element === null) {
                                        elements.current.delete(row);
                                    } else {
                                        elements.current.set(row, element);
                                    }
                                }}
                            />
                        );
                    })}
                </ul>

                <div aria-hidden="true" style={{ height: `${String(drawn.below)}px` }} />

                {wanting ? (
                    <p className="px-3 py-2 text-sm text-muted" role="status">
                        {translate('search.readingMore')}
                    </p>
                ) : null}

                {/* Where the ranked list ends is two different sentences: a search that found everything it matched,
                    and one that reached as far as a ranked list goes. Only the second is something to act on. */}
                {moreToRead ? null : (
                    <p className="px-3 py-2 text-sm text-faint">
                        {translate(rowCount >= mostSearchResults ? 'search.mostResultsRead' : 'search.wholeSearchRead')}
                    </p>
                )}
            </div>
        </div>
    );
}

// What a result matched on, said in words where the deployment cut no extract to show instead. Exhaustive over the
// ranking rather than a test for one of its three members: a result that matched by meaning as well as by the words
// typed has to say so, and reading it as a header match would drop the half a reader could not otherwise know about.
const rankingSentences: Readonly<Record<MailSearchRanking, MessageKey>> = {
    LexicalRanking: 'search.matchedInMail',
    SemanticRanking: 'search.matchedByMeaning',
    BothRankings: 'search.matchedBothWays',
};

/**
 * Why one result is in the list, in the line the row's height already reserves.
 *
 * An extract is what a person can check for themselves, so it is preferred wherever the deployment cut one. Where it
 * cut none the ranking is said in words instead: a message ranked by meaning carries no part of it showing the words
 * that were typed, and a row with nothing under it would read as unexplained rather than as honestly matched.
 */
function WhyItMatched({ result }: { readonly result: MailSearchResult }) {
    const { translate } = useLocalization();
    const extract = result.snippets[0];

    return (
        <span className="block truncate text-faint">
            <span className="sr-only">{translate('search.whyItMatched')} </span>

            {extract === undefined
                ? translate(rankingSentences[result.matchedBy])
                : matchedRuns(extract).map((run, at) => (
                      <span
                          // The runs of one extract have no identity of their own, so their position is what they are:
                          // the extract is replaced whole whenever the result is, and nothing reorders them.
                          key={`${String(at)}-${run.text}`}
                          className={run.matched ? 'font-semibold text-accent-strong' : undefined}
                      >
                          {run.text}
                      </span>
                  ))}
        </span>
    );
}

// One more page, appended to what is already on the screen. The retrieval fields are the first page's rather than the
// newest one's: they describe the search rather than the exchange, and a page that arrived while a provider was
// recovering would otherwise silently rewrite the sentence explaining the results above it.
function withPage(current: FoundMail | null, page: MailSearchPage): FoundMail {
    if (current === null) {
        return {
            results: page.results,
            nextCursor: page.nextCursor,
            retrievalMode: page.retrievalMode,
            semanticSearch: page.semanticSearch,
        };
    }

    return { ...current, results: [...current.results, ...page.results], nextCursor: page.nextCursor };
}

function Note({ announced = false, children }: { readonly announced?: boolean; readonly children: ReactNode }) {
    return (
        <p className="text-sm text-muted" role={announced ? 'status' : undefined}>
            {children}
        </p>
    );
}

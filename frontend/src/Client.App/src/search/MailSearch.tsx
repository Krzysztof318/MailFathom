// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState, type ReactNode } from 'react';
import {
    longestSearchText,
    type ClientSession,
    type MailAccount,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { borderedControl } from '../controls/chrome';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import type { MailScope } from '../workspace/mailScope';
import { mostRecentSearches } from '../workspace/rememberedWorkspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { SearchFilters } from './SearchFilters';
import { SearchResults } from './SearchResults';
import { askable, askIn, askKey, narrowings, widened, type MailSearchAsk } from './searchAsk';

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
// The field accepts a phrase and nothing else today. The prototype words it as accepting a description too, which is
// stage 3's work writing filters out of a sentence — it lands on this screen rather than replacing it, which is why
// the filters here are objects with values in them for something to write into. Until it does, the field promises what
// it does.

export function MailSearch({
    session,
    transport,
    scope,
    accounts,
    online,
    children,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;

    /** What the client is looking at, which is what a search started now is scoped to. */
    readonly scope: MailScope;

    readonly accounts: readonly MailAccount[];
    readonly online: boolean;

    /** What stands in this column while no search is in force, which is the mail in scope. */
    readonly children: ReactNode;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();

    const [typed, setTyped] = useState('');

    // Why the last submission was not run, or `null` where it was. Two sentences rather than one, because a person who
    // pressed the button with nothing typed and one who pasted a document into the field have to do different things.
    const [refused, setRefused] = useState<MessageKey | null>(null);

    // The search being read, held rather than derived, because a search is something somebody submitted rather than
    // something the field is: a filter taken off replaces it, and typing into the field does not.
    const [ask, setAsk] = useState<MailSearchAsk | null>(null);

    function search(text: string): void {
        if (!askable(text, longestSearchText)) {
            setRefused(text.trim().length === 0 ? 'search.blank' : 'search.tooLong');

            return;
        }

        const words = text.trim();

        setRefused(null);
        setTyped(words);
        setAsk(askIn(scope, words));
        revise({ recentSearches: withRecent(workspace.recentSearches, words) });
    }

    function stopSearching(): void {
        setRefused(null);
        setTyped('');
        setAsk(null);
    }

    return (
        <div className="flex min-h-0 flex-col gap-2">
            <form
                className="flex flex-wrap items-center gap-2"
                onSubmit={(event) => {
                    event.preventDefault();
                    search(typed);
                }}
            >
                <label className="flex min-w-0 flex-1 flex-col gap-1 text-sm">
                    <span className="text-muted">{translate('search.label')}</span>
                    <input
                        type="search"
                        className={`w-full px-2 py-1 text-sm ${borderedControl}`}
                        placeholder={translate('search.placeholder')}
                        value={typed}
                        onChange={(event) => {
                            setRefused(null);
                            setTyped(event.target.value);
                        }}
                    />
                </label>

                <button className={`self-end px-2 py-1 text-sm ${borderedControl}`} type="submit">
                    {translate('search.submit')}
                </button>

                {ask === null ? null : (
                    <span className="self-end">
                        <SecondaryButton label={translate('search.stop')} onActivate={stopSearching} />
                    </span>
                )}
            </form>

            {refused === null ? null : (
                <p className="text-sm text-warning" role="alert">
                    {translate(refused, { longest: String(longestSearchText) })}
                </p>
            )}

            {/* Offered where there is nothing else in the column's own controls to read, which is where somebody is
                about to type. They are this tab's own and they go with the credential, which is what the workspace
                already promises everything it holds. */}
            {ask === null && workspace.recentSearches.length > 0 ? (
                <RecentSearches
                    searches={workspace.recentSearches}
                    onSearch={search}
                    onForget={() => {
                        revise({ recentSearches: [] });
                    }}
                />
            ) : null}

            {ask === null ? (
                children
            ) : (
                <>
                    <SearchFilters ask={ask} accounts={accounts} onNarrow={setAsk} />

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
        <div className="flex flex-col gap-1">
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

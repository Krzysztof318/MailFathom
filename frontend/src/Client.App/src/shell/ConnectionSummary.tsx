// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import type { ReactNode } from 'react';
import type { ClientFailureReason, MailAccountDirectory } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { needsAttention } from '../synchronization/synchronizationState';
import { AccountLine } from './AccountLine';
import { offers } from './capabilities';
import { ageOf } from './synchronizationAge';
import { mostReconnectionAttempts, type Connection } from './useConnection';

// The line above every space saying what the client is looking at and how current it is, and the short panel behind it
// naming each account. Three different things blur into one spinner unless they are kept apart, so each has its own
// sentence: whether this client can reach its deployment at all, whether that deployment is refreshing these accounts,
// and when each of them last took mail in.
//
// A credential that may not read mail says nothing here. There is no freshness to report and the reason is not about
// this line, so it is said once, where everything this credential may not do is said.

const failureLabels: Readonly<Record<ClientFailureReason, MessageKey>> = {
    unauthenticated: 'failure.unauthenticated',
    unauthorized: 'failure.unauthorized',
    unavailable: 'failure.unavailable',
    unreadable: 'failure.unreadable',
};

type Tone = 'healthy' | 'attention' | 'quiet';

const toneColours: Readonly<Record<Tone, string>> = {
    healthy: 'bg-healthy',
    attention: 'bg-warning',
    quiet: 'bg-faint',
};

interface Freshness {
    readonly message: MessageKey;
    readonly tone: Tone;
}

function freshnessOf(directory: MailAccountDirectory): Freshness {
    if (directory.accounts.length === 0) {
        return { message: 'connection.noAccounts', tone: 'quiet' };
    }

    if (!directory.synchronizationEnabled) {
        return { message: 'accounts.notRefreshing', tone: 'quiet' };
    }

    // An account nothing is fixing is read before the lag below, because it says something a lagging one does not.
    if (directory.accounts.some((account) => needsAttention(account.synchronizationState))) {
        return { message: 'connection.failing', tone: 'attention' };
    }

    const current = directory.accounts.every(
        (account) => !account.behind && account.synchronizationState === 'Synchronized',
    );

    return current
        ? { message: 'connection.current', tone: 'healthy' }
        : { message: 'connection.behind', tone: 'attention' };
}

/** The instant the least recently refreshed account last took mail in, or `null` where none of them ever has. */
function oldestSynchronization(directory: MailAccountDirectory): string | null {
    return directory.accounts.reduce<string | null>((oldest, account) => {
        const at = account.lastSynchronizedAt;

        if (at === null) {
            return oldest;
        }

        return oldest === null || Date.parse(at) < Date.parse(oldest) ? at : oldest;
    }, null);
}

export function ConnectionSummary({ connection }: { readonly connection: Connection }) {
    const { locale, translate } = useLocalization();
    const { session, accounts, readAt, online, attempts, reread } = connection;

    // Nothing is being read and nothing will be until the network comes back, which is a different sentence from a
    // deployment that is not answering: one of them is this machine's to fix and the other is not.
    if (!online) {
        return (
            <Line tone="attention" announced>
                {translate('connection.offline')}
            </Line>
        );
    }

    if (session === null) {
        return (
            <Line tone="quiet" announced>
                {attempts === 0
                    ? translate('connection.connecting')
                    : translate('connection.reconnecting', {
                          attempt: formatCount(locale, attempts),
                          total: formatCount(locale, mostReconnectionAttempts),
                      })}
            </Line>
        );
    }

    if (session.outcome === 'failed') {
        return <Unreachable failure={session.failure.reason} attempts={attempts} reread={reread} />;
    }

    if (!offers(session.value, 'readMail')) {
        return null;
    }

    if (accounts === null || readAt === null) {
        return (
            <Line tone="quiet" announced>
                {translate('accounts.reading')}
            </Line>
        );
    }

    if (accounts.outcome === 'failed') {
        return (
            <Line tone="attention">
                {translate('accounts.failed', { reason: translate(failureLabels[accounts.failure.reason]) })}

                {/* Reading again is the way out of exactly one of the four failures. A refused credential, a missing
                    grant, and an answer this client cannot parse each repeat identically on a second attempt, so
                    offering the button there hands somebody an action that cannot work and says nothing about why.
                    Their next steps — signing in again, saying the grant is missing, reporting a defect — are actions
                    this frame has nowhere to send anybody to yet, and each arrives with the screen that can. */}
                {accounts.failure.reason === 'unavailable' && (
                    <SecondaryButton label={translate('connection.retry')} onActivate={reread} />
                )}
            </Line>
        );
    }

    const directory = accounts.value;
    const freshness = freshnessOf(directory);

    // An owner holding no account is told so and told what would fill it, rather than being handed a disclosure with
    // nothing behind it. Everything else opens onto the account-by-account reading of the same sentence.
    if (directory.accounts.length === 0) {
        return (
            <Line tone={freshness.tone}>
                {translate(freshness.message)} {translate('accounts.noneDeclared')}
            </Line>
        );
    }

    const oldest = ageOf(oldestSynchronization(directory), readAt, locale);

    return (
        <details className="text-sm text-muted">
            <summary className="flex cursor-pointer items-center gap-2">
                <Dot tone={freshness.tone} />
                {translate(freshness.message)}
            </summary>

            <div className="mt-2 flex flex-col gap-2 rounded-lg bg-sunken px-3 py-2">
                {oldest === null ? null : <p>{translate('accounts.oldest', { age: oldest })}</p>}

                <ul className="flex flex-col gap-1">
                    {directory.accounts.map((account) => (
                        <AccountLine key={account.id} account={account} readAt={readAt} />
                    ))}
                </ul>
            </div>
        </details>
    );
}

// A deployment that did not answer is reached for again on its own, so what this says is which of those two moments it
// is: another attempt is coming, or the budget is spent and it is a person's turn to ask. Every other failure of the
// session read repeats identically however often it is asked, so it is named and offers nothing.
function Unreachable({
    failure,
    attempts,
    reread,
}: {
    readonly failure: ClientFailureReason;
    readonly attempts: number;
    readonly reread: () => void;
}) {
    const { locale, translate } = useLocalization();
    const total = formatCount(locale, mostReconnectionAttempts);

    // What could not be read here is the session rather than the accounts, and the sentence says so: an answer this
    // client cannot act on is a different situation from a deployment that would not give up the mailboxes.
    if (failure !== 'unavailable') {
        return (
            <Line tone="attention">
                {translate('connection.unreadable', { reason: translate(failureLabels[failure]) })}
            </Line>
        );
    }

    if (attempts < mostReconnectionAttempts) {
        return (
            <Line tone="attention" announced>
                {translate('connection.reconnecting', { attempt: formatCount(locale, attempts + 1), total })}
            </Line>
        );
    }

    return (
        <Line tone="attention">
            {translate('connection.lost', { total })}
            <SecondaryButton label={translate('connection.retry')} onActivate={reread} />
        </Line>
    );
}

// Small whole numbers, formatted rather than interpolated, for the reason no date is spelled into a catalogue: which
// digits a language writes is `Intl`'s answer rather than JavaScript's default one.
function formatCount(locale: string, count: number): string {
    return new Intl.NumberFormat(locale).format(count);
}

function Dot({ tone }: { readonly tone: Tone }) {
    return <span aria-hidden="true" className={`size-2 shrink-0 rounded-full ${toneColours[tone]}`} />;
}

function Line({
    tone,
    announced = false,
    children,
}: {
    readonly tone: Tone;
    readonly announced?: boolean;
    readonly children: ReactNode;
}) {
    return (
        <p className="flex items-center gap-2 text-sm text-muted" role={announced ? 'status' : undefined}>
            <Dot tone={tone} />
            {children}
        </p>
    );
}

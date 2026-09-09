// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';
import { chip } from '../controls/chrome';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { MessageKey } from '../localization/en';
import type { Locale } from '../localization/locale';
import { useLocalization, type Translate } from '../localization/useLocalization';
import { goToSpace } from '../routing/useSpace';
import {
    askScopeInForce,
    askScopeKey,
    askScopeOnScreen,
    askScopesOffered,
    withAsked,
    type AskedQuestion,
    type AskScope,
} from '../workspace/askScope';
import { folderRoleLabels, type MailScope } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';

// What the product puts in front of the person in every space: the question they are composing, drawn as the design
// project draws the bar under a correspondence — a field with the product's mark on it, and the scope the question
// would be asked under beneath it. It asks nothing here: running a question is Discover's work, so submitting goes to
// the space that will answer and carries the question and its scope there in the workspace rather than starting
// anything.
//
// One field takes every kind of ask — a question, an instruction to search, a request to compare, a request to draft —
// because a mode selector is an admission that the field does not understand what was typed. What it does insist on is
// the scope: every question is asked *against* something, and what is in scope is what is read and sent, so it is
// named before the question is submitted rather than discovered in the answer.
//
// It no longer writes the scope the mail space is read under, which it used to: choosing a mailbox to ask about is not
// choosing a folder to read, and somebody widening a question after too narrow an answer would otherwise lose the
// folder they were in and the messages they had picked out.

export function IntentField({ accounts }: { readonly accounts: readonly MailAccount[] }) {
    const { locale, translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const question = useRef<HTMLInputElement>(null);

    const onScreen = askScopeOnScreen(workspace);
    const inForce = askScopeInForce(workspace, accounts);
    const offered = askScopesOffered(workspace, accounts);

    // The front door is reachable without hunting for it, which is what "from anywhere in the application" costs: a
    // reader inside a list of messages would otherwise tab back out of it to reach the field. An imperative browser
    // API being subscribed to is what an effect is for, and the shortcut is announced on the field itself rather than
    // written anywhere on the screen.
    useEffect(() => {
        function pressed(event: KeyboardEvent): void {
            if (event.key.toLowerCase() === askShortcutKey && (event.ctrlKey || event.metaKey) && !event.altKey) {
                event.preventDefault();
                question.current?.focus();
                question.current?.select();
            }
        }

        document.addEventListener('keydown', pressed);

        return () => {
            document.removeEventListener('keydown', pressed);
        };
    }, []);

    // Submitting records what was asked and what it was asked about, in that one act: the pair is what a run is
    // started from, and a question kept apart from its scope is a run that reads something the person never saw.
    function ask(asked: string): void {
        const words = asked.trim();

        if (words.length > 0) {
            revise({ question: asked, askedBefore: withAsked(workspace.askedBefore, words, inForce) });
        }

        goToSpace('discover');
    }

    return (
        <form
            // The one landmark the platform has no element for, and the one this frame genuinely is: a region a reader
            // moves to in order to ask something, rather than a form that submits a record.
            role="search"
            aria-label={translate('intent.label')}
            className="flex shrink-0 flex-col gap-2.25 border-t border-line bg-panel px-4 py-3.5 workspace:px-5.5"
            onSubmit={(event) => {
                event.preventDefault();
                ask(workspace.question);
            }}
        >
            <div className="flex items-center gap-3 rounded-xl border-2 border-accent px-3.25 py-2">
                <span
                    aria-hidden="true"
                    className="shrink-0 rounded-sm bg-accent px-1.75 py-0.75 text-2xs font-semibold tracking-widest text-on-accent"
                >
                    {translate('ai.badge')}
                </span>

                <input
                    ref={question}
                    type="search"
                    aria-label={translate('intent.label')}
                    aria-keyshortcuts={askShortcut}
                    placeholder={translate('intent.placeholder')}
                    value={workspace.question}
                    onChange={(event) => {
                        revise({ question: event.target.value });
                    }}
                    className="min-w-0 flex-1 bg-transparent text-lg text-text placeholder:text-faint"
                />

                <button
                    type="submit"
                    className="shrink-0 rounded-lg bg-accent px-3 py-1.75 text-base font-semibold text-on-accent shadow-raised transition hover:bg-accent-strong"
                >
                    {translate('intent.ask')}
                </button>
            </div>

            {/* Said as well as drawn: the scope changes because somebody opened a correspondence or ticked a row, which
                is a change a reader who cannot see the chip would otherwise not be told about at all — and this is the
                one place where not being told is a disclosure rather than a missed detail. A passage is said with its
                words for that same reason: naming it as a passage would tell a reader who cannot see the quote beside
                the control that something was narrowed to, and not what. */}
            <p className="sr-only" role="status">
                {inForce.kind === 'fragment'
                    ? translate('scope.fragment', { fragment: inForce.text })
                    : translate('scope.asking', { scope: scopeName(inForce, accounts, translate, locale) })}
            </p>

            <div className="flex flex-wrap items-center gap-2">
                {/* Choosing what the screen is already showing is choosing to keep following it, which is why that
                    option is kept as nothing named rather than pinned: somebody who picks the correspondence they are
                    reading and then opens another one means the one in front of them. */}
                <select
                    aria-label={translate('scope.inScope')}
                    value={askScopeKey(inForce)}
                    onChange={(event) => {
                        revise({
                            askScopeKey: event.target.value === askScopeKey(onScreen) ? null : event.target.value,
                        });
                    }}
                    className={`px-2.75 py-1.25 text-sm ${chip}`}
                >
                    {offered.map((one) => (
                        <option key={askScopeKey(one)} value={askScopeKey(one)}>
                            {scopeName(one, accounts, translate, locale)}
                        </option>
                    ))}
                </select>

                {/* The words themselves, beside the control that named them. The list above can only say that a
                    passage is in scope; a question silently asked about words somebody highlighted minutes ago is a
                    question answered about the wrong thing, so what is quoted here is what will be read. Widening away
                    from it is the control beside this rather than a close button on the chip, because giving the scope
                    back must not take the highlight off the message the reader is still looking at. */}
                {inForce.kind !== 'fragment' ? null : (
                    <span
                        className={`flex min-w-0 max-w-full items-center gap-1.5 border-accent-line bg-accent-soft px-2.75 py-1.25 text-sm text-accent-deep ${chip}`}
                        title={translate('scope.fragment', { fragment: inForce.text })}
                    >
                        <span className="truncate">{translate('scope.fragment', { fragment: inForce.text })}</span>
                    </span>
                )}
            </div>

            {workspace.askedBefore.length === 0 ? null : (
                <AskedBefore
                    asked={workspace.askedBefore}
                    accounts={accounts}
                    onAskAgain={ask}
                    // Forgetting the list takes the list off the screen, and the control that was pressed with it, so
                    // focus is placed rather than left to fall to the document — the same reason the fragment chip's
                    // own close button places it, and the same place: the question, which is what the field is for.
                    onForget={() => {
                        revise({ askedBefore: [] });
                        question.current?.focus();
                    }}
                />
            )}
        </form>
    );
}

// The key the shortcut is pressed with, beside the modifier the platform reads it under. Stated once because the
// handler tests it and `aria-keyshortcuts` announces it, and a screen reader promising a shortcut nothing listens for
// is worse than promising none — which is why the announcement below is built from the key rather than spelled beside
// it, where the two could drift apart without either half looking wrong.
const askShortcutKey = 'k';
const askShortcut = `Control+${askShortcutKey.toUpperCase()} Meta+${askShortcutKey.toUpperCase()}`;

// How many messages are picked out, in the forms a language has for the noun — selected rather than spelled, for the
// reason `mailSpace/SelectionBar.tsx` gives: Polish needs three forms and English hides that it needs two.
const selectionCounted: Readonly<Record<Intl.LDMLPluralRule, MessageKey>> = {
    zero: 'scope.selection.other',
    one: 'scope.selection.one',
    two: 'scope.selection.other',
    few: 'scope.selection.few',
    many: 'scope.selection.many',
    other: 'scope.selection.other',
};

// What a scope is called on the screen, which is the whole of what the field promises: the words beside the question
// are what the question will be asked about.
function scopeName(scope: AskScope, accounts: readonly MailAccount[], translate: Translate, locale: Locale): string {
    switch (scope.kind) {
        case 'fragment':
            return translate('scope.selectedText');
        case 'message':
            return translate('scope.message');
        case 'thread':
            return translate('scope.thread');
        case 'selection':
            return translate(selectionCounted[new Intl.PluralRules(locale).select(scope.messages.length)], {
                count: new Intl.NumberFormat(locale).format(scope.messages.length),
            });
        case 'mail':
            return mailScopeName(scope.scope, accounts, translate);
    }
}

function mailScopeName(scope: MailScope, accounts: readonly MailAccount[], translate: Translate): string {
    switch (scope.kind) {
        case 'everything':
            return translate('scope.allMailboxes');
        case 'role':
            return translate('scope.everyMailbox', { folder: translate(folderRoleLabels[scope.role]) });
        case 'account':
            return mailboxName(scope.accountId, accounts);
        case 'folder':
            return translate('scope.folderIn', {
                folder: scope.alias,
                mailbox: mailboxName(scope.accountId, accounts),
            });
    }
}

// A mailbox is called what the person calls it, and what the deployment assigned it where the declaration has gone
// since the scope was chosen — which beats naming nothing at all on a line whose whole job is to say what is in scope.
function mailboxName(accountId: string, accounts: readonly MailAccount[]): string {
    return accounts.find((account) => account.id === accountId)?.displayName ?? accountId;
}

// What was asked before, offered back so that asking again under a wider scope is one press rather than retyping the
// sentence — which is the commonest thing somebody does after an answer that was too narrow. Each carries the scope it
// was asked under, and pressing it asks under the scope in force now rather than that one, because widening is the
// point. Forgetting them is offered beside them: what somebody asked their own mail is theirs, and a list of it they
// cannot clear is one they did not choose to keep.
function AskedBefore({
    asked,
    accounts,
    onAskAgain,
    onForget,
}: {
    readonly asked: readonly AskedQuestion[];
    readonly accounts: readonly MailAccount[];
    readonly onAskAgain: (question: string) => void;
    readonly onForget: () => void;
}) {
    const { locale, translate } = useLocalization();

    return (
        <div className="flex flex-col gap-1">
            <p className="text-sm text-muted">{translate('intent.askedBefore')}</p>

            <ul aria-label={translate('intent.askedBefore')} className="flex flex-wrap items-center gap-2">
                {asked.map((before) => (
                    <li key={before.question} className="min-w-0 max-w-full">
                        {/* The name is a sentence rather than the two pieces the chip draws: the question and its
                            scope sit in adjacent elements with no whitespace between them, so what a screen reader
                            would otherwise read out is the two run together into one word at the join. */}
                        <button
                            type="button"
                            aria-label={translate('intent.askedUnder', {
                                question: before.question,
                                scope: scopeName(before.scope, accounts, translate, locale),
                            })}
                            title={before.question}
                            className={`flex min-w-0 max-w-full items-center gap-1.5 px-2.75 py-1.25 text-sm ${chip}`}
                            onClick={() => {
                                onAskAgain(before.question);
                            }}
                        >
                            <span className="truncate">{before.question}</span>
                            <span className="shrink-0 text-faint">
                                {scopeName(before.scope, accounts, translate, locale)}
                            </span>
                        </button>
                    </li>
                ))}

                <li>
                    <SecondaryButton label={translate('intent.forgetAsked')} onActivate={onForget} />
                </li>
            </ul>
        </div>
    );
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';
import { chip } from '../controls/chrome';
import { Icon } from '../controls/Icon';
import { useLocalization } from '../localization/useLocalization';
import { goToSpace } from '../routing/useSpace';
import { accountInScope, scopeOfAccount } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';

// What the product puts in front of the person in every space: the question they are composing, drawn as the design
// project draws the composer — a field with the product's mark on it, and the scope the question would be asked under
// as chips beneath it. It asks nothing here: running a question is Discover's work, so submitting goes to the space
// that will answer and carries the question there in the workspace rather than starting anything.
//
// It reads and writes the one scope the folder tree writes, rather than holding a mailbox of its own: the two controls
// are two ways to say one thing, and this one is present in every space where the tree is Mail's. Choosing a mailbox
// here scopes to the whole of it, which is what somebody who has not opened a folder means by naming one.

export function IntentField({ accounts }: { readonly accounts: readonly MailAccount[] }) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const question = useRef<HTMLInputElement>(null);

    return (
        <form
            // The one landmark the platform has no element for, and the one this frame genuinely is: a region a reader
            // moves to in order to ask something, rather than a form that submits a record.
            role="search"
            className="flex shrink-0 flex-col gap-2.25 border-t border-line bg-panel px-4 py-3.5 workspace:px-5.5"
            onSubmit={(event) => {
                event.preventDefault();
                goToSpace('discover');
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
                    placeholder={translate('intent.placeholder')}
                    value={workspace.question}
                    onChange={(event) => {
                        revise({ question: event.target.value });
                    }}
                    className="min-w-0 flex-1 bg-transparent text-lg text-text placeholder:text-faint"
                />

                <button
                    type="submit"
                    className="flex shrink-0 items-center gap-1.5 rounded-lg bg-accent px-3 py-1.75 text-base font-semibold text-on-accent shadow-raised transition hover:bg-accent-strong"
                >
                    <Icon name="auto_awesome" className="size-4.5" />
                    {translate('intent.ask')}
                </button>
            </div>

            <div className="flex flex-wrap items-center gap-2">
                <select
                    aria-label={translate('scope.mailbox')}
                    value={accountInScope(workspace.scope) ?? ''}
                    onChange={(event) => {
                        revise({ scope: scopeOfAccount(event.target.value === '' ? null : event.target.value) });
                    }}
                    className={`px-2.75 py-1.25 text-sm ${chip}`}
                >
                    <option value="">{translate('scope.allMailboxes')}</option>
                    {accounts.map((account) => (
                        <option key={account.id} value={account.id}>
                            {account.displayName}
                        </option>
                    ))}
                </select>

                {/* The other half of the scope, and the one a person set by pointing at something rather than by
                    choosing from a list. It is shown rather than assumed, because a question silently narrowed to
                    words somebody selected minutes ago is a question answered about the wrong thing — and it carries
                    the words themselves rather than that a fragment exists, so what the next question is about is
                    readable before it is asked. */}
                {workspace.fragment === null ? null : (
                    <span
                        className={`flex min-w-0 max-w-full items-center gap-1.5 border-accent-line bg-accent-soft px-2.75 py-1.25 text-sm text-accent-deep ${chip}`}
                        title={translate('scope.fragment', { fragment: workspace.fragment })}
                    >
                        <span className="truncate">
                            {translate('scope.fragment', { fragment: workspace.fragment })}
                        </span>

                        {/* Giving the scope back takes this chip off the screen, and the control somebody pressed
                            with it, so focus is placed rather than left to fall to the document: it goes to the
                            question itself, which is what widening the scope was in aid of asking. */}
                        <button
                            type="button"
                            aria-label={translate('scope.wholeMessage')}
                            title={translate('scope.wholeMessage')}
                            className="flex shrink-0 items-center rounded-full transition hover:bg-hover"
                            onClick={() => {
                                revise({ fragment: null });
                                question.current?.focus();
                            }}
                        >
                            <Icon name="close" className="size-3.5" />
                        </button>
                    </span>
                )}
            </div>
        </form>
    );
}

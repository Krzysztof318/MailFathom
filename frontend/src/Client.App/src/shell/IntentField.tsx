// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef } from 'react';
import type { MailAccount } from '@mailfathom/client-backend';
import { SecondaryButton } from '../controls/SecondaryButton';
import { useLocalization } from '../localization/useLocalization';
import { goToSpace } from '../routing/useSpace';
import { accountInScope, scopeOfAccount } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';

// What the product puts at the centre of the application, above whichever space is open. It asks nothing here: running
// a question is Discover's work, so submitting goes to the space that will answer and carries the question there in the
// workspace rather than starting anything. The scope beside it always says what that question would be asked against.
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
            className="flex flex-wrap items-center gap-2 border-b border-line bg-panel px-4 py-3 workspace:px-8"
            onSubmit={(event) => {
                event.preventDefault();
                goToSpace('discover');
            }}
        >
            <input
                ref={question}
                type="search"
                aria-label={translate('intent.label')}
                placeholder={translate('intent.placeholder')}
                value={workspace.question}
                onChange={(event) => {
                    revise({ question: event.target.value });
                }}
                className="min-w-60 flex-1 rounded-lg border-2 border-accent bg-panel px-3 py-2 text-base text-text placeholder:text-faint"
            />

            <select
                aria-label={translate('scope.mailbox')}
                value={accountInScope(workspace.scope) ?? ''}
                onChange={(event) => {
                    revise({ scope: scopeOfAccount(event.target.value === '' ? null : event.target.value) });
                }}
                className="rounded-full border border-line bg-sunken px-3 py-1.5 text-sm text-text-soft"
            >
                <option value="">{translate('scope.allMailboxes')}</option>
                {accounts.map((account) => (
                    <option key={account.id} value={account.id}>
                        {account.displayName}
                    </option>
                ))}
            </select>

            {/* The other half of the scope, and the one a person set by pointing at something rather than by choosing
                from a list. It is shown rather than assumed, because a question silently narrowed to words somebody
                selected minutes ago is a question answered about the wrong thing — and it says the words themselves
                rather than that a fragment exists, so what the next question is about is readable before it is asked. */}
            {workspace.fragment === null ? null : (
                <p className="flex w-full items-center gap-2 text-sm text-muted">
                    <span className="truncate">{translate('scope.fragment', { fragment: workspace.fragment })}</span>
                    {/* Giving the scope back takes this line off the screen, and the control somebody pressed with
                        it, so focus is placed rather than left to fall to the document: it goes to the question
                        itself, which is what widening the scope was in aid of asking. */}
                    <SecondaryButton
                        label={translate('scope.wholeMessage')}
                        onActivate={() => {
                            revise({ fragment: null });
                            question.current?.focus();
                        }}
                    />
                </p>
            )}
        </form>
    );
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useState } from 'react';
import { useLocalization } from '../localization/useLocalization';
import { useDesktopComposition, useTwoPanes, useWideWorkspace } from '../shell/useWideWorkspace';
import { fragmentBeingRead } from '../workspace/askScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { useReplyDrafting } from './replyDrafting';

// The field at the foot of a correspondence, as the design project draws it there and nowhere else: a box with the
// product's mark on it, a press that drafts a reply, and — where the composition has room — a row saying what the draft
// is about and where it stays.
//
// **What is typed here is about this correspondence and nothing else.** It is held by this component rather than by the
// workspace, so it never reaches Discover's question and Discover's never reaches it, and the frame mounts a fresh one
// for each message that is opened: a sentence written under one message is not a sentence about the next.
//
// **The press drafts in place.** A reply is asked for here and drawn inside the thread as a card the reader checks
// before anything else happens to it, so nothing here navigates and nothing here sends. The frame draws this field only
// where the deployment writes replies at all, which is why no press here has anywhere else to fall back to.

export function ThreadAgentField() {
    const { translate } = useLocalization();
    const { workspace } = useWorkspace();
    const drafting = useReplyDrafting();
    const wide = useWideWorkspace();
    const twoPanes = useTwoPanes();
    const desktop = useDesktopComposition();
    const [typed, setTyped] = useState('');

    const passage = fragmentBeingRead(workspace);
    const answering = workspace.selection;
    const busy = drafting?.draft?.body === null && drafting.draft.answering === answering;

    function draftAReply(): void {
        if (drafting === null || answering === null || busy) {
            return;
        }

        drafting.draftReply(answering, typed, passage?.text ?? null);
    }

    return (
        <form
            aria-label={translate('threadAgent.label')}
            className="flex shrink-0 flex-col gap-2 border-t border-line bg-panel px-3 py-2.25 desktop:gap-2.25 desktop:px-5.5 desktop:py-3.5"
            onSubmit={(event) => {
                event.preventDefault();
                draftAReply();
            }}
        >
            {/* The focus treatment stands on the box rather than on the field inside it, for the reason Discover's own
                field gives: a ring drawn round a transparent field inside a bordered box reads as two nested borders
                rather than one field that lit up. */}
            <div className="flex items-center gap-3 rounded-xl border-2 border-accent px-3.25 py-2.5 transition focus-within:ring-3 focus-within:ring-accent-soft">
                <span
                    aria-hidden="true"
                    className="shrink-0 rounded-sm bg-accent px-2 py-1 text-xs tracking-wider text-on-accent"
                >
                    {translate('ai.badge')}
                </span>

                <input
                    ref={drafting?.holdField}
                    type="text"
                    aria-label={translate('threadAgent.label')}
                    placeholder={translate('threadAgent.placeholder')}
                    value={typed}
                    onChange={(event) => {
                        setTyped(event.target.value);
                    }}
                    className="min-w-0 flex-1 bg-transparent text-lg text-text outline-none placeholder:text-faint"
                />

                {/* `aria-disabled` rather than `disabled` while a reply is being written, so the control somebody just
                    pressed keeps their focus; the handler is what refuses the second press. */}
                <button
                    type="submit"
                    aria-disabled={busy}
                    className={`flex shrink-0 items-center justify-center bg-accent whitespace-nowrap text-on-accent transition hover:bg-accent-strong aria-disabled:opacity-60 ${
                        wide ? 'rounded-md px-3.75 py-2 text-base' : 'min-h-12 rounded-full px-5 text-md font-semibold'
                    }`}
                >
                    {translate(twoPanes ? 'threadAgent.draftReply' : 'threadAgent.draftReplyShort')}
                </button>
            </div>

            {/* Said as well as drawn: a passage somebody selected is part of what the reply is written about, and a
                reader who cannot see the chip below — or a composition that draws none — would otherwise not be told
                that it is. */}
            <p className="sr-only" role="status">
                {passage === null ? '' : translate('scope.fragment', { fragment: passage.text })}
            </p>

            {desktop ? (
                <div className="flex flex-wrap items-center gap-2">
                    <span className="rounded-full bg-accent-soft px-2.75 py-1.25 text-sm text-accent-deep">
                        {translate('threadAgent.scope')}
                    </span>

                    {/* The passage is not chosen here — it is whatever the reader selected in the message above — so the
                        chip is a statement rather than a control, and it reads as taken in once there is one. */}
                    <span
                        title={passage?.text}
                        className={`rounded-full px-2.75 py-1.25 text-sm ${
                            passage === null ? 'border border-line bg-rail' : 'bg-accent-soft text-accent-deep'
                        }`}
                    >
                        {translate('threadAgent.passage')}
                    </span>

                    <span className="ms-auto text-sm text-muted">{translate('threadAgent.staysLocal')}</span>
                </div>
            ) : null}
        </form>
    );
}

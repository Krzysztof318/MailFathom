// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useComposing } from '../composer/useComposing';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { useWorkspace } from '../workspace/useWorkspace';
import { useReplyDrafting, type ReplyDrafting, type ThreadDraft } from './replyDrafting';

// The reply the field at the foot of a correspondence asked for, drawn at the end of the thread as the design draws it:
// a card marked as a local draft, saying nothing has been sent, the words, and the two ways on — into the composer, where
// it becomes something that can be sent, or away.
//
// It is drawn by whichever surface is reading the message the reply answers, and only there: a draft written for one
// message does not stand under the next one somebody opens, and it is back if they return to the first.
//
// *Discard draft* asks nothing first, which the design draws and which is not the composer's discard: these are words
// the deployment wrote a moment ago rather than words anybody wrote, and one press on the field writes them again.

export function ReplyDraftCard() {
    const { workspace } = useWorkspace();
    const drafting = useReplyDrafting();
    const draft = drafting?.draft ?? null;

    return drafting === null || draft?.answering !== workspace.selection ? null : (
        <DraftCard drafting={drafting} draft={draft} />
    );
}

function DraftCard({ drafting, draft }: { readonly drafting: ReplyDrafting; readonly draft: ThreadDraft }) {
    const { translate } = useLocalization();
    const composing = useComposing();
    const wide = useWideWorkspace();
    const body = draft.body;

    return (
        <section
            aria-label={translate('threadAgent.draft')}
            className="flex flex-col gap-2 rounded-lg border border-l-3 border-line border-l-accent bg-panel px-4 py-3.5"
        >
            <div className="flex items-center gap-2.5">
                <p className="text-xs tracking-widest text-muted uppercase">{translate('threadAgent.draft')}</p>

                {wide ? <p className="ms-auto text-sm text-muted">{translate('threadAgent.nothingSent')}</p> : null}
            </div>

            {/* One region whose words change, so a reader who pressed the field and is waiting hears the draft arrive
                rather than finding it later by chance. */}
            <div aria-live="polite">
                {body === null ? (
                    <p className="text-lg text-muted">{translate('compose.aiDrafting')}</p>
                ) : (
                    <p className="text-lg whitespace-pre-wrap text-pretty">{body}</p>
                )}
            </div>

            {body === null ? null : (
                <div className="flex flex-wrap gap-2.25">
                    {composing.offered ? (
                        <button
                            type="button"
                            className="rounded-md bg-accent px-3.75 py-2 text-base text-on-accent transition hover:bg-accent-strong"
                            onClick={() => {
                                drafting.letGo();
                                composing.compose({
                                    kind: 'answer',
                                    answers: 'senderOnly',
                                    storedEmailId: draft.answering,
                                    drafted: body,
                                });
                            }}
                        >
                            {translate('threadAgent.openInComposer')}
                        </button>
                    ) : null}

                    {/* The card goes with the press, so focus is put back on the field it was asked for in rather than
                        left on a control that no longer exists. */}
                    <button
                        type="button"
                        className="rounded-md border border-line-strong px-3.75 py-2 text-base text-text-soft transition hover:bg-rail"
                        onClick={() => {
                            drafting.letGo();
                            drafting.returnToField();
                        }}
                    >
                        {translate('threadAgent.discard')}
                    </button>
                </div>
            )}
        </section>
    );
}

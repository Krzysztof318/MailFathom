// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState, type ReactNode } from 'react';
import { Icon } from '../controls/Icon';
import { PlannedControl } from '../controls/PlannedControl';
import { useLocalization } from '../localization/useLocalization';
import { useWideWorkspace } from '../shell/useWideWorkspace';
import { scopeKey } from '../workspace/mailScope';
import { useWorkspace } from '../workspace/useWorkspace';
import { AiFilters } from './AiFilters';
import { MailToolbar } from './MailToolbar';

// The Mail space as the design project composes it, out of the three regions a mail client is: the mailboxes, the list
// of what is in the one that is scoped, and what is open from it. What this component owns is the composition alone —
// each region is handed in already built — and the composition is two shapes out of one tree, decided by the width the
// space has been given rather than by which head it runs on.
//
// Wide, it is the toolbar over three columns: a mailbox column that folds to a strip, the list at the width the design
// gives it, and the reading pane taking the rest. Narrow, it is one column at a time: the list first, with the mailboxes
// behind a control and the message in front of the list once one is open, and a way back from each. Nothing is hidden
// by the width alone; what the narrow shape cannot show at once is reached rather than dropped.
//
// Where focus goes when the narrow shape changes what it shows is this component's too: a message coming in front of
// the list is a view change, and so is going back, and a reader on a keyboard is put at the start of what replaced what
// they were on rather than left on an element that is no longer there. A resize that changes the shape is not, so the
// width changing moves nothing.

export function MailSpace({
    folders,
    list,
    mail,
    intent,
    status,
}: {
    /** The scope selector, which is the mailbox column's first thing. */
    readonly folders: ReactNode;

    /** The mail in scope, searchable, which is the middle column. */
    readonly list: ReactNode;

    /** What is open, which is the reading column. */
    readonly mail: ReactNode;

    /**
     * The question the reader is composing, which stands at the foot of the reading column — and, in the narrow shape,
     * at the foot of whichever column is on the screen, so that it is never behind a message somebody has to open.
     */
    readonly intent: ReactNode;

    /** What the deployment says about the connection, which stands at the foot of the mailbox column. */
    readonly status: ReactNode;
}) {
    const { translate } = useLocalization();
    const { workspace, revise } = useWorkspace();
    const wide = useWideWorkspace();
    const [folded, setFolded] = useState(false);
    const drawer = useRef<HTMLDialogElement>(null);
    const listColumn = useRef<HTMLElement>(null);
    const readingColumn = useRef<HTMLElement>(null);

    const readingInFront = !wide && (workspace.selection !== null || workspace.conversation !== null);

    // Focus is placed on the shape changing what it shows, and not on the width changing the shape: both are recorded
    // and only the first moves anything. A ref rather than state, because what it holds is what was last drawn.
    const shown = useRef({ wide, readingInFront });

    useEffect(() => {
        const before = shown.current;
        shown.current = { wide, readingInFront };

        if (before.wide === wide && before.readingInFront !== readingInFront) {
            (readingInFront ? readingColumn : listColumn).current?.focus();
        }
    }, [wide, readingInFront]);

    // The drawer is a way to point at a mailbox, so pointing at one closes it: a reader who chose a folder wants the
    // folder's mail, which is behind the drawer they chose it in. The dialog is the platform's own, so closing it puts
    // focus back on the control that opened it without anything here remembering which control that was.
    const scope = scopeKey(workspace.scope);
    const shownScope = useRef(scope);

    useEffect(() => {
        if (shownScope.current !== scope) {
            shownScope.current = scope;
            drawer.current?.close();
        }
    }, [scope]);

    function goBackToList(): void {
        revise({ selection: null, conversation: null });
    }

    return (
        <div className="flex min-h-0 flex-1 flex-col">
            {wide ? <MailToolbar /> : null}

            <div className="flex min-h-0 flex-1">
                {wide ? (
                    <aside
                        aria-label={translate('mailboxes.open')}
                        className={`flex shrink-0 flex-col border-e border-line bg-sunken ${folded ? 'w-mailboxes-folded' : 'w-mailboxes'}`}
                    >
                        <Mailboxes
                            folders={folders}
                            status={status}
                            folded={folded}
                            control={
                                <ColumnControl
                                    label={translate(folded ? 'mailboxes.unfold' : 'mailboxes.fold')}
                                    icon={folded ? 'chevron_right' : 'chevron_left'}
                                    onActivate={() => {
                                        setFolded(!folded);
                                    }}
                                />
                            }
                        />
                    </aside>
                ) : (
                    <dialog
                        ref={drawer}
                        aria-label={translate('mailboxes.open')}
                        className="fixed inset-y-0 left-0 m-0 h-full max-h-none w-drawer max-w-full border-0 border-e border-line bg-sunken p-0 text-text shadow-overlay backdrop:bg-scrim"
                    >
                        <div className="flex h-full flex-col">
                            <Mailboxes
                                folders={folders}
                                status={status}
                                folded={false}
                                control={
                                    <ColumnControl
                                        label={translate('mailboxes.close')}
                                        icon="close"
                                        onActivate={() => {
                                            drawer.current?.close();
                                        }}
                                    />
                                }
                            />
                        </div>
                    </dialog>
                )}

                {wide || !readingInFront ? (
                    <section
                        ref={listColumn}
                        tabIndex={-1}
                        aria-label={translate('mail.listColumn')}
                        className={`flex min-h-0 min-w-0 flex-col bg-panel ${wide ? 'w-message-list shrink-0 border-e border-line' : 'flex-1'}`}
                    >
                        {wide ? null : (
                            <div className="flex items-center px-2 pt-2">
                                <ColumnControl
                                    label={translate('mailboxes.open')}
                                    icon="menu"
                                    onActivate={() => {
                                        drawer.current?.showModal();
                                    }}
                                />
                            </div>
                        )}

                        {list}

                        {wide ? null : intent}
                    </section>
                ) : null}

                {wide || readingInFront ? (
                    <section
                        ref={readingColumn}
                        tabIndex={-1}
                        aria-label={translate('mail.readingColumn')}
                        className="flex min-h-0 min-w-0 flex-1 flex-col bg-panel"
                    >
                        {wide ? null : (
                            <div className="flex items-center px-2 pt-2">
                                <button
                                    type="button"
                                    className="flex items-center gap-1.5 rounded-lg px-2 py-1.5 text-base text-text-soft transition hover:bg-hover"
                                    onClick={goBackToList}
                                >
                                    <Icon name="arrow_back" className="size-5" />
                                    {translate('mail.backToList')}
                                </button>
                            </div>
                        )}

                        {/* The reading column keeps the width the design draws it at and stops there, so a window
                            wider than the design's adds margin rather than line length. The ceiling is the one thing a
                            mail client cannot leave to the viewport: a paragraph running 1900 pixels is past the
                            measure at which the eye finds the start of the next line. It binds above that width and
                            nowhere else, so the narrow shape and the workspace shape are untouched by it. */}
                        <div className="min-h-0 flex-1 overflow-y-auto">
                            <div className="mx-auto flex min-h-full w-full max-w-reading flex-col">{mail}</div>
                        </div>

                        {/* The question stands under the message rather than under the pane, so the column reads as
                            one width from the sender's name down to the field the reader asks in. */}
                        <div className="mx-auto w-full max-w-reading">{intent}</div>
                    </section>
                ) : null}
            </div>

            {/* Composing stands on the list in the narrow shape, where the toolbar carrying it is not drawn: the one
                thing a phone-width reader reaches for from the list, at the corner a thumb reaches. */}
            {wide || readingInFront ? null : (
                <PlannedControl
                    label={translate('mail.compose')}
                    icon="edit_square"
                    shape="floating"
                    className="fixed right-4.5 bottom-22"
                />
            )}
        </div>
    );
}

// The mailbox column's contents, drawn once for the two places they stand: the column beside the list, and the drawer
// in front of it. The heading, the tree, the filters MailFathom's own reading will add, and the connection at the foot.
function Mailboxes({
    folders,
    status,
    folded,
    control,
}: {
    readonly folders: ReactNode;
    readonly status: ReactNode;
    readonly folded: boolean;
    readonly control: ReactNode;
}) {
    const { translate } = useLocalization();

    return (
        <>
            <div className={`flex items-center px-2.25 pt-3 pb-1.5 ${folded ? 'justify-center' : 'justify-between'}`}>
                {folded ? null : (
                    <p className="ps-1.5 text-xs tracking-widest text-muted uppercase">
                        {translate('mailboxes.heading')}
                    </p>
                )}

                {control}
            </div>

            {folded ? null : (
                <>
                    <div className="flex min-h-0 flex-1 flex-col overflow-y-auto px-2.25 py-1">
                        {folders}
                        <AiFilters />
                    </div>

                    <div className="border-t border-line px-3.5 py-2.5">{status}</div>
                </>
            )}
        </>
    );
}

// The one control shape the columns draw: a symbol that folds, opens, or closes a column, named by what it does.
function ColumnControl({
    label,
    icon,
    onActivate,
}: {
    readonly label: string;
    readonly icon: 'chevron_left' | 'chevron_right' | 'close' | 'menu';
    readonly onActivate: () => void;
}) {
    return (
        <button
            type="button"
            aria-label={label}
            title={label}
            className="flex size-8 shrink-0 items-center justify-center rounded-md text-muted transition hover:bg-hover hover:text-text"
            onClick={onActivate}
        >
            <Icon name={icon} className="size-5" />
        </button>
    );
}

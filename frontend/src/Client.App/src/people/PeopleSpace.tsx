// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useRef, useState } from 'react';
import {
    eraseContact,
    promoteContact,
    recordContact,
    type ClientSession,
    type Contact,
    type ContactWriteOutcome,
    type MailFathomTransport,
} from '@mailfathom/client-backend';
import { Confirmation } from '../confirmation/Confirmation';
import { ContactBook } from '../contactBook/ContactBook';
import { ContactSelectionBar } from '../contactBook/ContactSelectionBar';
import { useContactBook, type ContactBookName } from '../contactBook/useContactBook';
import { Control } from '../controls/Control';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTwoPanes, useWideWorkspace } from '../shell/useWideWorkspace';
import { NewContact, type ContactDraft } from './NewContact';
import { PersonPage } from './PersonPage';
import { useContactCorrespondence } from './useContactCorrespondence';

// The People space as the design project composes it: the address book down one side and one person's own page beside
// it, with the acts that change the book held here rather than in either.
//
// **The book and the person are one pane below the width the mail screens collapse at**, and the same width decides
// both — a person opened there comes in front of the list and carries the way back, which is why `PersonPage` draws
// that control only when this hands it one.
//
// **Every write is answered by reading the book again rather than by correcting what is held.** Where somebody sorts in
// the book is the deployment's, and a promotion moves them between the two books entirely, so a list edited in place
// would be this client's own opinion of an ordering it does not own. What a write leaves behind instead is one
// sentence saying what happened, beside the thing it happened to.
//
// **What the toolbar carries is one act, and that is the whole of what this deployment publishes a route for.** The
// design draws five more beside it — importing a book, exporting one, merging two records, and two ways of filtering
// what is listed — and `/api/client` answers none of them, so each is left out rather than drawn as a control that
// would do nothing. That is a correction owed to the design rather than a toolbar to fill.

// What one write to the book said, worded as what happened to the person rather than as an outcome name. Exhaustive by
// its own type, so an outcome added to the surface does not compile until this says what a reader is told about it.
const writeSaid: Readonly<Record<ContactWriteOutcome, MessageKey>> = {
    Written: 'people.written',
    NotFound: 'people.writeNotFound',
    AddressHeldByAnotherContact: 'people.addressHeld',
    OriginRefusesWriter: 'people.originRefuses',
    AlreadyAsserted: 'people.alreadyAsserted',
};

// Where a sentence about a write belongs: over the book for a write the book as a whole answered, and on the person's
// own page for one about the person who is open. One state rather than two, because only one write is in flight at a
// time and two would eventually disagree about which was the last.
interface WriteSaid {
    readonly where: 'book' | 'person';
    readonly said: MessageKey;
}

export function PeopleSpace({
    session,
    transport,
    writable,
    onOpenThread,
    onOpenDocument,
}: {
    /** Who is asking and where, or `null` where there is nothing to ask with. */
    readonly session: ClientSession | null;

    readonly transport: MailFathomTransport;

    /** Whether this credential may change the book at all, which every act below is behind. */
    readonly writable: boolean;

    /** Opens one conversation, which is the Mail space's to draw and therefore the frame's to perform. */
    readonly onOpenThread: (thread: {
        readonly threadId: string;
        readonly messageId: string;
        readonly subject: string | null;
    }) => void;

    /** Opens one file a person sent, named as which file of which message. */
    readonly onOpenDocument: (document: { readonly messageId: string; readonly position: number }) => void;
}) {
    const { translate } = useLocalization();
    const twoPanes = useTwoPanes();
    const wide = useWideWorkspace();

    const [book, setBook] = useState<ContactBookName>('own');
    const [opened, setOpened] = useState<Contact | null>(null);
    const [selected, setSelected] = useState<readonly string[]>([]);
    const [erasing, setErasing] = useState<readonly Contact[]>([]);
    const [said, setSaid] = useState<WriteSaid | null>(null);
    const [promoting, setPromoting] = useState(false);

    const asked = useRef<HTMLDialogElement | null>(null);
    const asking = useRef<HTMLDialogElement | null>(null);

    const reading = useContactBook(session, transport, book);
    const correspondence = useContactCorrespondence(session, transport, opened?.id ?? null);

    // The people picked out, read back out of the book rather than held as records: the list holds identities and the
    // book holds what each of them is, so a selection cannot outlive a read that no longer names one of them.
    const picked = reading.contacts.filter((contact) => selected.includes(contact.id));

    function record(draft: ContactDraft): void {
        if (session === null) {
            return;
        }

        void recordContact(session, transport, {
            displayName: draft.displayName,
            addresses: [draft.address],
            preferredAddress: draft.address,
            note: null,
        }).then((answer) => {
            if (answer.outcome === 'failed') {
                setSaid({ where: 'book', said: 'people.writeFailed' });

                return;
            }

            setSaid({ where: 'book', said: writeSaid[answer.value.outcome] });

            if (answer.value.outcome === 'Written') {
                // Written into the book somebody asserted, which is the book this then shows: a person who has just
                // written somebody down is owed the row rather than the tab they happened to be on.
                setBook('own');
                reading.readAgain();
            }
        });
    }

    function promote(): void {
        if (session === null || opened === null) {
            return;
        }

        setPromoting(true);
        setSaid(null);

        void promoteContact(session, transport, opened.id).then((answer) => {
            setPromoting(false);

            if (answer.outcome === 'failed') {
                setSaid({ where: 'person', said: 'people.writeFailed' });

                return;
            }

            setSaid({ where: 'person', said: writeSaid[answer.value.outcome] });

            if (answer.value.contact !== null) {
                setOpened(answer.value.contact);
                setBook('own');
            }

            reading.readAgain();
        });
    }

    function erase(contacts: readonly Contact[]): void {
        if (session === null) {
            return;
        }

        const erased = contacts.map((contact) => contact.id);

        void Promise.all(erased.map((contact) => eraseContact(session, transport, contact))).then((answers) => {
            setSaid({
                where: 'book',
                said: answers.some((answer) => answer.outcome === 'failed') ? 'people.writeFailed' : 'people.erased',
            });

            setSelected((standing) => standing.filter((contact) => !erased.includes(contact)));
            setOpened((standing) => (standing !== null && erased.includes(standing.id) ? null : standing));
            reading.readAgain();
        });
    }

    function askErasure(contacts: readonly Contact[]): void {
        if (contacts.length === 0) {
            return;
        }

        setErasing(contacts);
        asked.current?.showModal();
    }

    const personInFront = !twoPanes && opened !== null;

    return (
        <div className="relative flex min-h-0 flex-1 flex-col">
            {selected.length > 0 ? (
                <ContactSelectionBar
                    selected={picked}
                    erasable={writable}
                    onClear={() => {
                        setSelected([]);
                    }}
                    onAskErasure={() => {
                        askErasure(picked);
                    }}
                />
            ) : wide ? (
                <div className="flex shrink-0 items-center gap-2.25 border-b border-line bg-panel px-3 py-2">
                    {writable ? (
                        <Control
                            label={translate('people.newContact')}
                            icon="add"
                            shape="primary"
                            onPress={() => {
                                setSaid(null);
                                asking.current?.showModal();
                            }}
                        />
                    ) : null}
                </div>
            ) : null}

            {said?.where === 'book' ? (
                <p role="status" className="shrink-0 border-b border-line bg-sunken px-4 py-2 text-sm text-muted">
                    {translate(said.said)}
                </p>
            ) : null}

            <div className="relative flex min-h-0 flex-1 flex-col panes:flex-row">
                {/* The book stands aside rather than coming down where one pane draws the person, for the reason
                    `shell/Space.tsx` gives about a space: what it holds is the pages the deployment answered and where
                    in them the reader had scrolled to, and the platform keeps a scroller's offset only while its box
                    is there. How wide the column is belongs to this composition rather than to the list, which is why
                    the width and the boundary are written here and not inside it. */}
                <div
                    aria-hidden={personInFront ? true : undefined}
                    inert={personInFront}
                    className={`flex min-h-0 min-w-0 flex-col panes:border-e panes:border-line ${
                        personInFront ? 'invisible absolute inset-0' : 'flex-1 panes:w-contact-list panes:flex-none'
                    }`}
                >
                    <ContactBook
                        book={book}
                        reading={reading}
                        opened={opened?.id ?? null}
                        selected={selected}
                        erasable={writable}
                        onBook={(chosen) => {
                            setBook(chosen);
                            setSelected([]);
                        }}
                        onOpen={(contact) => {
                            setSaid(null);
                            setOpened(contact);
                        }}
                        onSelected={setSelected}
                        onAskErasure={askErasure}
                    />
                </div>

                {opened === null ? (
                    // Beside an empty pane rather than instead of one, so the book does not move under a reader the
                    // moment they open somebody. The narrow shape has no second pane to leave empty.
                    twoPanes ? (
                        <div className="flex min-h-0 flex-1 flex-col items-center justify-center gap-2 px-6 py-8 text-center">
                            <p className="text-base text-muted text-pretty">{translate('people.nobodyOpen')}</p>
                        </div>
                    ) : null
                ) : (
                    <PersonPage
                        contact={opened}
                        correspondence={correspondence}
                        writable={writable}
                        promoting={promoting}
                        promotionSaid={said?.where === 'person' ? said.said : null}
                        onBack={
                            twoPanes
                                ? null
                                : () => {
                                      setOpened(null);
                                  }
                        }
                        onPromote={promote}
                        onAskErasure={() => {
                            askErasure([opened]);
                        }}
                        onOpenThread={onOpenThread}
                        onOpenDocument={onOpenDocument}
                    />
                )}
            </div>

            {/* The narrow composition's own way to write somebody down, which is where a thumb reaches rather than at
                the top of a column: the toolbar the wide shape carries has nowhere to stand once bottom navigation has
                the foot of the window. */}
            {!wide && writable && selected.length === 0 && !personInFront ? (
                <Control
                    label={translate('people.newContact')}
                    icon="person_add"
                    shape="floating"
                    className="absolute end-4 bottom-4"
                    onPress={() => {
                        setSaid(null);
                        asking.current?.showModal();
                    }}
                />
            ) : null}

            {writable ? <NewContact asked={asking} onSave={record} /> : null}

            <Confirmation
                asked={asked}
                mark="delete"
                question={translate(erasing.length === 1 ? 'people.eraseOne' : 'people.eraseMany', {
                    count: erasing.length.toFixed(0),
                })}
                consequence={
                    <>
                        {erasing.map((contact) => (
                            <span key={contact.id} className="block truncate">
                                {contact.displayName}
                            </span>
                        ))}
                    </>
                }
                reversal={{ kind: 'permanent', said: translate('people.erasePermanent') }}
                ways={[
                    { said: translate('act.cancel'), manner: 'back' },
                    {
                        said: translate('people.eraseAct'),
                        manner: 'destroy',
                        run: () => {
                            erase(erasing);
                        },
                    },
                ]}
            />
        </div>
    );
}

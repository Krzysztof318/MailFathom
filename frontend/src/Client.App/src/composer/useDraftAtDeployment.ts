// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useEffect, useRef, useState } from 'react';
import {
    discardMailDraft,
    reviseMailDraft,
    sendMailDraft,
    stageMailDraftAttachment,
    unstageMailDraftAttachment,
    withdrawOutgoingMail,
    writeMailDraft,
    type ClientFailureReason,
    type ClientResult,
    type ClientSession,
    type MailFathomTransport,
    type MailSendRefusal,
    type MailSendWithdrawal,
    type MailStagedAttachment,
} from '@mailfathom/client-backend';
import { useAttachmentUpload } from '../deployment/attachmentUpload';
import { wireComposition, type Composition } from './composition';

// What the deployment holds for the message being written, and the five acts that change it. It is a hook rather than
// state inside the composer because none of it is what is on the screen: the draft in the owner's own drafts folder,
// the files staged against it, and what became of a send are all the deployment's, and each act is a sequence with an
// outcome rather than a value to render.
//
// **Attaching and sending both need a draft the deployment holds**, because a file is staged against one and a send is
// queued from one. So each saves first where nothing has been saved yet, which is what makes the design's two controls
// work without a third that says "save before attaching" — and it is still a save the person asked for, because
// attaching and sending are both acts they asked for.

/** What the composer is doing about the deployment, which is one piece of state rather than a set of flags. */
export type DraftStanding =
    | { readonly kind: 'held' }
    | { readonly kind: 'saving' }
    | { readonly kind: 'saved' }
    | { readonly kind: 'attaching'; readonly fileName: string }
    | { readonly kind: 'sending' }
    | { readonly kind: 'queued'; readonly outgoingEmailId: string }
    | { readonly kind: 'withdrawn'; readonly withdrawal: MailSendWithdrawal }
    | { readonly kind: 'refused'; readonly refusal: MailSendRefusal }
    | { readonly kind: 'failed'; readonly reason: ClientFailureReason };

/** The draft the deployment holds for what is being written, and what a person does to it. */
export interface DraftAtDeployment {
    readonly standing: DraftStanding;

    /** The files staged against it, oldest first, which is nothing until one is attached. */
    readonly staged: readonly MailStagedAttachment[];

    /** Files the draft in the owner's own drafts folder, creating it where nothing has been saved yet. */
    readonly save: (composition: Composition) => Promise<boolean>;

    /** Stages one file against the draft, saving it first where nothing has been saved yet. */
    readonly attach: (composition: Composition, file: File) => Promise<void>;

    /** Takes one staged file back off. */
    readonly unstage: (attachmentId: string) => Promise<void>;

    /** Queues the message, saving what has been written since first. */
    readonly send: (composition: Composition) => Promise<void>;

    /** Takes a queued send back while it has not begun transmitting. */
    readonly withdraw: () => Promise<void>;

    /** Gives the draft up, taking its copies back out of the owner's drafts folder. */
    readonly discard: () => Promise<boolean>;
}

export function useDraftAtDeployment(session: ClientSession, transport: MailFathomTransport): DraftAtDeployment {
    const upload = useAttachmentUpload();
    const [standing, setStanding] = useState<DraftStanding>({ kind: 'held' });
    const [staged, setStaged] = useState<readonly MailStagedAttachment[]>([]);

    // The draft the deployment holds, as a ref rather than as state: two acts in the same turn have to see the
    // identifier the first of them wrote, and nothing on the screen is drawn from it.
    const draftId = useRef<string | null>(null);

    // The save in flight, if any. `saved` reads `draftId.current` and only writes it back once its own request has
    // answered, so two acts starting inside that window would each write a draft and strand whatever the loser staged
    // against it. A second caller joins the first rather than starting a second write.
    const saving = useRef<Promise<string | null> | null>(null);
    const queued = useRef<string | null>(null);
    const uploading = useRef<AbortController | null>(null);

    // An upload whose composer has gone is an upload nobody is waiting for, and letting it finish would stage a file
    // against a draft the author has closed.
    useEffect(
        () => () => {
            uploading.current?.abort();
        },
        [],
    );

    function saved(composition: Composition): Promise<string | null> {
        const already = saving.current;

        if (already !== null) {
            return already;
        }

        const writing = write(composition);

        saving.current = writing;

        return writing.finally(() => {
            saving.current = null;
        });
    }

    async function write(composition: Composition): Promise<string | null> {
        const held = draftId.current;
        const wire = wireComposition(composition);

        const answer =
            held === null
                ? await writeMailDraft(session, transport, wire)
                : await reviseMailDraft(session, transport, held, wire);

        if (answer.outcome === 'failed') {
            setStanding({ kind: 'failed', reason: answer.failure.reason });

            return null;
        }

        draftId.current = answer.value.draftId;
        setStaged(answer.value.attachments);

        return answer.value.draftId;
    }

    // Every act ends by saying what happened, and a failure says which of the four it was rather than that something
    // went wrong. Stated once here because five acts would otherwise each carry their own copy of the same three lines.
    function settled<TValue>(answer: ClientResult<TValue>, whenRead: (value: TValue) => DraftStanding): void {
        setStanding(
            answer.outcome === 'failed' ? { kind: 'failed', reason: answer.failure.reason } : whenRead(answer.value),
        );
    }

    return {
        standing,
        staged,

        save: async (composition) => {
            setStanding({ kind: 'saving' });

            if ((await saved(composition)) === null) {
                return false;
            }

            setStanding({ kind: 'saved' });

            return true;
        },

        attach: async (composition, file) => {
            setStanding({ kind: 'attaching', fileName: file.name });

            const held = await saved(composition);

            if (held === null) {
                return;
            }

            const abandoning = new AbortController();
            uploading.current = abandoning;

            const answer = await stageMailDraftAttachment(
                session,
                held,
                file.name,
                // What the file declares itself to be, which is what the author's own system said. A file the system
                // could not name is the general binary type, which is what the deployment reads a request declaring
                // none as anyway.
                file.type === '' ? 'application/octet-stream' : file.type,
                (request) => upload(request, file, abandoning.signal),
            );

            uploading.current = null;

            settled(answer, (attachment) => {
                setStaged((already) => [...already, attachment]);

                return { kind: 'held' };
            });
        },

        unstage: async (attachmentId) => {
            const held = draftId.current;

            if (held === null) {
                return;
            }

            settled(await unstageMailDraftAttachment(session, transport, held, attachmentId), () => {
                setStaged((already) => already.filter((file) => file.attachmentId !== attachmentId));

                return { kind: 'held' };
            });
        },

        send: async (composition) => {
            setStanding({ kind: 'sending' });

            const held = await saved(composition);

            if (held === null) {
                return;
            }

            settled(await sendMailDraft(session, transport, held), (outcome) => {
                if (!outcome.queued) {
                    return { kind: 'refused', refusal: outcome.refusal };
                }

                queued.current = outcome.outgoingEmailId;

                return { kind: 'queued', outgoingEmailId: outcome.outgoingEmailId };
            });
        },

        withdraw: async () => {
            const sent = queued.current;

            if (sent === null) {
                return;
            }

            settled(await withdrawOutgoingMail(session, transport, sent), (withdrawal) => ({
                kind: 'withdrawn',
                withdrawal,
            }));
        },

        discard: async () => {
            // Whatever save is in flight first, so a draft written a moment ago is one this knows about rather than
            // one it leaves behind because the identifier had not landed yet.
            await saving.current;

            const held = draftId.current;

            if (held === null) {
                return true;
            }

            const answer = await discardMailDraft(session, transport, held);

            if (answer.outcome === 'failed') {
                // Said rather than swallowed: closing on a refused delete would tell somebody their words are gone
                // while the deployment is still holding them, with nothing on this screen to go back to.
                setStanding({ kind: 'failed', reason: answer.failure.reason });

                return false;
            }

            draftId.current = null;

            return true;
        },
    };
}

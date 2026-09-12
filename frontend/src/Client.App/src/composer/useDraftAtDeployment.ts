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
} from '@mailfathom/client-backend';
import { useAttachmentUpload } from '../deployment/attachmentUpload';
import { useTelemetry } from '../telemetry/clientTelemetry';
import { wireComposition, type Composition } from './composition';

// What the deployment holds for the message being written, and the five acts that change it. It is a hook rather than
// state inside the composer because none of it is what is on the screen: the draft in the user's own drafts folder,
// the files staged against it, and what became of a send are all the deployment's, and each act is a sequence with an
// outcome rather than a value to render.
//
// **Two acts file the message and no others do**: saving it, and sending it. Each writes the draft the deployment
// holds and then puts up whatever files the author chose that have not reached it yet. Choosing a file is not one of
// them — a file somebody picked is part of what they are writing, exactly as the words are, so it is held here until
// one of those two acts says to file the message. That is what keeps attaching offered on a deployment nobody has
// needed yet, and it is what leaves nothing behind for closing the composer to have to delete.

// Which act asked for the write, which is what the refusal it meets is worded as. Only two words exist because only
// two are true of the person: they pressed save, or they pressed send.
type DraftAct = 'save' | 'send';

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
    | { readonly kind: 'refusedSave'; readonly refusal: MailSendRefusal }
    | { readonly kind: 'failed'; readonly reason: ClientFailureReason };

/**
 * One file the message carries, as the composer draws it.
 *
 * It says nothing about whether the deployment has it yet, because nothing on the screen says that either: a file is
 * part of the message from the moment it was chosen, and where it currently sits is this hook's business.
 */
export interface AttachedFile {
    /** This composer's own name for it, which never changes once the file is chosen. */
    readonly attachmentId: string;

    readonly fileName: string;
    readonly sizeOctets: number;
}

/** One chosen file as this hook holds it: the file itself, and what the deployment calls it once it has it. */
interface HeldFile {
    readonly attachmentId: string;
    readonly file: File;

    /** What the deployment named it when it was staged, or `null` while the deployment does not have it. */
    readonly stagedAs: string | null;
}

/** The draft the deployment holds for what is being written, and what a person does to it. */
export interface DraftAtDeployment {
    readonly standing: DraftStanding;

    /** The files the message carries, oldest first, which is nothing until one is chosen. */
    readonly attached: readonly AttachedFile[];

    /** Files the draft in the user's own drafts folder, putting up whatever was attached and not yet staged. */
    readonly save: (composition: Composition) => Promise<boolean>;

    /** Takes a chosen file into the message. Nothing leaves the client: a save or a send is what puts it up. */
    readonly attach: (file: File) => void;

    /** Takes one file back off, and off the deployment as well where it had already reached it. */
    readonly detach: (attachmentId: string) => Promise<void>;

    /**
     * Queues the message, saving what has been written since first, and answers what it came to.
     *
     * The standing is answered as well as held because the surface says what became of a send where the design project
     * says it — in a toast, which stands over whatever the person moved on to rather than at the foot of the composer.
     * A toast is raised at the moment of the outcome rather than watched for, so what is owed to the caller is the
     * outcome itself; reading it back out of state afterwards would raise a second toast on every later render.
     */
    readonly send: (composition: Composition) => Promise<DraftStanding>;

    /**
     * Takes a queued send back while it has not begun transmitting, and answers what that came to.
     *
     * Asked before the deployment has answered the send, it withdraws the message the moment there is one to withdraw
     * rather than doing nothing: somebody who said stop while it was still going out said it about this message, and
     * asking them to say it again once a round trip has landed is a stop the client quietly dropped.
     */
    readonly withdraw: () => Promise<DraftStanding>;

    /**
     * Gives the draft up, taking its copies back out of the user's drafts folder.
     *
     * It reaches the deployment only where the deployment is actually holding something, which after the rule at the
     * head of this file is a send that was refused or failed. Closing a composer nothing was filed for asks nothing of
     * anybody.
     */
    readonly discard: () => Promise<boolean>;
}

export function useDraftAtDeployment(session: ClientSession, transport: MailFathomTransport): DraftAtDeployment {
    const upload = useAttachmentUpload();
    const telemetry = useTelemetry();
    const [standing, setStanding] = useState<DraftStanding>({ kind: 'held' });
    const [attached, setAttached] = useState<readonly AttachedFile[]>([]);

    // The draft the deployment holds, as a ref rather than as state: two acts in the same turn have to see the
    // identifier the first of them wrote, and nothing on the screen is drawn from it.
    const draftId = useRef<string | null>(null);

    // The files themselves, beside the state that draws them, for the reason the standing is kept twice: a save runs
    // through them one at a time and each round has to see what the round before it recorded, and what somebody took
    // off while a file was going up.
    const held = useRef<readonly HeldFile[]>([]);

    // What this composer calls the next file chosen. A counter rather than a random name because nothing outside this
    // composer ever sees it and a test should not have to fix a generator to read the list.
    const named = useRef(0);

    // The save in flight, if any. `saved` reads `draftId.current` and only writes it back once its own request has
    // answered, so two acts starting inside that window would each write a draft and strand whatever the loser staged
    // against it. A second caller joins the first rather than starting a second write.
    const saving = useRef<Promise<string | null> | null>(null);
    const queued = useRef<string | null>(null);
    const uploading = useRef<AbortController | null>(null);

    // Whether somebody has asked to stop the send while the deployment had not yet answered it. A ref rather than
    // state because nothing draws it: what reads it is the send itself, one turn later, and a value read out of a
    // render would be the one from before they asked.
    const stopping = useRef(false);

    // What the standing already is, beside the state that draws it. An act answers what it came to, and two acts in
    // one turn — a stop landing on a send, a refusal met while writing the draft a send needed — have to read what the
    // one before them settled on rather than the value this render was given.
    const settledAs = useRef<DraftStanding>({ kind: 'held' });

    function hold(next: DraftStanding): DraftStanding {
        settledAs.current = next;
        setStanding(next);

        return next;
    }

    function holdFiles(next: readonly HeldFile[]): void {
        held.current = next;
        setAttached(
            next.map(({ attachmentId, file }) => ({
                attachmentId,
                fileName: file.name,
                sizeOctets: file.size,
            })),
        );
    }

    // An upload whose composer has gone is an upload nobody is waiting for, and letting it finish would stage a file
    // against a draft the author has closed.
    useEffect(
        () => () => {
            uploading.current?.abort();
        },
        [],
    );

    function saved(composition: Composition, asked: DraftAct): Promise<string | null> {
        const already = saving.current;

        if (already !== null) {
            return already;
        }

        const writing = write(composition, asked);

        saving.current = writing;

        return writing.finally(() => {
            saving.current = null;
        });
    }

    async function write(composition: Composition, asked: DraftAct): Promise<string | null> {
        const draft = draftId.current;
        const wire = wireComposition(composition);

        const answer =
            draft === null
                ? await writeMailDraft(session, transport, wire)
                : await reviseMailDraft(session, transport, draft, wire);

        if (answer.outcome === 'failed') {
            hold({ kind: 'failed', reason: answer.failure.reason });

            return null;
        }

        // A refusal is said in the words of the act that met it. The deployment screens the draft book by the same
        // rules as the outbox and answers the same codes, so the same refusal reaches a save — and telling its author
        // the message was not sent, when they pressed save, names an act nobody performed. A send writes the draft
        // before it posts it, so the refusal it meets here is the one it would have met at the send itself, and is
        // worded as the send's.
        if (!answer.value.written) {
            hold(
                asked === 'send'
                    ? { kind: 'refused', refusal: answer.value.refusal }
                    : { kind: 'refusedSave', refusal: answer.value.refusal },
            );

            return null;
        }

        draftId.current = answer.value.draft.draftId;

        return answer.value.draft.draftId;
    }

    /**
     * Puts up every file the deployment does not have yet, oldest first, and answers whether all of them arrived.
     *
     * One at a time, because each is a request of its own against one draft and a failure part way through has to stop
     * the act rather than leave the rest going up behind a message that will not be sent. The list is re-read on every
     * round rather than walked as a snapshot: a file somebody took off while it was going up is taken off the
     * deployment too, so nothing they removed travels with the message.
     */
    async function stageHeld(draft: string): Promise<boolean> {
        for (;;) {
            const next = held.current.find((file) => file.stagedAs === null);

            if (next === undefined) {
                return true;
            }

            hold({ kind: 'attaching', fileName: next.file.name });

            const abandoning = new AbortController();
            uploading.current = abandoning;

            const answer = await stageMailDraftAttachment(
                session,
                draft,
                next.file.name,
                // What the file declares itself to be, which is what the author's own system said. A file the system
                // could not name is the general binary type, which is what the deployment reads a request declaring
                // none as anyway.
                next.file.type === '' ? 'application/octet-stream' : next.file.type,
                (request) => upload(request, next.file, abandoning.signal),
            );

            uploading.current = null;

            if (answer.outcome === 'failed') {
                hold({ kind: 'failed', reason: answer.failure.reason });

                return false;
            }

            if (held.current.some((file) => file.attachmentId === next.attachmentId)) {
                holdFiles(
                    held.current.map((file) =>
                        file.attachmentId === next.attachmentId
                            ? { ...file, stagedAs: answer.value.attachmentId }
                            : file,
                    ),
                );
            } else {
                // Taken off while it was going up. The author's act stands, so what arrived is taken back off the
                // deployment rather than left staged against a message that would then carry it — and a removal the
                // deployment refused stops the act, because carrying on would send a file somebody took off.
                const removed = await unstageMailDraftAttachment(session, transport, draft, answer.value.attachmentId);

                if (removed.outcome === 'failed') {
                    hold({ kind: 'failed', reason: removed.failure.reason });

                    return false;
                }
            }
        }
    }

    // Every act ends by saying what happened, and a failure says which of the five it was rather than that something
    // went wrong. Stated once here because five acts would otherwise each carry their own copy of the same three lines.
    function settled<TValue>(answer: ClientResult<TValue>, whenRead: (value: TValue) => DraftStanding): DraftStanding {
        return hold(
            answer.outcome === 'failed' ? { kind: 'failed', reason: answer.failure.reason } : whenRead(answer.value),
        );
    }

    async function send(composition: Composition): Promise<DraftStanding> {
        hold({ kind: 'sending' });

        const draft = await saved(composition, 'send');

        if (draft === null) {
            // The write said what stopped it and in the words of the send, so what this answers is that same
            // statement rather than a second, vaguer one made here. A stop asked while it was in flight goes with it:
            // it was asked about a message that never left.
            stopping.current = false;

            return settledAs.current;
        }

        if (!(await stageHeld(draft))) {
            // A message goes out whole or not at all: a file that did not arrive is one the reader would never know
            // was meant to be there, so the send stops on it and the draft stays where the author can try again.
            stopping.current = false;

            return settledAs.current;
        }

        hold({ kind: 'sending' });

        const outcome = settled(await sendMailDraft(session, transport, draft), (sent) => {
            if (!sent.queued) {
                return { kind: 'refused', refusal: sent.refusal };
            }

            queued.current = sent.outgoingEmailId;

            return { kind: 'queued', outgoingEmailId: sent.outgoingEmailId };
        });

        // A stop belongs to the send it was asked about, so it is read once here and cleared whatever became of that
        // send. Left standing over a send that was refused or failed, it would take back the *next* message somebody
        // deliberately sent — which is a message discarded on state nobody was acting on any more.
        const stopWasAsked = stopping.current;
        stopping.current = false;

        // What became of a send this client asked for, which the deployment's own outbox cannot say: it records what
        // it was handed rather than what somebody wrote and then could not send. The refusal is a name out of the
        // closed set the wire package publishes; no recipient, no subject, and no part of the message reaches this.
        telemetry.happened('message_sent', {
            'mailfathom.client.send': outcome.kind,
            ...(outcome.kind === 'refused' ? { 'mailfathom.client.refusal': outcome.refusal } : {}),
        });

        // Somebody asked to stop while the deployment had not yet answered, so this is where their stop lands.
        if (outcome.kind === 'queued' && stopWasAsked) {
            return withdraw();
        }

        return outcome;
    }

    async function withdraw(): Promise<DraftStanding> {
        const sent = queued.current;

        if (sent === null) {
            stopping.current = true;

            return settledAs.current;
        }

        const taken = settled(await withdrawOutgoingMail(session, transport, sent), (withdrawal) => ({
            kind: 'withdrawn' as const,
            withdrawal,
        }));

        // Somebody changing their mind inside the window the deployment gives them, which is a decision rather than a
        // failure and is why it is written at the client's own account level rather than above it. The deployment's own
        // answer is what is reported rather than the standing this hook composed from it: `withdrawn` is the standing
        // whatever the route said, so a take-back the deployment refused because the message was already going out
        // would otherwise read as one it accepted.
        telemetry.happened('send_withdrawn', {
            'mailfathom.client.withdrawal': taken.kind === 'withdrawn' ? taken.withdrawal : taken.kind,
        });

        return taken;
    }

    return {
        standing,
        attached,

        save: async (composition) => {
            hold({ kind: 'saving' });

            const draft = await saved(composition, 'save');

            if (draft === null || !(await stageHeld(draft))) {
                return false;
            }

            hold({ kind: 'saved' });

            return true;
        },

        attach: (file) => {
            named.current += 1;

            holdFiles([...held.current, { attachmentId: String(named.current), file, stagedAs: null }]);
        },

        detach: async (attachmentId) => {
            const going = held.current.find((file) => file.attachmentId === attachmentId);

            if (going === undefined) {
                return;
            }

            // Off the screen first: the author asked for it, and what the deployment still holds is this hook's
            // business rather than something they should watch happen.
            holdFiles(held.current.filter((file) => file.attachmentId !== attachmentId));

            const draft = draftId.current;

            if (going.stagedAs === null || draft === null) {
                return;
            }

            settled(await unstageMailDraftAttachment(session, transport, draft, going.stagedAs), () => ({
                kind: 'held',
            }));
        },

        send,
        withdraw,

        discard: async () => {
            // Whatever save is in flight first, so a draft written a moment ago is one this knows about rather than
            // one it leaves behind because the identifier had not landed yet.
            await saving.current;

            const draft = draftId.current;

            if (draft === null) {
                return true;
            }

            const answer = await discardMailDraft(session, transport, draft);

            if (answer.outcome === 'failed') {
                // Said rather than swallowed: closing on a refused delete would tell somebody their words are gone
                // while the deployment is still holding them, with nothing on this screen to go back to.
                hold({ kind: 'failed', reason: answer.failure.reason });

                return false;
            }

            draftId.current = null;

            return true;
        },
    };
}

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import {
    endSession,
    type ClientSession,
    type DeploymentAddress,
    type MailFathomSignalChannel,
    type MailFathomTransport,
    type NotificationTarget,
    type SignalStreamSchedule,
} from '@mailfathom/client-backend';
import { BlockingOverlay } from './blocking/BlockingOverlay';
import { BlockingContext, type BlockingOperation } from './blocking/useBlocking';
import { Composer } from './composer/Composer';
import type { ComposerOpening } from './composer/composition';
import { forgetComposition } from './composer/keptComposition';
import { ComposingContext } from './composer/useComposing';
import { Containment } from './containment/Containment';
import { BrandMark } from './controls/BrandMark';
import { PlannedControl } from './controls/PlannedControl';
import { VersionLine } from './controls/VersionLine';
import {
    forgetDeployment,
    storeDeployment,
    type AdoptedDeployment,
    type ClientDeployment,
    type ConfigurationRefusal,
} from './deployment/adoptedDeployment';
import type { PortraitExchange } from './deployment/portraitExchange';
import type { DeploymentTransport } from './deployment/sendToDeployment';
import { telemetryForwardedBy } from './deployment/telemetryForwarding';
import { FolderTree } from './folders/FolderTree';
import { FullHtmlSurface } from './fullHtml/FullHtmlSurface';
import type { MessageKey } from './localization/en';
import { useLocalization } from './localization/useLocalization';
import { NothingOpen } from './mailSpace/NothingOpen';
import { SurfaceWindow } from './mailSpace/SurfaceWindow';
import { TabStrip } from './mailSpace/TabStrip';
import { useOpenTabs } from './mailSpace/useOpenTabs';
import { MailboxActsProvider } from './mailboxActs/MailboxActs';
import { ListedMailProvider } from './messageList/ListedMail';
import { MessageList } from './messageList/MessageList';
import { forgetListings } from './messageList/rememberedListings';
import { NotificationBell } from './notifications/NotificationBell';
import { followTarget } from './notifications/notificationDestination';
import { NotificationCentre } from './notifications/NotificationCentre';
import { usePanelSwipe } from './notifications/usePanelSwipe';
import { useNotificationCentre } from './notifications/useNotificationCentre';
import { PendingChangesProvider } from './pendingChanges/PendingChanges';
import { EmbeddedHtmlMessagesContext } from './preferences/messageView';
import { useClientPreferences } from './preferences/useClientPreferences';
import { useOwnProfile } from './profile/useOwnProfile';
import { ReadMarkingProvider } from './readMarking/ReadMarking';
import { AttachmentView } from './readingPane/AttachmentView';
import { ReadingPane } from './readingPane/ReadingPane';
import { addressOf } from './routing/spaces';
import { useSpace } from './routing/useSpace';
import { MailSearch } from './search/MailSearch';
import { offers, spacesOffered, withheldFrom } from './shell/capabilities';
import { ConnectionSummary } from './shell/ConnectionSummary';
import { GrantNotice } from './shell/GrantNotice';
import { AccountMenu } from './shell/AccountMenu';
import { IntentField } from './shell/IntentField';
import { LanguageChoice, ThemeChoice } from './shell/Preferences';
import { Space } from './shell/Space';
import { SpaceNavigation } from './shell/SpaceNavigation';
import { useConnection } from './shell/useConnection';
import { useBackNavigation } from './shellOperations/backNavigation';
import { ScreenLayersContext, useScreenLayerStack } from './shell/screenLayers';
import { useCoarsePointer, useDesktopComposition, useTwoPanes, useWideWorkspace } from './shell/useWideWorkspace';
import { CredentialNotices, type CredentialNotice } from './signIn/CredentialNotices';
import type { CredentialStore } from './signIn/credentialStore';
import { writeKeptSession, type KeptSession } from './signIn/keptSession';
import { useSessionRenewal } from './signIn/useSessionRenewal';
import { SignIn } from './signIn/SignIn';
import { SignalledChangesContext } from './signals/signalledChanges';
import { useSignals } from './signals/useSignals';
import { useTelemetry } from './telemetry/clientTelemetry';
import { useNavigationTelemetry } from './telemetry/navigationTelemetry';
import { Thread } from './thread/Thread';
import { attachmentKey, OpenAttachmentContext, type OpenedAttachment } from './workspace/openAttachment';
import { conversationKey, type OpenConversation } from './workspace/openConversation';
import { scopeKey } from './workspace/mailScope';
import { emptyWorkspace, useWorkspace, type Workspace } from './workspace/useWorkspace';

// The frame Discover, Mail, and Cases are held in, and the only thing in the client that survives moving between them.
// It is one tree laid out two ways by the width it is given — a rail beside a workspace, or bottom navigation under a
// stack of screens — and nothing in it asks which head or which platform it is running on.
//
// In front of it stands what every run answers first: which deployment this client belongs to, and who is asking it.
// That is a screen rather than a state of the frame, because a frame with nothing behind it is a frame around
// nothing — and the two halves of the answer are one screen because a person was handed all of it together.
//
// What the frame holds is then the session's answer rather than a fixed set: the deployment says what this credential
// may do, and a space, a control, or a read it does not permit is absent here instead of present and refused when it
// is pressed. Enforcing that is the service's; declining to offer it is this frame's.

export function App({
    deployment,
    signedInWith,
    credentials,
    send,
    portraits,
    openSignals,
    signalSchedule,
}: {
    readonly deployment: ClientDeployment;
    readonly signedInWith: KeptSession | null;
    readonly credentials: CredentialStore;
    readonly send: DeploymentTransport;

    /** How the picture the signed-in person is drawn by is read and written, octets not being what a transport speaks. */
    readonly portraits: PortraitExchange;

    /** How a connection to the deployment's signal channel is opened, which is the composition root's for the reason the transport is. */
    readonly openSignals: MailFathomSignalChannel;

    /** How that connection waits before opening again, which is the browser's own timer and the browser's own spread. */
    readonly signalSchedule: SignalStreamSchedule;
}) {
    const { workspace, revise } = useWorkspace();
    const telemetry = useTelemetry();
    const [adopted, setAdopted] = useState(deployment.outcome === 'resolved' ? deployment.adopted : null);
    const [kept, setKept] = useState(signedInWith);
    const authorization = kept?.authorization ?? null;

    // Who is signed in, taken from what was kept rather than out of the credential. A session token names nobody — the
    // Basic header it replaced carried the name inside it — so the name travels beside it, and it is not a secret.
    const person = kept?.person ?? null;
    const [notices, setNotices] = useState<readonly CredentialNotice[]>([]);
    const baseAddress = adopted === null ? null : adopted.deployment.baseAddress;

    // What names this sign-in, and it is deliberately not the credential: a renewal replaces the token every eleven
    // hours, and a frame keyed on the value would empty the screen, take focus off whatever was being read, and record
    // a second session beginning — at an instant nothing happened at. The person and the address are what actually
    // change when somebody signs out and somebody else signs in.
    const signedInAs = person === null || baseAddress === null ? null : `${baseAddress}\n${person}`;
    const workspaceRegion = useRef<HTMLDivElement>(null);
    const [written, setWritten] = useState<ComposerOpening | null>(null);
    const [blockedOn, setBlockedOn] = useState<BlockingOperation | null>(null);
    const askedFrom = useRef<HTMLElement | null>(null);
    const focusedFor = useRef(signedInAs);

    // Built once per address and credential rather than per render, because it is what the message read below depends
    // on: a fresh object every render would restart that read every render.
    const session = useMemo(
        () => (baseAddress === null || authorization === null ? null : { baseAddress, authorization }),
        [baseAddress, authorization],
    );

    // Who the deployment is read for, which is the identity above and whatever is being presented for them now. It is
    // built per render rather than memoized because nothing keys on the object: the hook keys on the identity inside
    // it and reads the header when a request actually goes out.
    const signedInCaller =
        signedInAs === null || authorization === null ? null : { identity: signedInAs, authorization };

    // The transport those reads are made through, built once for the same reason. It carries a signal nothing ever
    // fires: the tree, the reading pane, and the body renderer each discard the answer to a read they stopped listening
    // for rather than cancelling it, which is what a screen that may be looking at another message by then actually
    // needs. A download is the one read here that is genuinely abandoned, and it carries a signal of its own from the
    // row that started it.
    const readMail = useMemo(() => send(new AbortController().signal), [send]);

    // The view changed, so focus goes to the start of what replaced it rather than staying on a control that is no
    // longer there. Only in this direction: the sign-in screen places focus itself, on the field it is asking to have
    // filled, and a parent effect runs after a child's and would take it back off. A cold start against a credential
    // that was kept is not a view change, so opening already signed in moves nothing.
    //
    // What separates the two is the credential this effect last acted on rather than a flag saying the first render
    // has happened. React invokes an effect twice on mount under `StrictMode`, which `main.tsx` mounts the application
    // in, and a flag the first invocation cleared is already cleared when the second one reads it — so the guard would
    // pull focus onto the workspace on exactly the ordinary open it exists to leave alone. Both invocations see the
    // same credential, so a comparison against it survives being run twice.
    useEffect(() => {
        if (signedInAs === focusedFor.current) {
            return;
        }

        focusedFor.current = signedInAs;

        if (signedInAs !== null) {
            workspaceRegion.current?.focus();
        }
    }, [signedInAs]);

    // A credential the deployment has stopped accepting is acted on once rather than left to produce the same refusal
    // on every later read, which is why this is the one failure the frame does not render. What was kept goes with it:
    // a stored password the service refuses is a password nothing will make work again.
    //
    // It is held steady across renders because the connection below reads again whenever it changes, and a callback
    // rebuilt every render would be a read started every render.
    const credentialRefused = useCallback(() => {
        telemetry.happened('credential_no_longer_accepted');
        setNotices(['credentialNoLongerAccepted']);
        setKept(null);
        revise(emptyWorkspace);
        forgetListings();
        forgetComposition();

        if (baseAddress === null) {
            return;
        }

        void credentials.forget({ baseAddress }).then((removed) => {
            if (!removed) {
                setNotices((shown) => [...shown, 'sessionNotRemoved']);
            }
        });
    }, [baseAddress, credentials, revise, telemetry]);

    // A renewed session replaces what is held and what is kept, in that order and in one place: holding it without
    // keeping it would leave the next start presenting a token this run has already replaced, which the deployment
    // refuses. A store that would not write is not reported again here — the screen said what it would keep at
    // sign-in, and saying it a second time mid-morning tells nobody anything they can act on.
    const sessionRenewed = useCallback(
        (renewed: KeptSession) => {
            setKept(renewed);

            if (baseAddress !== null) {
                void credentials.keep({ baseAddress }, writeKeptSession(renewed));
            }
        },
        [baseAddress, credentials],
    );

    // What the deployment says is read from the address and the credential rather than held beside them, which is what
    // makes a credential unable to outlive the deployment it was presented to: pointing the client somewhere else, or
    // signing out, runs this again with nothing to present, and nothing of the previous one's answers survives it.
    const connection = useConnection(baseAddress, signedInCaller, send, credentialRefused);

    // A session has a life the deployment decides, so the client renews it rather than letting somebody be signed
    // out mid-morning. It renews only while there is a session to renew and a network to renew over; a client that was
    // offline across its own expiry is signed out at the next request, which is the same path a revoked one takes.
    useSessionRenewal(session, kept, readMail, connection.online, sessionRenewed, credentialRefused);

    const deploymentSession = connection.session?.outcome === 'read' ? connection.session.value : null;
    const offeredSpaces = deploymentSession === null ? [] : spacesOffered(deploymentSession);
    const space = useSpace(offeredSpaces);
    useNavigationTelemetry(space);
    const withheld = deploymentSession === null ? [] : withheldFrom(deploymentSession);
    const mailAccounts = connection.accounts?.outcome === 'read' ? connection.accounts.value.accounts : [];
    const readsMail = deploymentSession !== null && offers(deploymentSession, 'readMail');
    const writesMail = deploymentSession !== null && offers(deploymentSession, 'composeMail');

    // The two grants the acts on a mailbox are reached under, which are separate for the reason
    // `shell/capabilities.ts` gives: a wrong flag misdescribes mail somebody can still find, and a wrong move puts it
    // somewhere else. A credential holding one and not the other therefore meets a control that says so on the acts it
    // may not perform, rather than a strip that refuses everything.
    const writesFlags = deploymentSession !== null && offers(deploymentSession, 'writeMailFlags');
    const filesMail = deploymentSession !== null && offers(deploymentSession, 'fileMail');

    // What is being written, held here for the reason the workspace is: the three controls that ask for it are each
    // several components below this, and what it replaces is a region this frame composes. It is the opening alone —
    // the message itself is the composer's, so nothing here can read half a message off the frame.
    const composing = useMemo(
        () => ({
            offered: writesMail,
            opening: written,
            compose: (asked: ComposerOpening) => {
                // What asked for it, so that closing hands the keyboard back to it. The three controls that ask are in
                // three different components, and remembering the one that had focus is one place rather than a ref
                // threaded through each of them.
                askedFrom.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;

                setWritten(writesMail ? asked : null);
            },
            close: () => {
                setWritten(null);
                askedFrom.current?.focus();
                askedFrom.current = null;
            },
        }),
        [writesMail, written],
    );

    // What the whole client is blocked on, held here for the reason above and one more: the surface covers everything
    // this frame draws, so nothing below it could own the state without owning what is drawn over it as well. The
    // operations that reach for it arrive from stage 5 onwards; what this holds is the surface and the way to ask for
    // it, so that each of them asks rather than inventing a modal of its own.
    const blocking = useMemo(
        () => ({
            block: (operation: BlockingOperation) => {
                setBlockedOn(operation);
            },
            release: () => {
                setBlockedOn(null);
            },
        }),
        [],
    );

    // The settings that follow the person rather than this machine. They are read here rather than in the menu that
    // shows them because two of them decide what opening a message does, which is the frame's rather than a menu's —
    // the tab mode, whose other half lands in #1494 and will already be here, and whether opening one marks it read.
    //
    // Asked for on the same three conditions the mail itself is: somebody signed in, a machine with a network, and a
    // credential the deployment lets read. The route is admitted under the grant a reader already holds, so a
    // credential without it would meet a refusal the screen has nothing to do about, and a machine with no network
    // would meet nothing at all.
    const preferences = useClientPreferences(readsMail && connection.online ? session : null, readMail, person);

    // What this client reports about itself goes out under the session that is signed in, so it starts when one exists
    // and stops with it. Signing out, or being pointed at another deployment, therefore leaves nothing queued for a
    // deployment somebody has left — and the session beginning is itself the first thing recorded.
    //
    // Both halves of the permission have to hold: somebody has to have agreed to be reported on, and the deployment
    // has to forward telemetry at all. They travel with the session rather than through a call of their own, so a
    // change to any of the three restarts one effect — the pipeline that was running is torn down before the next is
    // asked for, which is the ordering a separate stop and start racing each other would not have.
    //
    // The two halves are unanswered differently on purpose. What the person agreed to is answered from this device
    // until the deployment answers, so a client that had been turned off records nothing in the seconds a read takes.
    // What the deployment forwards is unknown rather than refused until it says — a deployment nobody has reached yet
    // is every cold start and every failed sign-in, which is exactly what the pipeline holds records for, so refusing
    // there would throw away the failures somebody cannot otherwise describe. Only `false`, which is a deployment
    // stating that it forwards nothing, stops it; and stopping it discards what was held rather than sending it.
    const telemetryPermitted = preferences.telemetryEnabled && deploymentSession?.telemetryForwarded !== false;

    // Which session has already been reported as having begun, so that it is reported once however many times this
    // effect runs. It runs again whenever the permission changes, and the permission is false until the deployment has
    // answered what it forwards — so without this, an ordinary sign-in would record nothing and moving the switch
    // twice would record a session beginning twice.
    const sessionReported = useRef<string | null>(null);

    useEffect(() => {
        const stop = telemetry.exportFor(session, telemetryPermitted);

        // Forgotten on the way out as well as written on the way in, because this frame is not unmounted by signing
        // out — it renders the sign-in screen instead. A value left behind would make signing back in as the same
        // person at the same deployment record nothing, and the commonest way to reach that is the path this event is
        // most about: the deployment refuses the kept session and somebody signs straight back in.
        if (session === null) {
            sessionReported.current = null;
        } else if (telemetryPermitted && sessionReported.current !== signedInAs) {
            sessionReported.current = signedInAs;
            telemetry.happened('session_started');
        }

        return stop;
    }, [session, signedInAs, telemetry, telemetryPermitted]);

    // Whether opening a message marks it read on the person's own mail server, which is the frame's answer rather than
    // a screen's for the reason ADR 0026 gives about the two halves of it: the reader's own setting says what they want
    // of every account they read, and the grant says whether this credential may write a flag at all. Either missing is
    // the same client — one that draws what the mail server last reported and marks nothing.
    const markingRead =
        preferences.markReadOnOpen && deploymentSession !== null && offers(deploymentSession, 'writeMailFlags');

    // Whether the person is working in tabs, which is the two halves of that question and nothing else: what they set,
    // and a window with room for a row of tabs above the columns. Below that width the mode is inert rather than off —
    // the switch stays in the menu and says so — so narrowing returns the pane layout and widening returns the strip.
    const desktopComposition = useDesktopComposition();
    const inTabs = preferences.openMailInTabs && desktopComposition;
    const openTabs = useOpenTabs(inTabs);

    // Who the person is, asked on the same three conditions and for the same reasons. It is read here rather than in
    // the menu that shows it because the settings screen behind that menu writes it, and two reads made separately
    // would disagree the moment one of them did.
    const profile = useOwnProfile(readsMail && connection.online ? session : null, readMail, portraits);

    // What the deployment says has changed while somebody is looking at it, held on the same three conditions every
    // read above is: a credential that may not read mail is told nothing about it, and a machine with no network has
    // no connection to open. A screen subscribes to decide what to read again; nothing here renders, and a deployment
    // that serves no channel leaves every screen on the interval it already had.
    const signalledChanges = useSignals(
        readsMail && connection.online ? session : null,
        readMail,
        openSignals,
        signalSchedule,
    );

    // The account's own state is the frame's to re-read, because what it moves is what the frame draws: how current
    // each account is, and the freshness line beside it. Every other kind is a screen's, and each of them subscribes
    // where it decides what to ask for again.
    //
    // Through a ref because the way to ask is a new function on every render while the subscription is not: reading it
    // when a signal arrives is what keeps one subscription across the renders, instead of one torn down and rebuilt
    // every time anything on this frame changes.
    const rereadConnection = useRef(connection.reread);

    useEffect(() => {
        rereadConnection.current = connection.reread;
    }, [connection.reread]);

    useEffect(
        () =>
            signalledChanges.listen((signal) => {
                if (signal.kind === 'account.state') {
                    rereadConnection.current();
                }
            }),
        [signalledChanges],
    );

    // Where a notification leads. What each target kind comes to is `notifications/notificationDestination.ts`, which
    // is where it can be asserted without a frame around it; what this supplies is the two things this client can
    // actually do about one, because opening a message and reaching a space are the frame's rather than a mapping's.
    const followNotification = useCallback(
        (target: NotificationTarget): void => {
            followTarget(target, {
                openMail: (storedEmailId) => {
                    openTabs.openMail(storedEmailId, null);
                },
                goTo: (space) => {
                    window.location.hash = addressOf(space);
                },
            });
        },
        [openTabs],
    );

    // Asked for on the three conditions the mail itself is, and on the grant the routes are admitted under: the
    // notification routes read what this deployment holds about one person's mail, so a credential that may not read
    // mail meets no bell rather than a bell that answers nothing.
    // The network is not part of that condition, and deliberately: what was read before it went stays on the screen
    // rather than being cleared, which is the offline state every other surface here holds to. The reads themselves
    // stop, which is what the hook is handed `online` for.
    const notifications = useNotificationCentre(
        readsMail ? session : null,
        readMail,
        connection.online,
        followNotification,
    );

    // The two gestures belong to the phone composition and to a coarse pointer, which is the one place this frame asks
    // what the pointer can do: everything else about the two compositions is a width the stylesheet already answers.
    const coarsePointer = useCoarsePointer();
    const wideWorkspace = useWideWorkspace();
    const swipe = usePanelSwipe(
        notifications.shown,
        readsMail && coarsePointer && !wideWorkspace,
        notifications.show,
        notifications.hide,
    );

    // Everything standing over the screen, so that the back gesture reaches the topmost one and moving to another
    // destination leaves none of them behind. Held here for the reason the composer and the blocking surface are: a
    // drawer in the Mail space, a menu on the rail, a sheet at the foot of a phone, and a confirmation opened from any
    // of them have no common parent below this, and what the shell asks of them is asked of all of them at once.
    const layers = useScreenLayerStack();
    const twoPanes = useTwoPanes();

    // What is standing in front of what a reader was last looking at, which is what the back gesture unwinds before it
    // moves anywhere. The two surfaces count here only where they are tabs in the reading column: where somebody does
    // not work in tabs each is drawn in a window over the message, and a window registers itself as a screen layer the
    // way every other modal surface does — so counting it here as well would spend two presses on one window. The
    // message covering the list counts only where the composition shows one pane, since that is the only composition
    // where opening one took the other off the screen.
    //
    // One list rather than a count beside a chain of branches, because how many steps stand there and what each of
    // them does to go away are one fact read twice. A press that unwinds two of them at once is what makes the
    // difference: asking the workspace again for the second step would answer with the surface the first has already
    // taken away, this event holding the revision that closed one no more than it holds the one that opened it.
    const inFrontOfTheList = [
        inTabs && workspace.attachment !== null ? openTabs.closeAttachment : null,
        inTabs && workspace.fullHtml !== null ? openTabs.closeFullHtml : null,
        twoPanes || (workspace.selection === null && workspace.conversation === null)
            ? null
            : () => {
                  revise({ selection: null, conversation: null });
              },
    ].filter((close) => close !== null);

    useBackNavigation(layers.depth + inFrontOfTheList.length, (used) => {
        let left = used;

        while (left > 0 && layers.closeTop()) {
            left -= 1;
        }

        // Topmost first, which is the order the list is written in: a file stands in front of the message it was
        // opened from, and that message in front of the list it was opened from.
        for (const close of inFrontOfTheList.slice(0, left)) {
            close();
        }
    });

    // Moving to another destination closes what was standing over the one being left, so that coming back to it gives
    // that screen rather than the layer somebody had over it. A layer that survived would be one they cannot get rid of
    // without finding its own close control, which on a phone is where it is hardest to find.
    //
    // The reading column's own surfaces go with them, and for a second reason as well as that one: they are steps the
    // back gesture has to unwind, and a step left standing while another space is on the screen is a press that closes
    // something nobody can see instead of leaving the space they are in.
    const destination = useRef(space);
    const { closeAttachment, closeFullHtml } = openTabs;

    // The first destination is arrived at rather than moved to: the space is unknown until the session says which ones
    // this credential is offered, so the client's own opening screen would otherwise read as somewhere it had been
    // taken away from — and would clear a reader's place the moment a reload restored it.
    useEffect(() => {
        const left = destination.current;

        destination.current = space;

        if (left === null || left === space) {
            return;
        }

        layers.closeEvery();
        closeAttachment();
        closeFullHtml();
        revise({ selection: null, conversation: null });
    }, [space, layers, closeAttachment, closeFullHtml, revise]);

    function signedIn(reached: DeploymentAddress, session: KeptSession): void {
        if (adopted === null) {
            storeDeployment(reached);
            setAdopted({ deployment: reached, origin: 'chosen' });
        }

        setNotices([]);

        // Signing in is somebody arriving rather than somebody returning: a tab whose credential was not kept can be
        // signed into by a second person, and what the first one was looking at and where they were reading is theirs.
        // A reload of a signed-in client does not pass through here, so what survives a reload still survives one.
        revise(emptyWorkspace);
        forgetListings();
        forgetComposition();

        // The screen has already said how long the sign-in will be kept, so a store that refused the write says so
        // rather than leaving somebody to discover it by being asked for the password again at the next start. This
        // one is read inside the frame: signing in worked, and what failed is only the keeping.
        void credentials.keep(reached, writeKeptSession(session)).then((stored) => {
            if (!stored) {
                setNotices(['sessionNotKept']);
            }
        });
        setKept(session);
    }

    // Everything this session held goes with the credential, including what the person carried between the spaces:
    // the question in the intent field and the mailbox it was scoped to are theirs rather than the machine's, and a
    // client that kept them would show the next person what the last one was asking about.
    //
    // A store that would not delete is reported rather than swallowed. The screen has already said that signing out is
    // what removes the password, so a refused deletion leaves it on the machine for the next start to read back while
    // the person believes they signed out.
    function signOut(): void {
        setNotices([]);
        setKept(null);
        revise(emptyWorkspace);
        forgetListings();
        forgetComposition();

        if (adopted === null) {
            return;
        }

        // The deployment is told first, so the token stops working before it would have expired rather than staying
        // good for whatever is left of its life. It is asked and not waited on: signing out of the client is what the
        // person pressed, and a deployment that never heard still expires the session on its own.
        if (session !== null) {
            void endSession(session, readMail);
        }

        void credentials.forget(adopted.deployment).then((removed) => {
            if (!removed) {
                setNotices(['sessionNotRemoved']);
            }
        });
    }

    // What the reading column holds: the empty state where somebody working in tabs has closed all of them, and what
    // they have open otherwise. A function rather than a chain inside the markup, for the reason `frontend/src`'s
    // instructions give about where markup ends.
    function whatIsOpen(): ReactNode {
        if (inTabs && openTabs.tabs.length === 0) {
            return <NothingOpen arriving={openTabs.emptiedByClosing} onReopenLastRead={openTabs.reopenLastRead} />;
        }

        if (session === null) {
            return null;
        }

        // The one region of this client that draws a document assembled out of mail somebody else sent, which is where
        // a defect nobody anticipated is most likely to be met. Contained here rather than further in, because what a
        // reader needs when a message cannot be drawn is the list they opened it from: the surfaces below stand in one
        // position and each of them draws what a row was opened, so a boundary around any one of them would leave the
        // other three uncontained.
        return (
            <Containment drawing={whatTheColumnDraws(workspace)} region="reading_pane">
                <OpenMail
                    session={session}
                    transport={readMail}
                    conversation={workspace.conversation}
                    fullHtml={workspace.fullHtml}
                    attachment={workspace.attachment}
                    storedEmailId={workspace.selection}
                    online={connection.online}
                    inTabs={inTabs}
                    expandWholeThread={preferences.expandWholeThread}
                    onShowFullHtml={openTabs.openFullHtml}
                    onCloseFullHtml={openTabs.closeFullHtml}
                    onCloseAttachment={openTabs.closeAttachment}
                />
            </Containment>
        );
    }

    function pointSomewhereElse(): void {
        signOut();
        forgetDeployment();
        setAdopted(null);
    }

    if (baseAddress === null || authorization === null) {
        return (
            <SignInScreen
                adopted={adopted}
                refusal={deployment.outcome === 'refused' ? deployment.refusal : null}
                clearTextPermitted={deployment.outcome === 'resolved' ? deployment.clearTextPermitted : null}
                lifetime={credentials.lifetime}
                notices={notices}
                send={send}
                onSignedIn={signedIn}
                onPointSomewhereElse={pointSomewhereElse}
            />
        );
    }

    return (
        // Above every surface that can stand over the screen, which is every one of them: what the back gesture reaches
        // and what a change of destination closes are questions about the frame rather than about whichever surface is
        // on top. It is not above the sign-in screen, because that screen stands in front of the frame rather than
        // inside it and opens nothing over itself.
        <ScreenLayersContext value={layers}>
            {/* Above every screen that would act on one, because what changed is the deployment's statement rather than
            any one screen's: the tree, the list, and the bell each subscribe to the kinds they draw, and a second
            connection per screen would be several sockets saying the same thing. */}
            <SignalledChangesContext value={signalledChanges}>
                {/* Above everything that changes a mailbox, because following a change is not the business of whichever
            screen asked for one: what is waiting, what the deployment refused, and what only a person can settle are
            one queue whoever produced the change. */}
                <PendingChangesProvider session={session} transport={readMail}>
                    {/* Above the frame rather than inside a space, because three unrelated places below read what has been
            marked: the row that draws a message, the folder tree that counts unread mail, and the body that marks one
            on being drawn. What it holds goes with the credential, exactly as the workspace does. */}
                    <ReadMarkingProvider session={session} transport={readMail} marking={markingRead}>
                        {/* Above the frame for the same reason: which of the two reading surfaces a message opens on decides
                how every message anywhere below is drawn, and the two components that read it — the body and the
                control on the message head — sit several levels under the reading pane and under the conversation
                alike. */}
                        <EmbeddedHtmlMessagesContext value={preferences.embeddedHtmlMessages}>
                            {/* Above the frame for the reason the marking is, and above the acts because the acts read it: what the
            list has drawn is where a message belongs, and every act on one has to name that. It holds nothing that is
            drawn, so nothing below re-renders because of it. */}
                            <ListedMailProvider>
                                {/* Above the frame as well, because four surfaces reach the same five acts — the toolbar over what is
            open, the bar over a selection, the row that draws one as pending, and the message somebody swiped — and a
            second implementation of *archive* is how two of them come to file mail differently. */}
                                <MailboxActsProvider
                                    session={readsMail ? session : null}
                                    transport={readMail}
                                    online={connection.online}
                                    flags={writesFlags}
                                    moves={filesMail}
                                >
                                    {/* Above the frame rather than inside the mail space, because what is being written outlives moving
            between the spaces, and because the three controls that ask for it are each several components below here.
            Writing is offered where the credential may file a draft; one that may not is told so by the strip above
            rather than by a screen that refuses every act. */}
                                    <ComposingContext value={composing}>
                                        {/* Above the frame as well, and for a stronger reason than the composer's: what it draws covers
                everything below it, and the operations that ask for it — a mailbox migration, a bulk change, an
                export — each begin somewhere different and none of them is the parent of what has to be covered. The
                surface itself is the platform's own modal dialog, so where it sits in the document decides nothing
                about where it is drawn; it stands first here because it stands in front of everything. */}
                                        <BlockingContext value={blocking}>
                                            <BlockingOverlay operation={blockedOn} />

                                            {/* Beside them for the composer's own reason, one component further down: the row that opens a
                    file a message carries sits three components below the frame that owns what is open, and none of
                    the three between them has a reason to name a file it never opens. */}
                                            <OpenAttachmentContext value={openTabs.openAttachment}>
                                                <div className="flex h-dvh flex-col bg-rail pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left workspace:flex-row">
                                                    <div
                                                        ref={workspaceRegion}
                                                        tabIndex={-1}
                                                        className="flex min-h-0 min-w-0 flex-1 flex-col bg-page"
                                                    >
                                                        {/* Inside the frame as well as on the sign-in screen, because a credential that could not be kept is
                    learned about at the moment somebody successfully signed in — which is the one of these sentences
                    whose reader is already past that screen. Beside it, and in the same strip, is what this credential
                    may not do: both are statements about the credential rather than about anything it read. */}
                                                        {notices.length === 0 && withheld.length === 0 ? null : (
                                                            <div className="flex flex-col gap-2 border-b border-line-soft bg-panel px-4 py-2 workspace:px-8">
                                                                <CredentialNotices notices={notices} />
                                                                <GrantNotice withheld={withheld} />
                                                            </div>
                                                        )}

                                                        {/* The region the space is drawn in is there before the deployment says which space that is, so
                    nothing on the screen moves under a reader when the answer arrives. Until it does, what stands in
                    the region is what the connection says — reaching, retrying, offline, or refused — because that is
                    the only thing on the screen there is to read, and the way out of a deployment that never answers
                    has to be somewhere. */}
                                                        {space === null ? (
                                                            <main className="flex-1 px-4 py-6 workspace:px-8">
                                                                <ConnectionSummary connection={connection} />
                                                            </main>
                                                        ) : (
                                                            <Space
                                                                space={space}
                                                                // Who is signed in, which is what was kept beside the session rather than anything read out of a credential.
                                                                // A screen never sees the credential; what it is handed is the name the deployment knows the
                                                                // person by, which is what a preference kept per person on this machine is written under.
                                                                person={person}
                                                                // Asking is what the field is for, so a credential that may not ask is not shown one. It is
                                                                // absent rather than disabled: a control nobody can use says less about why than the sentence
                                                                // above does. Where it stands is the space's decision, which is why it is handed in rather
                                                                // than drawn here.
                                                                intent={
                                                                    // Not while a message is being written: what stands at the foot of that column then is
                                                                    // the composer's own footer, and two rows of controls under one column is two answers
                                                                    // to what the primary act is.
                                                                    written === null &&
                                                                    deploymentSession !== null &&
                                                                    offers(deploymentSession, 'askMail') ? (
                                                                        <IntentField accounts={mailAccounts} />
                                                                    ) : null
                                                                }
                                                                status={<ConnectionSummary connection={connection} />}
                                                                folders={
                                                                    session === null || !readsMail ? null : (
                                                                        <FolderTree
                                                                            session={session}
                                                                            transport={readMail}
                                                                            online={connection.online}
                                                                        />
                                                                    )
                                                                }
                                                                list={
                                                                    session === null || !readsMail ? null : (
                                                                        // Both keyed by the scope, so pointing at another mailbox starts a list and a search
                                                                        // rather than resetting either: every value below belongs to one mailbox read one way,
                                                                        // and a search carries the mailbox it was made in as a filter it would go on showing.
                                                                        // Searching stands above the list rather than beside it because it is where somebody
                                                                        // reaches for it — looking at a folder, with the message not in front of them — and
                                                                        // what it finds is drawn in the same column with the same row.
                                                                        <MailSearch
                                                                            key={scopeKey(workspace.scope)}
                                                                            session={session}
                                                                            transport={readMail}
                                                                            scope={workspace.scope}
                                                                            accounts={mailAccounts}
                                                                            online={connection.online}
                                                                            onOpen={openTabs.openMail}
                                                                        >
                                                                            <MessageList
                                                                                key={scopeKey(workspace.scope)}
                                                                                session={session}
                                                                                transport={readMail}
                                                                                scope={workspace.scope}
                                                                                accounts={mailAccounts}
                                                                                online={connection.online}
                                                                                onOpen={openTabs.openMail}
                                                                            />
                                                                        </MailSearch>
                                                                    )
                                                                }
                                                                tabs={
                                                                    /* A map of what is open, so it is drawn only where there is something to map: an empty
                                   strip over an empty pane would say the same thing twice. */
                                                                    inTabs && openTabs.tabs.length > 0 ? (
                                                                        <TabStrip
                                                                            tabs={openTabs.tabs}
                                                                            active={openTabs.active}
                                                                            onActivate={openTabs.activate}
                                                                            onClose={openTabs.close}
                                                                            onCloseEverything={openTabs.closeEverything}
                                                                        />
                                                                    ) : null
                                                                }
                                                                mail={
                                                                    // A message being written stands where one being read stands, which is the design
                                                                    // project's composition rather than a window over it — and what is open is still open
                                                                    // underneath, so closing the composer is a return rather than a second thing to find.
                                                                    // Keyed by what is being written, so asking for an answer while a message of its own is
                                                                    // open starts that answer rather than pouring it into the fields already on the screen.
                                                                    written === null || session === null ? (
                                                                        whatIsOpen()
                                                                    ) : (
                                                                        <Composer
                                                                            key={openingKey(written)}
                                                                            session={session}
                                                                            transport={readMail}
                                                                            accounts={mailAccounts}
                                                                            opening={written}
                                                                            online={connection.online}
                                                                            onClosed={composing.close}
                                                                        />
                                                                    )
                                                                }
                                                            />
                                                        )}
                                                    </div>

                                                    {/* Navigation is last in the document because the keyboard follows the document rather than the layout,
                and the narrow composition puts it at the bottom of the screen: written the other way round, a reader
                tabbing into a narrow window would reach the bottom bar before the header above it. The wide
                composition then carries the one mismatch CSS cannot remove — a rail drawn on the left out of a node
                that comes last — because no single document order matches both shapes, and content before navigation
                is the direction a skip link exists to manufacture rather than the one it works around. */}
                                                    <SpaceNavigation
                                                        offered={offeredSpaces}
                                                        current={space}
                                                        onPointerDown={swipe.onNavigationPointerDown}
                                                        onClickCapture={swipe.onNavigationClickCapture}
                                                        notifications={
                                                            // Offered on the grant the routes are admitted under, and absent
                                                            // rather than inert without it: a bell that could never answer is
                                                            // a control saying less about why than not drawing it does.
                                                            readsMail ? (
                                                                <NotificationBell
                                                                    unreadCount={notifications.unreadCount}
                                                                    shown={notifications.shown}
                                                                    onPress={
                                                                        notifications.shown
                                                                            ? notifications.hide
                                                                            : notifications.show
                                                                    }
                                                                />
                                                            ) : null
                                                        }
                                                        account={
                                                            <AccountMenu
                                                                accounts={mailAccounts}
                                                                deploymentVersion={deploymentSession?.version ?? null}
                                                                telemetryForwarding={telemetryForwardedBy(
                                                                    deploymentSession,
                                                                    baseAddress,
                                                                )}
                                                                preferences={preferences}
                                                                profile={profile}
                                                                onSignOut={signOut}
                                                            />
                                                        }
                                                    />

                                                    {/* Outside the frame's own columns because it stands over all of them,
                                                and inside the frame because it goes with the credential: it is the
                                                platform's own modal dialog, so where it sits in the document decides
                                                nothing about where it is drawn. It is mounted whether or not it is
                                                open, which is what lets it travel on and off the screen rather than
                                                appearing and disappearing. */}
                                                    {readsMail ? (
                                                        <NotificationCentre centre={notifications} swipe={swipe} />
                                                    ) : null}
                                                </div>
                                            </OpenAttachmentContext>
                                        </BlockingContext>
                                    </ComposingContext>
                                </MailboxActsProvider>
                            </ListedMailProvider>
                        </EmbeddedHtmlMessagesContext>
                    </ReadMarkingProvider>
                </PendingChangesProvider>
            </SignalledChangesContext>
        </ScreenLayersContext>
    );
}

// What a composer is mounted under, so that asking for a second message replaces the first rather than editing it.
function openingKey(opening: ComposerOpening): string {
    return opening.kind === 'new' ? 'new' : `${opening.answers}:${opening.storedEmailId}`;
}

// What is being read on the right of the mail space: one message, the conversation it belongs to, the sender's own
// markup, or one file the message carries — the last three standing in front of the message rather than instead of it,
// which is why the message they were opened from is still what this component is holding. Closing any of them draws
// what is behind it again with nothing having had to remember it.
//
// The markup surface is in front of the conversation as well as of the message, because it is opened from a message's
// head and returns to whatever that head was drawn in. The file is asked about before either, because it was opened
// from whatever was on the screen: a reader who opened an attachment from a message inside a conversation is looking
// at the file, and the conversation is what they go back to.
//
// Keyed by the conversation together with the message it was opened at, because what a conversation opens with is
// decided once from what it holds then: opening the same conversation at another message is a screen of its own rather
// than the same one adjusted. The file is keyed for the same reason and by the same rule.
// What the reading column is drawing, as one value the boundary around it compares against the last one. Every surface
// that stands in that position is in it rather than the message alone, because each is a different thing to draw: a
// file that could not be shown says nothing about the conversation behind it, and a message the pane failed on says
// nothing about the next message somebody opens.
function whatTheColumnDraws(workspace: Workspace): string {
    const attachment = workspace.attachment === null ? '' : attachmentKey(workspace.attachment);
    const conversation = workspace.conversation === null ? '' : conversationKey(workspace.conversation);

    return [workspace.selection ?? '', workspace.fullHtml ?? '', conversation, attachment].join('\n');
}

function OpenMail({
    session,
    transport,
    conversation,
    fullHtml,
    attachment,
    storedEmailId,
    online,
    inTabs,
    expandWholeThread,
    onShowFullHtml,
    onCloseFullHtml,
    onCloseAttachment,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly conversation: OpenConversation | null;
    readonly fullHtml: string | null;
    readonly attachment: OpenedAttachment | null;
    readonly storedEmailId: string | null;
    readonly online: boolean;
    readonly onShowFullHtml: (storedEmailId: string, subject: string | null) => void;
    readonly onCloseFullHtml: () => void;
    readonly onCloseAttachment: () => void;

    /**
     * Whether the person is working in tabs, which is what decides the shape either surface is drawn in rather than
     * which of them was opened: a tab in the reading column where they are, and a window over the message where they
     * are not.
     */
    readonly inTabs: boolean;

    /** Whether the reader asked for conversations to open with every message drawn, which only the conversation reads. */
    readonly expandWholeThread: boolean;
}) {
    const { translate } = useLocalization();

    // Whether the pane below is being arrived at rather than landed on. Closing whatever stood in front of the message
    // swaps this position from one component to the other, so the pane mounts afresh exactly as it does on a cold start
    // and cannot tell the two apart from anything it holds itself — this is the only place that saw the surface go.
    // Adjusted during render, which is React's answer to state that a changed prop invalidates, rather than read from a
    // ref written during one: a ref would be written twice under StrictMode and the second pass would report that
    // nothing had been in front.
    //
    // The question is *whether something was in front* rather than which of the three it was, so the conversation, the
    // markup surface, and the file are one value here. Asking it per surface is how closing the second one would leave
    // focus on a control that has just been unmounted, while closing the first placed it correctly.
    //
    // Neither surface is in front of the pane where they are drawn as windows: the pane goes on standing underneath one
    // and is never unmounted by it, and what places focus as the window goes is the platform handing it back to the
    // control that opened it. Counting it here as well would have the pane take focus off that control.
    const covered = conversation !== null || (inTabs && (fullHtml !== null || attachment !== null));
    const [wasCovered, setWasCovered] = useState(covered);
    const [arriving, setArriving] = useState(false);

    if (wasCovered !== covered) {
        setWasCovered(covered);
        setArriving(!covered);
    }

    // Which surface is open, what it is drawn as, and what it comes to — written once, because the two shapes below
    // differ in where the surface stands rather than in what it is. Keyed by what was opened, so opening a second file
    // from the same message is a surface of its own rather than the first one adjusted — which is what keeps a read of
    // one file from drawing into the screen that asked for another.
    const opened =
        attachment !== null
            ? {
                  drawn: 'file' as const,
                  label: attachment.attachment.fileName ?? translate('attachment.unnamed'),
                  onClosed: onCloseAttachment,
                  draw: (close: () => void) => (
                      <AttachmentView
                          key={attachmentKey(attachment)}
                          session={session}
                          opened={attachment}
                          online={online}
                          onClose={close}
                      />
                  ),
              }
            : fullHtml !== null
              ? {
                    drawn: 'markup' as const,
                    label: translate('fullHtml.surface'),
                    onClosed: onCloseFullHtml,
                    draw: (close: () => void) => (
                        <FullHtmlSurface
                            key={fullHtml}
                            session={session}
                            transport={transport}
                            storedEmailId={fullHtml}
                            online={online}
                            onClose={close}
                        />
                    ),
                }
              : null;

    // In tabs the surface *is* the reading column, because a tab of its own is what it was opened as and the message it
    // came from is a tab beside it. Closing it is the workspace letting go of it, there being no window to leave.
    if (inTabs && opened !== null) {
        return opened.draw(opened.onClosed);
    }

    return (
        <>
            {conversation === null ? (
                <ReadingPane
                    session={session}
                    transport={transport}
                    storedEmailId={storedEmailId}
                    online={online}
                    onShowFullHtml={onShowFullHtml}
                    arriving={arriving}
                />
            ) : (
                <Thread
                    key={conversationKey(conversation)}
                    session={session}
                    transport={transport}
                    conversation={conversation}
                    online={online}
                    expandWholeThread={expandWholeThread}
                />
            )}

            {opened === null ? null : (
                <SurfaceWindow label={opened.label} drawn={opened.drawn} onClosed={opened.onClosed}>
                    {opened.draw}
                </SurfaceWindow>
            )}
        </>
    );
}

// The screen in front of the frame, which carries the theme and the language controls itself: they belong to somebody
// who has not signed in yet exactly as much as to somebody who has, and the frame that usually holds them is not on the
// screen at this point.
function SignInScreen({
    adopted,
    refusal,
    clearTextPermitted,
    lifetime,
    notices,
    send,
    onSignedIn,
    onPointSomewhereElse,
}: {
    readonly adopted: AdoptedDeployment | null;
    readonly refusal: ConfigurationRefusal | null;
    readonly clearTextPermitted: boolean | null;
    readonly lifetime: CredentialStore['lifetime'];
    readonly notices: readonly CredentialNotice[];
    readonly send: DeploymentTransport;
    readonly onSignedIn: (reached: DeploymentAddress, session: KeptSession) => void;
    readonly onPointSomewhereElse: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex min-h-dvh flex-col bg-page pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left split:flex-row">
            {/* The brand half. Above the split it is a column standing beside the form and carrying the claim; below
                it the claim goes and what is left is a strip naming the product, because a narrow window's room
                belongs to the form somebody came here to fill rather than to a sentence about it. */}
            <aside className="flex shrink-0 items-center border-b border-line bg-rail px-4.5 pt-4.5 pb-3 workspace:px-8.5 workspace:pt-5.5 workspace:pb-3.5 split:basis-sign-in-brand split:flex-col split:items-start split:justify-start split:gap-10 split:border-e split:border-b-0 split:px-11.5 split:py-11">
                {/* The product's name is the screen's heading at every width, rather than the claim beneath it: the
                    claim is the half a narrow window drops, and a heading that disappears with the composition would
                    leave the form's own `h2` as the first heading on the page below the split. What is decided by
                    width here is what is drawn, never what the document is made of. */}
                <div className="flex items-center gap-2.75 split:gap-3 split:self-start">
                    <BrandMark className="size-8.5 split:size-10" />
                    <h1 className="text-xl font-semibold tracking-tight split:text-2xl">{translate('shell.title')}</h1>
                </div>

                {/* Centred in what the brand above it leaves, rather than centred with it: the design stands the
                    product's name at the top of the column and the claim in the middle of the rest. */}
                <div className="hidden max-w-100 flex-col gap-4.5 pb-10 split:my-auto split:flex">
                    <p className="text-5xl font-semibold tracking-tight text-balance">{translate('signIn.claim')}</p>
                    <p className="text-lg text-muted text-pretty">{translate('signIn.claimExplanation')}</p>
                </div>
            </aside>

            <main className="flex flex-1 justify-center overflow-y-auto px-4.5 pt-5.5 pb-7 workspace:px-8.5 workspace:py-7.5 split:items-center split:px-11.5 split:py-11">
                <div className="flex w-full max-w-98 flex-col gap-4.5 workspace:gap-5.5">
                    {/* The version is not on this row and is at the foot of the form, which is where the design
                        project draws it. Off this row the two pickers fit one line at the narrowest width, which is
                        the composition the design draws. */}
                    <div className="flex flex-wrap items-center justify-end gap-3.5">
                        <ThemeChoice />
                        <LanguageChoice />
                    </div>

                    {/* A deployment that configured this client wrongly is said out loud rather than worked around.
                        Nothing else on this screen is drawn with it: every control under it is about a connection this
                        run has already been refused, and offering the form would invite a password against an address
                        the client will not use. */}
                    {refusal === null ? (
                        <SignIn
                            adopted={adopted}
                            clearTextPermitted={clearTextPermitted}
                            lifetime={lifetime}
                            notices={notices}
                            send={send}
                            onSignedIn={onSignedIn}
                            onPointSomewhereElse={onPointSomewhereElse}
                        />
                    ) : (
                        <ConfigurationRefused refusal={refusal} />
                    )}

                    {/* The foot of the form, under the refusal as well as under the form. The design draws a help line
                        on its left, and no deployment publishes an address to send somebody to yet, so the link stands
                        as the control it is — not built — rather than being dropped; the version on its right is one of
                        the first things asked of somebody reporting that a deployment will not take them, and is the
                        client's own alone here because no deployment has answered anything yet. */}
                    <div className="flex flex-wrap items-center justify-between gap-3">
                        <p className="flex min-h-12 items-center text-base text-muted workspace:min-h-6 workspace:text-sm">
                            {translate('signIn.forgotPassword')}&nbsp;
                            <PlannedControl label={translate('signIn.itHelp')} shape="link" />
                        </p>
                        <VersionLine deploymentVersion={null} className="text-2xs whitespace-nowrap text-faint" />
                    </div>
                </div>
            </main>
        </div>
    );
}

// What a deployment configured that this client will not connect to, in place of the form. Each of the four is a
// different mistake with a different repair, so each is a sentence of its own rather than one about configuration
// being wrong — the person reading it is often not the person who wrote the setting, and what they need is something
// exact enough to pass on.
//
// There is no way out of this screen from inside the client, and that is the state rather than an omission: the
// setting is on the machine rather than in the application, so what leaves it is an operator editing what they wrote.
// Saying which three places those are is what the sentence has to do instead of offering a control that would only
// pretend.
const configurationRefusals: Readonly<Record<ConfigurationRefusal, MessageKey>> = {
    addressMalformed: 'configuration.addressMalformed',
    addressNeedsClearTextPermission: 'configuration.addressNeedsClearTextPermission',
    clearTextContradictsAddress: 'configuration.clearTextContradictsAddress',
    permissionNotABoolean: 'configuration.permissionNotABoolean',
};

function ConfigurationRefused({ refusal }: { readonly refusal: ConfigurationRefusal }) {
    const { translate } = useLocalization();
    const refused = useRef<HTMLElement>(null);

    // This is drawn in place of the form somebody was on their way to filling, which is a view change like any other:
    // a keyboard left on the document would tab into whatever follows and never meet the sentence saying why there is
    // no form. The alert announces it to a screen reader; this is what puts anybody else at the start of it.
    useEffect(() => {
        refused.current?.focus();
    }, []);

    return (
        <section className="flex flex-col gap-3" ref={refused} role="alert" tabIndex={-1}>
            <h2 className="text-4xl font-semibold tracking-tight text-text">{translate('configuration.refused')}</h2>
            <p className="text-base text-text-soft">{translate(configurationRefusals[refusal])}</p>
            <p className="text-sm text-muted">{translate('configuration.whereItIsStated')}</p>
        </section>
    );
}

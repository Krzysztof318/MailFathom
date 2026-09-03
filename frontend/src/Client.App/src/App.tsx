// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';
import type { ClientSession, DeploymentAddress, MailFathomTransport } from '@mailfathom/client-backend';
import { BrandMark } from './controls/BrandMark';
import { SecondaryButton } from './controls/SecondaryButton';
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
import { TabStrip } from './mailSpace/TabStrip';
import { useOpenTabs } from './mailSpace/useOpenTabs';
import { MessageList } from './messageList/MessageList';
import { forgetListings } from './messageList/rememberedListings';
import { useClientPreferences } from './preferences/useClientPreferences';
import { useOwnProfile } from './profile/useOwnProfile';
import { ReadMarkingProvider } from './readMarking/ReadMarking';
import { ReadingPane } from './readingPane/ReadingPane';
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
import { useWideEnoughForTabs } from './shell/useWideWorkspace';
import { userNameIn } from './signIn/credentialEntry';
import { CredentialNotices, type CredentialNotice } from './signIn/CredentialNotices';
import type { CredentialStore } from './signIn/credentialStore';
import { SignIn } from './signIn/SignIn';
import { useTelemetry } from './telemetry/clientTelemetry';
import { useNavigationTelemetry } from './telemetry/navigationTelemetry';
import { Thread } from './thread/Thread';
import { conversationKey, type OpenConversation } from './workspace/openConversation';
import { scopeKey } from './workspace/mailScope';
import { emptyWorkspace, useWorkspace } from './workspace/useWorkspace';

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
}: {
    readonly deployment: ClientDeployment;
    readonly signedInWith: string | null;
    readonly credentials: CredentialStore;
    readonly send: DeploymentTransport;

    /** How the picture the signed-in person is drawn by is read and written, octets not being what a transport speaks. */
    readonly portraits: PortraitExchange;
}) {
    const { workspace, revise } = useWorkspace();
    const telemetry = useTelemetry();
    const [adopted, setAdopted] = useState(deployment.outcome === 'resolved' ? deployment.adopted : null);
    const [authorization, setAuthorization] = useState(signedInWith);
    const [notices, setNotices] = useState<readonly CredentialNotice[]>([]);
    const baseAddress = adopted === null ? null : adopted.deployment.baseAddress;
    const workspaceRegion = useRef<HTMLDivElement>(null);
    const focusedFor = useRef(authorization);

    // Built once per address and credential rather than per render, because it is what the message read below depends
    // on: a fresh object every render would restart that read every render.
    const session = useMemo(
        () => (baseAddress === null || authorization === null ? null : { baseAddress, authorization }),
        [baseAddress, authorization],
    );

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
        if (authorization === focusedFor.current) {
            return;
        }

        focusedFor.current = authorization;

        if (authorization !== null) {
            workspaceRegion.current?.focus();
        }
    }, [authorization]);

    // A credential the deployment has stopped accepting is acted on once rather than left to produce the same refusal
    // on every later read, which is why this is the one failure the frame does not render. What was kept goes with it:
    // a stored password the service refuses is a password nothing will make work again.
    //
    // It is held steady across renders because the connection below reads again whenever it changes, and a callback
    // rebuilt every render would be a read started every render.
    const credentialRefused = useCallback(() => {
        telemetry.happened('credential_no_longer_accepted');
        setNotices(['credentialNoLongerAccepted']);
        setAuthorization(null);
        revise(emptyWorkspace);
        forgetListings();

        if (baseAddress === null) {
            return;
        }

        void credentials.forget({ baseAddress }).then((removed) => {
            if (!removed) {
                setNotices((shown) => [...shown, 'passwordNotRemoved']);
            }
        });
    }, [baseAddress, credentials, revise, telemetry]);

    // What the deployment says is read from the address and the credential rather than held beside them, which is what
    // makes a credential unable to outlive the deployment it was presented to: pointing the client somewhere else, or
    // signing out, runs this again with nothing to present, and nothing of the previous one's answers survives it.
    const connection = useConnection(baseAddress, authorization, send, credentialRefused);

    const deploymentSession = connection.session?.outcome === 'read' ? connection.session.value : null;
    const offeredSpaces = deploymentSession === null ? [] : spacesOffered(deploymentSession);
    const space = useSpace(offeredSpaces);
    useNavigationTelemetry(space);
    const withheld = deploymentSession === null ? [] : withheldFrom(deploymentSession);
    const mailAccounts = connection.accounts?.outcome === 'read' ? connection.accounts.value.accounts : [];
    const readsMail = deploymentSession !== null && offers(deploymentSession, 'readMail');

    // The settings that follow the person rather than this machine. They are read here rather than in the menu that
    // shows them because two of them decide what opening a message does, which is the frame's rather than a menu's —
    // the tab mode, whose other half lands in #1494 and will already be here, and whether opening one marks it read.
    //
    // Asked for on the same three conditions the mail itself is: somebody signed in, a machine with a network, and a
    // credential the deployment lets read. The route is admitted under the grant a reader already holds, so a
    // credential without it would meet a refusal the screen has nothing to do about, and a machine with no network
    // would meet nothing at all.
    const preferences = useClientPreferences(
        readsMail && connection.online ? session : null,
        readMail,
        userNameIn(authorization),
    );

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
    const sessionReported = useRef<ClientSession | null>(null);

    useEffect(() => {
        const stop = telemetry.exportFor(session, telemetryPermitted);

        if (session !== null && telemetryPermitted && sessionReported.current !== session) {
            sessionReported.current = session;
            telemetry.happened('session_started');
        }

        return stop;
    }, [session, telemetry, telemetryPermitted]);

    // Whether opening a message marks it read on the person's own mail server, which is the frame's answer rather than
    // a screen's for the reason ADR 0026 gives about the two halves of it: the reader's own setting says what they want
    // of every account they read, and the grant says whether this credential may write a flag at all. Either missing is
    // the same client — one that draws what the mail server last reported and marks nothing.
    const markingRead =
        preferences.markReadOnOpen && deploymentSession !== null && offers(deploymentSession, 'markMailRead');

    // Whether the person is working in tabs, which is the two halves of that question and nothing else: what they set,
    // and a window with room for a row of tabs above the columns. Below that width the mode is inert rather than off —
    // the switch stays in the menu and says so — so narrowing returns the pane layout and widening returns the strip.
    const wideEnoughForTabs = useWideEnoughForTabs();
    const inTabs = preferences.openMailInTabs && wideEnoughForTabs;
    const openTabs = useOpenTabs(inTabs);

    // Who the person is, asked on the same three conditions and for the same reasons. It is read here rather than in
    // the menu that shows it because the settings screen behind that menu writes it, and two reads made separately
    // would disagree the moment one of them did.
    const profile = useOwnProfile(readsMail && connection.online ? session : null, readMail, portraits);

    function signedIn(reached: DeploymentAddress, presented: string): void {
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

        // The screen has already said how long the password will be kept, so a store that refused the write says so
        // rather than leaving somebody to discover it by being asked for the password again at the next start. This
        // one is read inside the frame: signing in worked, and what failed is only the keeping.
        void credentials.keep(reached, presented).then((stored) => {
            if (!stored) {
                setNotices(['passwordNotKept']);
            }
        });
        setAuthorization(presented);
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
        setAuthorization(null);
        revise(emptyWorkspace);
        forgetListings();

        if (adopted !== null) {
            void credentials.forget(adopted.deployment).then((removed) => {
                if (!removed) {
                    setNotices(['passwordNotRemoved']);
                }
            });
        }
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

        return (
            <OpenMail
                session={session}
                transport={readMail}
                conversation={workspace.conversation}
                fullHtml={workspace.fullHtml}
                storedEmailId={workspace.selection}
                online={connection.online}
                expandWholeThread={preferences.expandWholeThread}
                onShowFullHtml={openTabs.openFullHtml}
                onCloseFullHtml={openTabs.closeFullHtml}
            />
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
        // Above the frame rather than inside a space, because three unrelated places below read what has been marked:
        // the row that draws a message, the folder tree that counts unread mail, and the body that marks one on being
        // drawn. What it holds goes with the credential, exactly as the workspace does.
        <ReadMarkingProvider session={session} transport={readMail} marking={markingRead}>
            <div className="flex h-dvh flex-col bg-rail pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left workspace:flex-row">
                <div ref={workspaceRegion} tabIndex={-1} className="flex min-h-0 min-w-0 flex-1 flex-col bg-page">
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
                            // Who is signed in, taken apart in the one module that composes a credential and never here.
                            // A screen never sees the credential; what it is handed is the name the deployment knows the
                            // person by, which is what a preference kept per person on this machine is written under.
                            person={userNameIn(authorization)}
                            // Asking is what the field is for, so a credential that may not ask is not shown one. It is
                            // absent rather than disabled: a control nobody can use says less about why than the sentence
                            // above does. Where it stands is the space's decision, which is why it is handed in rather
                            // than drawn here.
                            intent={
                                deploymentSession !== null && offers(deploymentSession, 'askMail') ? (
                                    <IntentField accounts={mailAccounts} />
                                ) : null
                            }
                            status={<ConnectionSummary connection={connection} />}
                            folders={
                                session === null || !readsMail ? null : (
                                    <FolderTree session={session} transport={readMail} online={connection.online} />
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
                            mail={whatIsOpen()}
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
                    account={
                        <AccountMenu
                            accounts={mailAccounts}
                            deploymentVersion={deploymentSession?.version ?? null}
                            readingFrom={adopted?.origin === 'chosen' ? baseAddress : null}
                            telemetryForwarding={telemetryForwardedBy(deploymentSession, baseAddress)}
                            preferences={preferences}
                            profile={profile}
                            onPointSomewhereElse={pointSomewhereElse}
                            onSignOut={signOut}
                        />
                    }
                />
            </div>
        </ReadMarkingProvider>
    );
}

// What is being read on the right of the mail space: one message, the conversation it belongs to, or the sender's own
// markup — the last two standing in front of the message rather than instead of it, which is why the message they were
// opened from is still what this component is holding. Closing either draws it again with nothing having had to
// remember it.
//
// The markup surface is in front of the conversation as well as of the message, because it is opened from a message's
// head and returns to whatever that head was drawn in.
//
// Keyed by the conversation together with the message it was opened at, because what a conversation opens with is
// decided once from what it holds then: opening the same conversation at another message is a screen of its own rather
// than the same one adjusted.
function OpenMail({
    session,
    transport,
    conversation,
    fullHtml,
    storedEmailId,
    online,
    expandWholeThread,
    onShowFullHtml,
    onCloseFullHtml,
}: {
    readonly session: ClientSession;
    readonly transport: MailFathomTransport;
    readonly conversation: OpenConversation | null;
    readonly fullHtml: string | null;
    readonly storedEmailId: string | null;
    readonly online: boolean;
    readonly onShowFullHtml: (storedEmailId: string, subject: string | null) => void;
    readonly onCloseFullHtml: () => void;

    /** Whether the reader asked for conversations to open with every message drawn, which only the conversation reads. */
    readonly expandWholeThread: boolean;
}) {
    // Whether the pane below is being arrived at rather than landed on. Closing whatever stood in front of the message
    // swaps this position from one component to the other, so the pane mounts afresh exactly as it does on a cold start
    // and cannot tell the two apart from anything it holds itself — this is the only place that saw the surface go.
    // Adjusted during render, which is React's answer to state that a changed prop invalidates, rather than read from a
    // ref written during one: a ref would be written twice under StrictMode and the second pass would report that
    // nothing had been in front.
    //
    // The question is *whether something was in front* rather than which of the two it was, so the conversation and the
    // markup surface are one value here. Asking it per surface is how closing the second one would leave focus on a
    // control that has just been unmounted, while closing the first placed it correctly.
    const covered = conversation !== null || fullHtml !== null;
    const [wasCovered, setWasCovered] = useState(covered);
    const [arriving, setArriving] = useState(false);

    if (wasCovered !== covered) {
        setWasCovered(covered);
        setArriving(!covered);
    }

    if (fullHtml !== null) {
        return (
            <FullHtmlSurface
                key={fullHtml}
                session={session}
                transport={transport}
                storedEmailId={fullHtml}
                online={online}
                onClose={onCloseFullHtml}
            />
        );
    }

    return conversation === null ? (
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
    readonly onSignedIn: (reached: DeploymentAddress, authorization: string) => void;
    readonly onPointSomewhereElse: () => void;
}) {
    const { translate } = useLocalization();

    return (
        <div className="flex min-h-dvh flex-col bg-page pt-safe-top pr-safe-right pb-safe-bottom pl-safe-left split:flex-row">
            {/* The brand half. Above the split it is a column standing beside the form and carrying the claim; below
                it the claim goes and what is left is a strip naming the product, because a narrow window's room
                belongs to the form somebody came here to fill rather than to a sentence about it. */}
            <aside className="flex shrink-0 items-center gap-3 border-b border-line bg-rail px-4 py-4 split:basis-2/5 split:flex-col split:items-start split:justify-start split:gap-10 split:py-12 split:border-e split:border-b-0 split:px-12">
                {/* The product's name is the screen's heading at every width, rather than the claim beneath it: the
                    claim is the half a narrow window drops, and a heading that disappears with the composition would
                    leave the form's own `h2` as the first heading on the page below the split. What is decided by
                    width here is what is drawn, never what the document is made of. */}
                <div className="flex items-center gap-3 split:self-start">
                    <BrandMark className="size-9 split:size-10" />
                    <h1 className="text-2xl font-semibold tracking-tight">{translate('shell.title')}</h1>
                </div>

                {/* Centred in what the brand above it leaves, rather than centred with it: the design stands the
                    product's name at the top of the column and the claim in the middle of the rest. */}
                <div className="hidden max-w-sm flex-col gap-4 split:my-auto split:flex">
                    <p className="text-5xl font-semibold tracking-tight text-balance">{translate('signIn.claim')}</p>
                    <p className="text-lg text-muted text-pretty">{translate('signIn.claimExplanation')}</p>
                </div>
            </aside>

            <main className="flex flex-1 justify-center overflow-y-auto px-4 py-8 split:items-center split:px-12">
                <div className="flex w-full max-w-sm flex-col gap-6">
                    {/* The client's version is not here: the design draws neither, and the account menu already
                        reports it beside the deployment's own once there is a session to read it against — which is
                        where somebody comparing the two would look. Off this row the two pickers fit one line at the
                        narrowest width, which is the composition the design draws. */}
                    <div className="flex flex-wrap items-center justify-end gap-3.5">
                        <ThemeChoice />
                        <LanguageChoice />
                    </div>

                    {/* A deployment that configured this client wrongly is said out loud rather than worked around.
                        Nothing else on this screen is drawn with it: every control under it is about a connection this
                        run has already been refused, and offering the form would invite a password against an address
                        the client will not use. */}
                    {refusal === null ? (
                        <>
                            {/* The way out of an address somebody named themselves, offered here rather than only
                                inside the frame: a deployment that stopped accepting the credential, or one whose
                                password is gone, leaves a person on this screen with no address field to correct —
                                and a chosen address is read back out of storage on every later start, so reloading
                                returns to the same one. */}
                            {adopted?.origin === 'chosen' ? (
                                <ChosenDeployment
                                    address={adopted.deployment.baseAddress}
                                    onChange={onPointSomewhereElse}
                                />
                            ) : null}

                            <SignIn
                                adopted={adopted}
                                clearTextPermitted={clearTextPermitted}
                                lifetime={lifetime}
                                notices={notices}
                                send={send}
                                onSignedIn={onSignedIn}
                            />
                        </>
                    ) : (
                        <ConfigurationRefused refusal={refusal} />
                    )}
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

// Offered only where somebody named the deployment themselves. An origin that served the client is not something
// changing an address could move, so a client served by its own deployment is not asked to be pointed anywhere.
function ChosenDeployment({ address, onChange }: { readonly address: string; readonly onChange: () => void }) {
    const { translate } = useLocalization();

    return (
        <p className="flex items-center gap-2 text-sm text-muted">
            {translate('deployment.reachedAt', { address })}
            <SecondaryButton label={translate('deployment.change')} onActivate={onChange} />
        </p>
    );
}

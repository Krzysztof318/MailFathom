// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import {
    ownProviderName,
    reachDeployment,
    readProtectedResource,
    readSignInMethods,
    resolveDeploymentEntry,
    signIn,
    type ClientFailureReason,
    type DeploymentAddress,
    type DeploymentEntryRefusal,
    type DeploymentEntryResult,
    type ProtectedResource,
    type SignInAuthorizationServer,
    type SignInMethods,
    type SignInRefusal,
} from '@mailfathom/client-backend';
import { Icon } from '../controls/Icon';
import { SecondaryButton } from '../controls/SecondaryButton';
import type { AdoptedDeployment } from '../deployment/adoptedDeployment';
import type { DeploymentTransport } from '../deployment/sendToDeployment';
import type { MessageKey } from '../localization/en';
import { useLocalization } from '../localization/useLocalization';
import { useTelemetry } from '../telemetry/clientTelemetry';
import { AdvancedConnection } from './AdvancedConnection';
import { defaultPortOf, portForPermission, portOf, resolveConnection, type ResolvedConnection } from './connection';
import { resolveCredentialEntry, resolveSessionCredential, type CredentialEntryRefusal } from './credentialEntry';
import type { KeptSession } from './keptSession';
import { CredentialNotices, type CredentialNotice } from './CredentialNotices';
import { offersMoreThanTheTab, type KeptBeyondTheTab } from './credentialStore';
import {
    completeOAuthSignIn,
    discardWrittenAttempt,
    startOAuthSignIn,
    type OAuthSignInOutcome,
    type OAuthSignInRefusal,
} from './oauthFlow';
import type { OAuthGrant } from './oauthGrant';
import { ProviderMark } from './ProviderMark';
import type { SignInRedirect, SignInRedirectAnswer } from '../shellOperations/signInRedirect';

// The screen somebody meets before any mail: it collects the credential, and the address beside it wherever nothing has
// already said where the deployment is. Those are one form rather than two screens because a person was handed all four
// values together, and splitting them would make the first half read as configuration.
//
// Which shape is rendered follows from where the address came from, not from which head this is. Three answers, and
// each draws the address differently:
//
// - **Nobody has said.** The field is asked for and is editable, and the `Advanced` disclosure beside it holds the
//   permission an unsecured connection needs and what the entry resolved to.
// - **A deployment configured it, the origin served the client, or the person named it on an earlier run.** The
//   address is named under the title beside the lock that says what the password will cross, which is where the design
//   draws it, and the disclosure below the form reads it back row by row. Only a chosen address can be changed, and
//   the disclosure is where that is offered; a configured one arrived with its permission, so nothing here offers a
//   decision that has already been taken.
//
// What is decided here is only what a person sees; the address rule, the precedence between configuration sources, and
// the credential's own encoding each belong to the module that owns them.

/** Everything this screen can be stopped by, whether it was decided here, by the deployment, or by the wire. */
type SignInScreenRefusal =
    | DeploymentEntryRefusal
    | CredentialEntryRefusal
    | SignInRefusal
    | OAuthSignInRefusal
    | ClientFailureReason

    /**
     * The one refusal this screen names itself, because no other reader of `unavailable` needs it separated.
     *
     * An OAuth `unavailable` is the *authorization server* not answering, and `shownFor` below folds every other
     * `unavailable` into a sentence about the deployment — which would point whoever reads it, and whoever they go and
     * ask, at the wrong system entirely.
     */
    | 'providerUnavailable';

/** Which controls a refusal is about, so the fields that have to change are the ones marked as needing it. */
type RefusedControl = 'address' | 'userName' | 'password';

interface Refusal {
    readonly message: MessageKey;

    /**
     * The controls this refusal is about, and only those.
     *
     * A refusal about the user name alone announces the password valid, because telling somebody that a field they
     * filled in correctly is wrong is worse than saying nothing about it. A refusal about neither — the deployment's
     * own configuration, or the wire — marks nothing at all.
     */
    readonly controls: readonly RefusedControl[];
}

// One lookup, exhaustive by its own type, so a refusal added to any of the four sets fails to compile here until this
// screen says what it reads as and which controls it belongs to.
const refusals: Readonly<Record<SignInScreenRefusal, Refusal>> = {
    blank: { message: 'connect.blank', controls: ['address'] },
    malformed: { message: 'connect.malformed', controls: ['address'] },
    clearTextRefused: { message: 'connect.clearTextRefused', controls: ['address'] },

    incomplete: { message: 'signIn.incomplete', controls: ['userName', 'password'] },
    userNameHasColon: { message: 'signIn.userNameHasColon', controls: ['userName'] },
    tooLong: { message: 'signIn.tooLong', controls: ['userName', 'password'] },

    credentialRefused: { message: 'signIn.credentialRefused', controls: ['userName', 'password'] },
    basicNotOffered: { message: 'signIn.basicNotOffered', controls: [] },

    // The four an authorization server sign-in can end at. None of them marks a control: nothing on this form was
    // typed for one, and the way out of each is starting it again rather than correcting a field.
    notAuthorized: { message: 'signIn.notAuthorized', controls: [] },
    unexpectedAnswer: { message: 'signIn.unexpectedAnswer', controls: [] },
    refused: { message: 'signIn.providerRefused', controls: [] },
    notAUser: { message: 'signIn.notAUser', controls: [] },
    providerUnavailable: { message: 'signIn.providerSilent', controls: [] },

    // `unauthenticated` is the answer `credentialRefused` already is — a 401 whose challenge did prove MailFathom
    // wrote it, a 401 that did not being an unreadable answer instead — so it reads as a refused credential and marks
    // both halves of one. `unauthorized` is the one that says something about the grant rather than about the
    // password, and it marks nothing: retyping a password changes nothing about a permission a user is missing.
    // Neither is reached from here — `signIn` and `reachDeployment` return neither — and both are named because the
    // failure set is closed by its own type rather than because this screen expects either.
    unauthenticated: { message: 'signIn.credentialRefused', controls: ['userName', 'password'] },
    unauthorized: { message: 'signIn.grantMissing', controls: [] },
    unavailable: { message: 'connect.unavailable', controls: [] },
    unreadable: { message: 'connect.unreadable', controls: [] },

    // `missing` is named for the same reason the two above it are and is reached from here even less: it is produced
    // by the one route that names a single message, which nothing on this screen calls. It reads as a deployment that
    // did not answer, because at this point that is what a reader would have to act on — an address to check and a
    // deployment to start — and nothing here has named a thing that could have gone.
    missing: { message: 'connect.unavailable', controls: [] },
};

// What "nothing answered" reads as where there is no address on the screen. `connect.unavailable` asks somebody to
// check an address and check that the deployment is running, and neither half is theirs to act on when the origin that
// served this page is the deployment — so the same outcome takes the sentence that fits the shape it is rendered in.
const silentDeployment: Refusal = { message: 'signIn.deploymentSilent', controls: [] };

// What a store that keeps nothing beyond the tab has to say for itself, which is the sentence a person on a shared
// machine is deciding from. The two places that do keep something say it under the checkbox instead, in the design
// project's own words, because there the sentence is about a choice being made rather than about a limit being met.
const nothingKeptMessages: Readonly<Record<KeptBeyondTheTab, MessageKey>> = {
    inTheDeviceStore: 'signIn.keptOnThisDevice',
    inThisBrowser: 'signIn.keptInThisBrowser',
    nowhereTheShellKeepsTheRun: 'signIn.keptUntilTheClientCloses',
    nowhereStorageUnreachable: 'signIn.notKeptStorageUnreachable',
    nowhereKeyInvalidated: 'signIn.notKeptKeyInvalidated',
};

// One shape for every field on this screen, stated once. The focus treatment is the design project's — the line goes
// to the accent and the tint widens behind it — and it is written with `focus-within` on the box rather than on the
// input, because the box is what the reveal control and the port hint stand inside.
//
// Everything that changes below the workspace breakpoint is the same decision made twice: a control a finger has to
// hit is taller than one a pointer has to hit, and a field a phone keyboard would zoom the page into is one set below
// the size that browser treats as readable. So the generous size is the base and the tighter one is the variant, which
// is the direction a mobile-first breakpoint reads in. It is the same width the workspace stops taking a phone's shape
// at, which is why this screen names that breakpoint rather than one of its own.
const fieldBox =
    'flex items-center gap-2.5 rounded-xl border border-line-strong bg-panel px-3.25 transition focus-within:border-accent focus-within:ring-3 focus-within:ring-accent-soft';

const fieldInput =
    'min-h-13 min-w-0 flex-1 bg-transparent text-xl text-text outline-none workspace:min-h-11 workspace:text-md';

const fieldLabel = 'text-sm font-medium text-text-soft';

/**
 * How long a typed address is left alone before the two documents are read for it.
 *
 * Every keystroke that happens to resolve to an address is otherwise a request to a host somebody is halfway through
 * naming, which is both noise and a disclosure. An address this screen was handed waits for none of this: it was
 * settled before the screen was drawn.
 */
const typedAddressSettles = 600;

/** What a deployment published about signing in, and which address it was published for. */
interface Published {
    readonly address: string;
    readonly methods: SignInMethods | null;
    readonly resource: ProtectedResource | null;
}

/** What this screen is in the middle of, where somebody has been handed to an authorization server or is back from one. */
type HandingOver =
    | { readonly stage: 'handingOff'; readonly provider: string }

    /** A redirect was waiting when this run started, and the code in it is being redeemed. */
    | { readonly stage: 'returning' };

export function SignIn({
    adopted,
    clearTextPermitted: configuredClearText,
    beyondTheTab,
    notices,
    redirect,
    redirectAnswer,
    send,
    onSignedIn,
    onSignedInWithGrant,
    onPointSomewhereElse,
}: {
    readonly adopted: AdoptedDeployment | null;

    /** The clear-text permission a deployment configured, or `null` where it configured none. */
    readonly clearTextPermitted: boolean | null;

    /** Where a session somebody asks to keep would go, which decides whether the choice is offered at all. */
    readonly beyondTheTab: KeptBeyondTheTab;

    readonly notices: readonly CredentialNotice[];

    /** How somebody is handed to an authorization server and how the answer comes back, resolved at the composition root. */
    readonly redirect: SignInRedirect;

    /** The answer that was already waiting when this run started, which is the web head's whole way of answering. */
    readonly redirectAnswer: SignInRedirectAnswer | null;

    readonly send: DeploymentTransport;
    readonly onSignedIn: (deployment: DeploymentAddress, session: KeptSession, keptBeyondTheTab: boolean) => void;

    /** Signing in through an authorization server, which produces a grant rather than a session the deployment minted. */
    readonly onSignedInWithGrant: (deployment: DeploymentAddress, grant: OAuthGrant) => void;

    /** Pointing away from an address somebody named themselves, which this screen offers inside its disclosure. */
    readonly onPointSomewhereElse: () => void;
}) {
    const { translate } = useLocalization();
    const telemetry = useTelemetry();
    const deployment = adopted === null ? null : adopted.deployment;
    const [entry, setEntry] = useState('');

    // Seeded from what a deployment configured, and left alone afterwards where it did: nothing this screen holds is
    // written back to the device store, so removing the setting removes it from the screen on the next start.
    const [clearTextPermitted, setClearTextPermitted] = useState(configuredClearText ?? false);
    const [userName, setUserName] = useState('');
    const [password, setPassword] = useState('');
    const [revealed, setRevealed] = useState(false);

    // Unticked to begin with, and that is the decision rather than the default: a box already ticked would keep a
    // session on a machine somebody borrowed for one message, and the person who wants it kept is the one who is
    // there to tick it. ADR 0023 turns on nothing being kept durably that nobody asked for, and this is where it is
    // asked.
    const [keepSignedIn, setKeepSignedIn] = useState(false);
    const [presenting, setPresenting] = useState(false);

    // The address the attempt in flight was started against, which is the one case on this screen where a value is
    // held rather than computed: nothing on the form still says it. The fields stay editable while an attempt runs —
    // deliberately, so a person who sees their mistake can correct it without waiting — and a screen that re-derived
    // this from the entry would then name the address being typed while the request goes to the one that was.
    const [reaching, setReaching] = useState<ResolvedConnection | null>(null);
    const [refusal, setRefusal] = useState<SignInScreenRefusal | null>(null);

    // What the deployment published about signing in, kept beside the address it was published for rather than cleared
    // when the address changes: the pairing is what makes a stale answer unreadable instead of making it somebody
    // else's provider list, and it is read during render rather than synchronised by an effect.
    const [published, setPublished] = useState<Published | null>(null);

    // Seeded from the redirect rather than set by the effect that reads it, because a person coming back from an
    // authorization server must meet the wait on the first paint: the redemption starts in the same commit, and a
    // screen that drew the form first would be offering a sign-in to somebody who is already halfway through one.
    const [handing, setHanding] = useState<HandingOver | null>(redirectAnswer === null ? null : { stage: 'returning' });

    const address = useRef<HTMLInputElement>(null);
    const name = useRef<HTMLInputElement>(null);
    const submit = useRef<HTMLButtonElement>(null);
    const notified = useRef<HTMLDivElement>(null);
    const attempt = useRef<AbortController | null>(null);
    const started = useRef(false);

    /** How many times the two documents have been asked for, which is what a read asked for again keys the effect on. */
    const [reading, setReading] = useState(0);

    /** Which answer has already been redeemed, so a re-run of the effect below does not spend the code twice. */
    const redeeming = useRef<SignInRedirectAnswer | null>(null);

    /** How that redemption reports, read when the answer arrives so that no dependency re-enters the redemption. */
    const reporting = useRef<(outcome: OAuthSignInOutcome) => void>(() => undefined);

    // The view changed, so focus is placed rather than left wherever the previous screen had it: on what this screen
    // has to say about why it is back where there is something, and otherwise on the first thing the form is asking to
    // have filled. Moving focus is an imperative browser API, which is what an effect is for.
    //
    // It runs again when the deployment changes, because that is the other view change this screen has: pointing away
    // from a chosen address puts an address field on a form that had none, and this screen is often already standing
    // when that happens — somebody signs out of the frame first and points elsewhere from here. Leaving focus in the
    // login field would leave them typing a password against a deployment they have just abandoned.
    useEffect(() => {
        (notified.current ?? address.current ?? name.current)?.focus();
    }, [deployment]);

    // An attempt is answered for the deployment it was started against, so one whose deployment was abandoned while it
    // ran is called off rather than allowed to answer: the way out of a chosen address sits above this form and stays
    // live while an attempt runs, and a client that signed somebody in here would write the credential back into the
    // store that was just asked to clear it, at the address they had pointed away from.
    useEffect(() => {
        return () => {
            attempt.current?.abort();
        };
    }, [deployment]);

    // An attempt disables the control that started it, and the browser drops focus to the document when it does — so
    // an attempt that ends leaves the refusal announced with focus nowhere and somebody reading by keyboard tabbing in
    // from the top of the page. Focus goes back to that control, and it is placed here rather than where the attempt
    // ended because a disabled element cannot take focus: this runs after the render that re-enabled it.
    //
    // The ref is what separates an attempt that ended from the first render, and it survives `StrictMode` invoking
    // this twice because both invocations read the same `presenting`.
    useEffect(() => {
        if (presenting) {
            started.current = true;

            return;
        }

        if (started.current) {
            submit.current?.focus();
        }
    }, [presenting]);

    // Which deployment this screen is composed for, which is the one it was handed or the one being typed once what
    // has been typed is an address at all. A typed one counts because the controls a deployment offers have to be
    // composed before a credential is presented rather than after one was accepted: every client nobody handed an
    // address to, and everybody who used *Change the server*, reaches this screen with nothing adopted, and a
    // deployment that takes no password and publishes its own provider would otherwise be unreachable from there.
    const named = deployment === null ? resolveDeploymentEntry(entry, clearTextPermitted) : null;
    const asked: DeploymentAddress | null = deployment ?? (named?.outcome === 'resolved' ? named.deployment : null);
    const askedAddress = asked?.baseAddress ?? null;

    /** Whether the address was handed to this screen rather than typed into it, which is what decides it waits. */
    const handed = deployment !== null;

    // What the deployment says it offers, asked as soon as there is a deployment to ask and carrying no credential.
    // Both documents are read together because a screen needs both before it draws a provider control at all — one
    // says which servers are offered and the other what a token has to be issued for — and a deployment answering
    // neither is an older one that still signs somebody in with a password.
    useEffect(() => {
        if (askedAddress === null) {
            return;
        }

        const running = new AbortController();

        const ask = (): void => {
            const at = { baseAddress: askedAddress };
            const asking = send(running.signal);

            void Promise.all([readSignInMethods(at, asking), readProtectedResource(at, asking)]).then(
                ([methods, resource]) => {
                    if (running.signal.aborted) {
                        return;
                    }

                    setPublished({
                        address: askedAddress,
                        methods: methods.outcome === 'read' ? methods.value : null,
                        resource: resource.outcome === 'read' ? resource.value : null,
                    });
                },
            );
        };

        if (handed) {
            ask();

            return () => {
                running.abort();
            };
        }

        const settling = window.setTimeout(ask, typedAddressSettles);

        return () => {
            window.clearTimeout(settling);
            running.abort();
        };
    }, [askedAddress, handed, reading, send]);

    // What an OAuth sign-in ended as, in one place, because the two halves reach it from different runs of the client:
    // the shell head comes back out of the control that started it, and the web head out of a redirect that was
    // already waiting when this run began.
    const settleOAuth = useCallback(
        (outcome: OAuthSignInOutcome): void => {
            setHanding(null);

            if (outcome.outcome === 'refused') {
                // An authorization server that did not answer is said as that rather than as the deployment's silence,
                // which is what every other `unavailable` on this screen reads as. A failure at the provider reported
                // as a failure at MailFathom sends whoever reads it, and whoever they ask, to the wrong system.
                const refused = outcome.refusal === 'unavailable' ? 'providerUnavailable' : outcome.refusal;

                setRefusal(refused);
                telemetry.happened('sign_in_refused', { 'mailfathom.client.refusal': refused });

                return;
            }

            telemetry.happened('signed_in', {
                'mailfathom.client.kept': 'true',
                'mailfathom.client.method': 'authorizationServer',
            });

            onSignedInWithGrant(outcome.deployment, outcome.grant);
        },
        [onSignedInWithGrant, telemetry],
    );

    // A redirect that was waiting when this run started, redeemed exactly once for the answer it carried.
    //
    // Once, and not once per run of the effect: the verifier is taken from the tab's own storage as it is read, so a
    // second pass over the same answer has nothing left to redeem with and the code is spent for nothing. `StrictMode`
    // runs every effect, its cleanup, and the effect again on mount, and this component's own callbacks change
    // identity with the frame above it — so the ref is what makes the redemption one act rather than one per re-run,
    // and the outcome is settled through a ref of its own so that no dependency of this effect is a reason to re-enter
    // it. It is not abandonable for the same reason: an aborted redemption is an authorization code nothing can spend.
    useEffect(() => {
        reporting.current = settleOAuth;
    }, [settleOAuth]);

    useEffect(() => {
        if (redirectAnswer === null || redeeming.current === redirectAnswer) {
            return;
        }

        redeeming.current = redirectAnswer;

        void completeOAuthSignIn(redirectAnswer, send(new AbortController().signal)).then((outcome) => {
            reporting.current(outcome);
        });
    }, [redirectAnswer, send]);

    const shown = refusal === null ? null : shownFor(refusal, deployment);
    // Whether the choice between the tab and the device is a choice at all, which is what decides both that the
    // checkbox is drawn and that the sentence it replaces is not.
    const keptBeyondTheTabOffered = offersMoreThanTheTab(beyondTheTab);

    // What this deployment offers, read during render from what was published for the address on the screen. An answer
    // published for a different address is not read at all, and a deployment that published nothing is an older one —
    // which offers a password, because that is what every deployment before this route offered.
    const offered = published !== null && published.address === askedAddress ? published : null;
    const acceptsPassword = offered?.methods?.acceptsPassword ?? true;

    // What the deployment said it offers, which is read apart from what this head can actually draw a control for: the
    // two answer different questions, and the sentences below say so separately.
    const publishedServers = offered?.methods?.authorizationServers ?? [];

    // A server is offerable where three things hold at once: the deployment published it, the deployment said what a
    // token has to be issued for, and this head receives a redirect at all. The last is why the Android head draws no
    // provider control rather than one that goes nowhere.
    const servers: readonly SignInAuthorizationServer[] =
        redirect.offered && offered?.resource != null ? publishedServers : [];

    // The deployment's own is drawn as the one primary control above everything else, and every other server in the
    // grid below it. That is the design's split between `showSso` and `showOauth`, and what decides it is the reserved
    // name the configuration gives the operator's own provider.
    const own = servers.find((server) => server.name === ownProviderName) ?? null;
    const providers = servers.filter((server) => server.name !== ownProviderName);
    const resource = offered?.resource ?? null;

    // Nothing is offered at all, which is a deployment somebody has to go and configure rather than a screen to keep
    // trying on. It is read off what the deployment *published* rather than off what is drawable here, because the two
    // things that empty `servers` — a head that receives no redirect, and a resource document that could not be read —
    // say nothing whatever about what the deployment offers, and telling somebody to go and configure one that is
    // already configured names a fix nobody can make.
    const noMethods = offered?.methods != null && !acceptsPassword && publishedServers.length === 0;

    // The other half of that: the deployment published servers and this screen is drawing none of them. Which of the
    // two reasons it is decides the sentence, because one is permanent on this head and the other is worth reading
    // again — and whether a password form is standing under it decides which sentence, because one that names signing
    // in with a password here names a way out that is not on the screen.
    const withheldServers: MessageKey | null =
        publishedServers.length === 0 || servers.length > 0
            ? null
            : redirect.offered
              ? 'signIn.providersUnread'
              : acceptsPassword
                ? 'signIn.providersNotOnThisHead'
                : 'signIn.providersOnAnotherHeadOnly';

    /** Hands somebody to one authorization server, and settles whatever this head came back with. */
    async function handOver(server: SignInAuthorizationServer): Promise<void> {
        if (asked === null || resource === null) {
            return;
        }

        // Whatever was already in the slot goes first. A password attempt against a slow deployment is still running
        // when somebody presses a provider control, and replacing its controller unannounced would leave it
        // unabortable — with the control that abandons it gone from the screen in the same render.
        attempt.current?.abort();

        const running = new AbortController();
        attempt.current = running;

        // What a hand-over that was called off has to undo, hung on the signal for the reason the password attempt's
        // is: it holds however the attempt was abandoned, including a deployment changed underneath it while the
        // person was away in their browser for minutes. The shell head is holding its redirect port for the whole of
        // that wait, and the verifier written down for the attempt is a secret with the life of one sign-in.
        running.signal.addEventListener(
            'abort',
            () => {
                redirect.abandon();
                discardWrittenAttempt();

                if (attempt.current === running) {
                    attempt.current = null;
                    setHanding(null);
                }
            },
            { once: true },
        );

        setRefusal(null);
        setHanding({ stage: 'handingOff', provider: server.displayName });

        const outcome = await startOAuthSignIn({
            deployment: asked,
            server,
            resource,
            redirect,
            transport: send(running.signal),
        });

        // An abandoned hand-over has no answer, exactly as an abandoned password attempt has none: the screen is
        // already back where the person left it, and signing them in to a deployment they pointed away from would
        // write a grant into the store that was just asked to clear one.
        if (running.signal.aborted) {
            return;
        }

        attempt.current = null;
        settleOAuth(outcome);
    }

    // The password form stands down while a hand-over does. Somebody coming back from an authorization server meets
    // this screen while the code is still being redeemed, and a complete form with an enabled control on it is a second
    // sign-in they can start against the same deployment while the first one is in flight.
    const passwordOffered = acceptsPassword && handing === null;

    // The two credential fields are described by where the sign-in is kept, and that sentence is the checkbox's
    // own hint where the choice is offered and the standing paragraph where it is not. Naming an element that is
    // not in the document leaves an `aria-describedby` resolving to nothing, which is a field described by
    // silence rather than by the refusal that is on the screen.
    const describedBy = (hint: string): string => (shown === null ? hint : `sign-in-refusal ${hint}`);
    const marks = (control: RefusedControl): boolean => shown?.controls.includes(control) === true;

    async function present(): Promise<void> {
        const reached: DeploymentEntryResult =
            deployment === null
                ? resolveDeploymentEntry(entry, clearTextPermitted)
                : { outcome: 'resolved', deployment };

        if (reached.outcome === 'refused') {
            setRefusal(reached.refusal);

            return;
        }

        const credential = resolveCredentialEntry(userName, password);

        if (credential.outcome === 'refused') {
            setRefusal(credential.refusal);

            return;
        }

        const running = new AbortController();
        attempt.current = running;

        // Whatever called this attempt off — the person, or the deployment it was against being abandoned underneath
        // it — the form it disabled is what has to come back. Hanging that on the signal rather than on the caller is
        // what makes it hold for a wire that never answers at all, which no later step of this function would reach.
        running.signal.addEventListener(
            'abort',
            () => {
                if (attempt.current === running) {
                    attempt.current = null;
                    setPresenting(false);
                }
            },
            { once: true },
        );

        setRefusal(null);

        // Read back permitting clear text, the way the summary reads a handed address: what is being asked here is
        // what this attempt's address resolved *to*, and it only became one by having been resolved already.
        setReaching(resolveConnection(reached.deployment.baseAddress, true));
        setPresenting(true);

        // An abandoned attempt has no answer, whatever the wire eventually said: the screen is already back where the
        // person left it, and reporting a refusal here would be this attempt overwriting what they did next.
        //
        // Focus returns to the control that started the attempt, because starting one disabled that control and the
        // browser dropped focus to the document when it did. Without this the refusal is announced with focus nowhere,
        // and somebody reading by keyboard tabs in from the top of the page to reach the field it named.
        const stopped = (reason: SignInScreenRefusal): void => {
            if (running.signal.aborted) {
                return;
            }

            attempt.current = null;
            setPresenting(false);
            setRefusal(reason);

            // The one funnel every refusal this screen can reach passes through, which is why it is the only place a
            // sign-in is reported from. The refusal is this screen's own closed vocabulary rather than a status or a
            // sentence — an operator grouping by it sees a deployment refusing passwords apart from one nobody can
            // reach, and neither the address, the name, nor any part of what was typed is written down.
            //
            // Below the level a deployment keeps by default, because a person mistyping their password is not a
            // deployment's business and a fleet of them would be the loudest thing in its log. It is what an operator
            // lowers the floor for when somebody says they cannot get in.
            telemetry.happened('sign_in_refused', { 'mailfathom.client.refusal': reason });
        };

        // An address typed on this screen is asked what it is before it is handed a password, which is the whole
        // reason there are two requests here. An address this screen was handed is not, whichever way it arrived: an
        // origin that served the client is the deployment by definition, and an address somebody chose was asked this
        // question on the run they chose it — which is why the stored one is not asked again on every later start.
        if (deployment === null) {
            const greeting = await reachDeployment(reached.deployment, send(running.signal));

            if (running.signal.aborted) {
                return;
            }

            if (greeting.outcome === 'failed') {
                stopped(greeting.failure.reason);

                return;
            }

            if (!greeting.value.acceptsPassword) {
                stopped('basicNotOffered');

                return;
            }
        }

        const answer = await signIn(
            { baseAddress: reached.deployment.baseAddress, authorization: credential.authorization },
            send(running.signal),
        );

        if (running.signal.aborted) {
            return;
        }

        if (answer.outcome === 'failed') {
            stopped(answer.failure.reason);

            return;
        }

        if (!answer.value.signedIn) {
            stopped(answer.value.refusal);

            return;
        }

        attempt.current = null;
        setPresenting(false);

        // Whether the session will be kept beyond this tab, and nothing else: not the name, not the address, and not
        // the token. It is the one thing about a sign-in an operator cannot infer from the requests around it, and it
        // is what separates somebody who signs in every morning from somebody whose stored session keeps being refused.
        telemetry.happened('signed_in', { 'mailfathom.client.kept': String(keepSignedIn) });

        // The password is not handed on and is not kept anywhere: what the exchange answered with is a session, and
        // that is the whole of what this client holds from here. The name travels beside it because a token does not
        // carry one and the screens above ask who is signed in.
        onSignedIn(
            reached.deployment,
            {
                authorization: resolveSessionCredential(answer.value.session.token),
                expiresAt: answer.value.session.expiresAt,
                person: userName,
            },
            // Ticking a box a store cannot honour keeps nothing, so the answer is the choice and the store's ability to
            // act on it together rather than the checkbox alone. Where nothing is offered the box is not drawn either,
            // and this is the same statement read from the other end.
            keepSignedIn && offersMoreThanTheTab(beyondTheTab),
        );
    }

    // A deployment that accepts the connection and never answers would otherwise hold the screen on `signIn.presenting`
    // with the only control on it disabled, which is a state nobody can leave. Abandoning frees the connection as well
    // as the screen, which is why it aborts the request rather than only ignoring what it says. Focus goes back to the
    // control this one stood beside, placed by the effect above, since this one is about to leave the document.
    //
    // It abandons a hand-over on the same terms and through the same slot, which is why the provider block draws it
    // too: a hand-over waits on somebody's browser for minutes, and that is far longer than a deployment's silence.
    function abandon(): void {
        attempt.current?.abort();
    }

    // The address field stands on this form only while an address is being typed. One that arrived with the
    // deployment — the origin that served the client, a configuration, or a choice made on an earlier run — is named
    // under the title instead, beside the lock that says what the password will cross, which is where the design
    // draws it; the way to change a chosen one is inside the disclosure below the form.
    const shownAddress = deployment === null ? entry : undefined;

    // What the address on the screen resolves to, computed during render rather than held beside the entry: it is a
    // pure function of values this component already has, and a second piece of state kept in step with them is the
    // pair that eventually disagrees.
    //
    // A handed address is read back permitting clear text, for the reason a stored one is: it only became the
    // address this run uses by being resolved, and what is being asked here is what it resolved *to*.
    const connection =
        deployment === null
            ? resolveConnection(entry, clearTextPermitted)
            : resolveConnection(deployment.baseAddress, true);

    return (
        <section className="flex flex-col gap-4.5 workspace:gap-5">
            <div className="flex flex-col gap-1.25">
                <h2 className="text-4xl font-semibold tracking-tight text-text">{translate('signIn.title')}</h2>

                {/* Where the password is about to go, under the title: the lock says whether it goes over TLS and the
                    address says where. Only where the address arrived with the deployment — while one is being typed,
                    the field below is what names it. */}
                {deployment === null || connection === null ? null : (
                    <p className="flex flex-wrap items-center gap-1.75 text-sm text-muted">
                        <Icon
                            name={connection.secure ? 'lock' : 'lock_open'}
                            className={`size-3.75 ${connection.secure ? 'text-healthy-text' : 'text-warning-text'}`}
                        />
                        <span className="text-text-soft">{connection.authority}</span>
                        {connection.secure ? null : (
                            <span className="rounded-sm bg-warning-soft px-1.75 py-0.5 text-2xs font-medium text-warning-text">
                                {translate('connect.withoutTls')}
                            </span>
                        )}
                    </p>
                )}
            </div>

            <CredentialNotices notices={notices} ref={notified} />

            {/* The wait somebody coming back from an authorization server meets, in the place the answer will appear.
                Nothing else on this screen is drawn for it: the two documents have not been read yet at that moment,
                so there is no provider control standing to carry a spinner of its own. */}
            {handing?.stage === 'returning' ? (
                <p
                    className="flex items-center gap-2.25 rounded-lg border border-line bg-panel px-3.25 py-2.75 text-sm text-text-soft"
                    role="status"
                >
                    <Spinner />
                    {translate('signIn.returning')}
                </p>
            ) : null}

            {/* Each of the three is drawn from what the deployment published and from nothing else, which is what lets
                a deployment offering one of them draw one control rather than one control and two empty spaces. The
                deployment's own provider stands above everything, as the design draws it, and every other server is a
                row in the grid below. */}
            {own === null ? null : (
                <div className="flex flex-col gap-2">
                    <button
                        className="flex min-h-13 items-center justify-center gap-2 rounded-full border border-accent bg-accent px-4 text-lg font-semibold text-on-accent transition hover:brightness-107 disabled:opacity-85 workspace:min-h-11 workspace:rounded-xl workspace:text-md"
                        disabled={handing !== null}
                        type="button"
                        onClick={() => {
                            void handOver(own);
                        }}
                    >
                        {handing === null ? <Icon name="key" className="size-4.5" /> : <Spinner />}
                        <span className="min-w-0 truncate">
                            {handing?.stage === 'handingOff'
                                ? translate('signIn.openingProvider', { provider: handing.provider })
                                : translate('signIn.signInWithProvider')}
                        </span>
                        {handing === null ? <Icon name="open_in_new" className="size-4.25 opacity-75" /> : null}
                    </button>

                    <p className="text-xs leading-normal text-faint text-pretty">
                        {handing === null
                            ? translate('signIn.providerOpensInBrowser', { provider: own.displayName })
                            : translate('signIn.finishInBrowser')}
                    </p>
                </div>
            )}

            {/* The way out of a hand-over, which is the same control the password attempt draws and is here for the
                same reason: the shell head holds its redirect port for minutes, and somebody who closed the provider
                window or pressed the wrong provider would otherwise have nothing on the screen to press. */}
            {handing?.stage === 'handingOff' ? (
                <SecondaryButton label={translate('signIn.abandon')} shape="form" onActivate={abandon} />
            ) : null}

            {/* What is under the divider decides what it says: the grid where there is one, and the password form
                where the deployment's own server is the only one published. */}
            {own === null || (providers.length === 0 && !passwordOffered) ? null : (
                <Divider
                    label={translate(providers.length === 0 ? 'signIn.orWithPassword' : 'signIn.orAnotherMethod')}
                />
            )}

            {providers.length === 0 ? null : (
                <div className="flex flex-col gap-2.25">
                    <p className="text-xs font-medium tracking-wide text-faint">{translate('signIn.viaProvider')}</p>
                    <div className="grid grid-cols-3 gap-2">
                        {providers.map((provider) => (
                            <ProviderButton
                                key={provider.issuer}
                                busy={handing?.stage === 'handingOff' && handing.provider === provider.displayName}
                                disabled={handing !== null}
                                label={translate('signIn.continueToProvider', { provider: provider.displayName })}
                                provider={provider}
                                onActivate={() => {
                                    void handOver(provider);
                                }}
                            />
                        ))}
                    </div>
                </div>
            )}

            {providers.length === 0 || !passwordOffered ? null : <Divider label={translate('signIn.orWithPassword')} />}

            {noMethods ? <StatedNotice message={translate('signIn.noMethods')} /> : null}

            {/* What the deployment offers and this screen is not drawing, said as its own sentence: a person told that
                a configured deployment offers nothing has been pointed at a fix nobody can make. The document that
                could not be read is the one case worth another read, so that notice carries the control rather than
                asking somebody to produce the retry themselves. */}
            {withheldServers === null ? null : (
                <StatedNotice message={translate(withheldServers)}>
                    {withheldServers === 'signIn.providersUnread' ? (
                        <button
                            className="shrink-0 self-start rounded-md border border-warning px-2.25 py-1 font-medium text-warning-text transition hover:bg-warning/10"
                            type="button"
                            onClick={() => {
                                setReading((reads) => reads + 1);
                            }}
                        >
                            {translate('signIn.readAgain')}
                        </button>
                    ) : null}
                </StatedNotice>
            )}

            <form
                className="flex flex-col gap-4.5 workspace:gap-5"
                onSubmit={(event) => {
                    event.preventDefault();
                    void present();
                }}
            >
                {shownAddress === undefined ? null : (
                    <div className="flex flex-col gap-1.5">
                        <label className={fieldLabel} htmlFor="sign-in-address">
                            {translate('connect.address')}
                        </label>
                        <div className={fieldBox}>
                            <input
                                // The refusal joins the hint rather than replacing it, so somebody reading the field
                                // hears why it was refused and what it wants, in that order, without moving off it.
                                aria-describedby={describedBy('sign-in-address-hint')}
                                aria-invalid={marks('address')}
                                autoComplete="off"
                                className={fieldInput}
                                id="sign-in-address"
                                inputMode="url"
                                placeholder={translate('connect.addressExample')}
                                ref={address}
                                spellCheck={false}
                                type="text"
                                value={shownAddress}
                                onChange={(event) => {
                                    setEntry(event.target.value);
                                    setRefusal(null);
                                }}
                            />

                            {/* The port this will actually reach, said beside the field while it is being typed. Out
                                of the accessibility tree because the sentence under the field says the same thing in
                                words, and hearing a bare number after every keystroke is noise. */}
                            {connection === null ? null : (
                                <span aria-hidden="true" className="shrink-0 text-xs whitespace-nowrap text-faint">
                                    {translate('connect.portHint', { port: portOf(connection) })}
                                </span>
                            )}
                        </div>
                        <p className="text-xs text-muted" id="sign-in-address-hint">
                            {translate('connect.addressHint', {
                                port:
                                    connection === null
                                        ? portForPermission(clearTextPermitted)
                                        : defaultPortOf(connection),
                            })}
                        </p>
                    </div>
                )}

                {/* The password form, drawn only where the deployment said it takes one. Neither field carries a
                    `maxLength`, deliberately: it truncates a paste without saying so, and a password silently
                    shortened is refused by the deployment and read back as a wrong password.
                    `resolveCredentialEntry` refuses what is too long by name instead. */}
                {passwordOffered ? (
                    <>
                        <div className="flex flex-col gap-1.5">
                            <label className={fieldLabel} htmlFor="sign-in-user-name">
                                {translate('signIn.userName')}
                            </label>
                            <div className={fieldBox}>
                                <input
                                    aria-describedby={describedBy(
                                        keptBeyondTheTabOffered ? 'sign-in-keep' : 'sign-in-kept',
                                    )}
                                    aria-invalid={marks('userName')}
                                    autoComplete="username"
                                    className={fieldInput}
                                    id="sign-in-user-name"
                                    placeholder={translate('signIn.userNameExample')}
                                    ref={name}
                                    spellCheck={false}
                                    type="text"
                                    value={userName}
                                    onChange={(event) => {
                                        setUserName(event.target.value);
                                        setRefusal(null);
                                    }}
                                />
                            </div>
                        </div>

                        <div className="flex flex-col gap-1.5">
                            <label className={fieldLabel} htmlFor="sign-in-password">
                                {translate('signIn.password')}
                            </label>
                            <div className={fieldBox}>
                                <input
                                    aria-describedby={describedBy(
                                        keptBeyondTheTabOffered ? 'sign-in-keep' : 'sign-in-kept',
                                    )}
                                    aria-invalid={marks('password')}
                                    autoComplete="current-password"
                                    className={fieldInput}
                                    id="sign-in-password"
                                    type={revealed ? 'text' : 'password'}
                                    value={password}
                                    onChange={(event) => {
                                        setPassword(event.target.value);
                                        setRefusal(null);
                                    }}
                                />

                                {/* A real button rather than a word somebody clicks, so it is reachable from the keyboard and
                            announced as what it does. Its accessible name says the action; the word beside it is what
                            the design shows and is out of the accessibility tree because it would be read twice. */}
                                <button
                                    aria-label={translate(
                                        revealed ? 'signIn.hidePasswordControl' : 'signIn.revealPasswordControl',
                                    )}
                                    className="-me-1.75 flex min-h-12 shrink-0 items-center rounded-xl px-3 text-base text-muted transition hover:bg-hover hover:text-text workspace:min-h-8 workspace:px-1.5 workspace:text-sm"
                                    type="button"
                                    onClick={() => {
                                        setRevealed(!revealed);
                                    }}
                                >
                                    <span aria-hidden="true">
                                        {translate(revealed ? 'signIn.hidePassword' : 'signIn.revealPassword')}
                                    </span>
                                </button>
                            </div>
                        </div>

                        {/* Under the password field and only where there is one, as the design draws it: what this keeps is a
                    password sign-in, and a provider session follows the provider's own rules. It is also only drawn
                    where the store has somewhere to keep it — a choice between one place and the same place is not a
                    choice, and the sentence below the form says what is happening instead. */}
                        {keptBeyondTheTabOffered ? (
                            <label className="flex cursor-pointer items-start gap-2.25 py-0.5 select-none">
                                {/* Named by the line above the hint rather than by the whole label. A `label` wrapping both
                            would name the control with the sentence under it as well, which reads out as one run-on
                            name and is not what the hint is: it describes what ticking the box will do. */}
                                <input
                                    aria-describedby="sign-in-keep"
                                    aria-labelledby="sign-in-keep-label"
                                    checked={keepSignedIn}
                                    className="mt-0.5 size-5.5 shrink-0 cursor-pointer accent-accent workspace:size-4.5"
                                    type="checkbox"
                                    onChange={(event) => {
                                        setKeepSignedIn(event.target.checked);
                                    }}
                                />

                                <span className="flex min-w-0 flex-col gap-0.5">
                                    <span className="text-sm text-text-soft" id="sign-in-keep-label">
                                        {translate('signIn.keepMeSignedIn')}
                                    </span>

                                    {/* The hint is the design's own, and it changes with the box rather than describing the
                                control: ticked it says what will be kept and for how long, unticked it says what the
                                choice does not cover. */}
                                    <span className="text-xs leading-snug text-faint text-pretty" id="sign-in-keep">
                                        {translate(
                                            keepSignedIn
                                                ? nothingKeptMessages[beyondTheTab]
                                                : 'signIn.keepMeSignedInUnticked',
                                        )}
                                    </span>
                                </span>
                            </label>
                        ) : null}
                    </>
                ) : null}

                {shown === null || presenting ? null : (
                    <p
                        className="rounded-lg bg-warning-soft px-3 py-2.25 text-sm text-warning-text"
                        id="sign-in-refusal"
                        role="alert"
                    >
                        {translate(shown.message)}
                    </p>
                )}

                {passwordOffered ? (
                    <div className="flex items-center gap-3">
                        <button
                            className="flex min-h-13 flex-1 items-center justify-center gap-2.25 rounded-full bg-accent px-4.5 text-lg font-semibold text-on-accent transition hover:bg-accent-strong disabled:opacity-70 workspace:min-h-11.5 workspace:rounded-xl workspace:text-md"
                            disabled={presenting}
                            ref={submit}
                            type="submit"
                        >
                            {presenting ? <Spinner /> : null}
                            {presenting
                                ? translate('signIn.presenting', { address: reaching?.authority ?? '' })
                                : translate('signIn.submit')}
                        </button>

                        {presenting ? (
                            <SecondaryButton label={translate('signIn.abandon')} shape="form" onActivate={abandon} />
                        ) : null}
                    </div>
                ) : null}

                {/* Under the submit, as the design draws it, and on every shape of this screen: what a password is
                    about to cross is worth checking whether or not the address can be changed here. What the
                    disclosure offers follows the address: the permission an unsecured connection needs while one is
                    being typed, and the way out of an address somebody named themselves on an earlier run. */}
                <AdvancedConnection
                    connection={connection}
                    clearTextPermitted={clearTextPermitted}
                    clearTextConfigured={configuredClearText !== null}
                    onPermitClearText={
                        deployment === null
                            ? (permitted) => {
                                  setClearTextPermitted(permitted);
                                  setRefusal(null);
                              }
                            : null
                    }
                    onChangeServer={adopted?.origin === 'chosen' ? onPointSomewhereElse : undefined}
                />
            </form>

            {/* Where the sign-in is kept, for the case the checkbox above is not drawn for: a store with nowhere to
                put a session says so here rather than offering a choice between one place and the same place. Out of
                sight rather than out of the document, because the design draws no sentence in that case and both
                credential fields are described by it — a reader who is told nothing about where a password goes has
                been told less than the screen knows. Where the box is drawn, its own hint is the statement and this
                one would be a second one saying something else. */}
            {keptBeyondTheTabOffered ? null : (
                <p className="sr-only" id="sign-in-kept">
                    {translate(nothingKeptMessages[beyondTheTab])}
                </p>
            )}

            {/* The wait is drawn on the control that started it, which is where somebody looking at the screen reads
                it. A label changing is not something a screen reader announces, so the same sentence stands here in a
                live region as well — out of sight rather than out of the accessibility tree, because two copies of it
                on the screen would say the same thing twice. */}
            {presenting ? (
                <p className="sr-only" role="status">
                    {translate('signIn.presenting', { address: reaching?.authority ?? '' })}
                </p>
            ) : null}
        </section>
    );
}

/**
 * What this screen has to say about the deployment rather than about a field, in the one shape both such sentences take.
 *
 * Above the form rather than inside it, because neither is about something somebody typed: one says the deployment
 * offers no way in at all, and the other that it offers one this screen is not drawing.
 */
function StatedNotice({ children, message }: { readonly children?: ReactNode; readonly message: string }) {
    return (
        <p
            className="flex flex-wrap items-start gap-2.25 rounded-lg border border-warning bg-warning-soft px-3.25 py-2.75 text-xs leading-normal text-warning-text text-pretty"
            role="alert"
        >
            <Icon name="report" className="mt-0.25 size-4 text-warning-text" />
            {message}
            {children}
        </p>
    );
}

/** The rule with a word on it that separates one offering from the next, which the design draws twice with two words. */
function Divider({ label }: { readonly label: string }) {
    return (
        <p className="flex items-center gap-2.75 text-xs text-faint">
            <span aria-hidden="true" className="h-px flex-1 bg-line" />
            {label}
            <span aria-hidden="true" className="h-px flex-1 bg-line" />
        </p>
    );
}

/**
 * One provider in the grid, drawn with the mark the bundle carries for it or with the symbol every credential takes.
 *
 * The accessible name is the whole sentence rather than the provider's name alone, because what the control does is
 * leave this application for somebody else's sign-in screen — which is exactly what a reader is owed before they press
 * it. The label beside the mark is the provider's name, out of the accessibility tree, because it would otherwise be
 * read twice.
 */
function ProviderButton({
    busy,
    disabled,
    label,
    provider,
    onActivate,
}: {
    readonly busy: boolean;
    readonly disabled: boolean;
    readonly label: string;
    readonly provider: SignInAuthorizationServer;
    readonly onActivate: () => void;
}) {
    return (
        <button
            aria-label={label}
            className={`flex min-h-13 items-center justify-center gap-1.75 overflow-hidden rounded-xl border bg-panel px-2.5 text-text-soft transition hover:border-line-strong hover:bg-hover disabled:opacity-70 workspace:min-h-11 ${busy ? 'border-accent' : 'border-line'}`}
            disabled={disabled}
            type="button"
            onClick={onActivate}
        >
            {busy ? <Spinner /> : <ProviderMark name={provider.name} className="size-4.25" />}

            <span aria-hidden="true" className="truncate text-sm font-medium">
                {provider.displayName}
            </span>
        </button>
    );
}

// What a submit says while it is waiting. Out of the accessibility tree because the sentence below the form is what
// announces the wait; this is the same statement drawn, for a reader who is looking at the button they just pressed.
function Spinner() {
    return (
        <span
            aria-hidden="true"
            className="size-3.5 animate-spin rounded-full border-2 border-current border-t-transparent"
        />
    );
}

/** What a refusal reads as on the shape this screen is actually rendering. */
function shownFor(refusal: SignInScreenRefusal, deployment: DeploymentAddress | null): Refusal {
    return refusal === 'unavailable' && deployment !== null ? silentDeployment : refusals[refusal];
}

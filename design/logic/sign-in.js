// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.artboards.signIn = (DCLogic, React) => {
  const { signInAppVersion: APP_VERSION, signInOauthProviders: OAUTH, signInServerSamples: SERVER_SAMPLES } = MailFathomDesign.data;
  const FRAMES = { tablet: { w: 1024, h: 768 }, fold: { w: 884, h: 832 }, phone: { w: 390, h: 844 } };
  const LS_SRV = "mailfathom.server";
  const LS_TLS = "mailfathom.noTls";

  class Component extends DCLogic {
    _saved = (() => {
      try { return { srv: localStorage.getItem(LS_SRV), tls: localStorage.getItem(LS_TLS) }; }
      catch (e) { return { srv: null, tls: null }; }
    })();

    state = {
      view: "login",
      srv: this._saved.srv,           // null = default from configuration
      notls: this._saved.tls === "1",
      user: "", pass: "", reveal: false, remember: false, advanced: false, focus: null,
      connecting: false, probing: false, oauth: null, error: "", vw: 1440, theme: null, lang: "en",
    };

    rootRef = React.createRef();

    componentDidMount() {
      const el = this.rootRef.current;
      const measure = () => {
        const w = el && el.parentElement ? el.parentElement.clientWidth : window.innerWidth;
        if (w && w !== this.state.vw) this.setState({ vw: w });
      };
      measure();
      this._ro = window.ResizeObserver ? new ResizeObserver(measure) : null;
      if (this._ro && el && el.parentElement) this._ro.observe(el.parentElement);
      this._onResize = measure;
      window.addEventListener("resize", this._onResize);
    }

    componentWillUnmount() {
      clearTimeout(this._t);
      clearTimeout(this._p);
      if (this._ro) this._ro.disconnect();
      window.removeEventListener("resize", this._onResize);
    }

    defaultServer() { return (this.props.defaultServer ?? SERVER_SAMPLES.defaultServer).trim(); }
    /* Configuration handed to the app (env / MDM) wins: the address and TLS mode are then immutable. */
    envLocked() { return !!this.props.forcedServer; }
    server() { return (this.envLocked() || this.state.srv == null) ? this.defaultServer() : this.state.srv; }
    notls() { return this.envLocked() ? false : this.state.notls; }
    /* Non-default server or a TLS exception — then we offer a way back to the installer configuration. */
    customized() { return this.server().trim() !== this.defaultServer() || this.state.notls; }

    save(srv, notls) {
      try {
        if (srv == null) localStorage.removeItem(LS_SRV); else localStorage.setItem(LS_SRV, srv);
        if (notls) localStorage.setItem(LS_TLS, "1"); else localStorage.removeItem(LS_TLS);
      } catch (e) {}
    }

    setServer = e => {
      const v = e.target.value;
      this.setState({ srv: v, error: "" });
      this.save(v, this.state.notls);
    };

    toggleInsecure = () => {
      const v = !this.state.notls;
      this.setState({ notls: v, error: "" });
      this.save(this.state.srv, v);
    };

    restoreDefaults = () => {
      this.setState({ srv: null, notls: false, error: "" });
      this.save(null, false);
    };

    parse() {
      const raw = (this.server() || "").trim().replace(/^\w+:\/\//, "");
      const m = raw.match(/^([^:/\s]*)(?::(\d{1,5}))?/);
      return { host: (m && m[1]) || "", port: (m && m[2]) || "" };
    }

    go = () => {
      if (!this.state.user.trim()) return this.setState({ error: "Enter a username." });
      if (!this.state.pass) return this.setState({ error: "Enter a password." });
      this.setState({ connecting: true, error: "" });
      clearTimeout(this._t);
      this._t = setTimeout(() => this.setState({ connecting: false }), 1600);
    };

    /* SSO leaves the app: the client hands off to the MailFathom identity provider in the system browser. */
    goSso = () => {
      this.setState({ sso: true, error: "" });
      clearTimeout(this._s);
      this._s = setTimeout(() => this.setState({ sso: false }), 2000);
    };

    goOauth = id => () => {
      this.setState({ oauth: id, error: "" });
      clearTimeout(this._t);
      this._t = setTimeout(() => this.setState({ oauth: null }), 1800);
    };

    /* "Connect" on the server screen = check the address and return to sign-in with the new methods. */
    connectServer = () => {
      if (!this.parse().host) return this.setState({ error: "Enter the server address, e.g. " + SERVER_SAMPLES.example });
      this.setState({ probing: true, error: "" });
      clearTimeout(this._p);
      this._p = setTimeout(() => this.setState({ probing: false, view: "login", advanced: false }), 1100);
    };

    box(name, h) {
      const on = this.state.focus === name;
      return "display:flex;align-items:center;gap:10px;min-height:" + (h || 44) + "px;background:var(--panel);border:1px solid " +
        (on ? "var(--accent)" : "var(--border2)") +
        ";box-shadow:" + (on ? "0 0 0 3px var(--accent-soft)" : "none") +
        ";border-radius:12px;padding:0 13px;transition:border-color 0.12s,box-shadow 0.12s";
    }

    themeChoice() { return this.state.theme ?? this.props.theme ?? "light"; }

    resolvedTheme() {
      const t = this.themeChoice();
      if (t !== "auto") return t;
      const dark = typeof window !== "undefined" && window.matchMedia
        && window.matchMedia("(prefers-color-scheme: dark)").matches;
      return dark ? "dark" : "light";
    }

    renderVals() {
      const s = this.state, p = this.parse();
      const mode = this.props.layout ?? "auto";
      const FR = FRAMES[mode] || null;
      const w = FR ? FR.w : s.vw;
      const phone = w < 700;
      const stacked = w < 940;
      const pad = phone ? "22px 18px 28px 18px" : stacked ? "30px 34px" : "44px 46px";
      const tapH = phone ? 52 : 44;
      /* Touch layouts (phone, fold, tablet) — the accessibility rule raises every finger target
         to at least 44×44 px, so the theme and language switches must honour that in the design. */
      const touchPick = w < 1180;
      const pickBase = touchPick
        ? "min-width:44px;min-height:44px;display:flex;align-items:center;justify-content:center;text-align:center;padding:0 7px;"
        : "min-width:26px;text-align:center;padding:4px 7px;";
      const dark = this.resolvedTheme() === "dark";
      const locked = this.envLocked();
      const notls = this.notls();
      const secure = !notls;
      const defPort = secure ? "7443" : "7080";
      const port = p.port || defPort;
      const busy = s.connecting || !!s.oauth;
      const onServer = s.view === "server" && !locked;
      const oauthOn = this.props.oauthAvailable ?? true;
      const basicOn = this.props.basicAvailable ?? true;
      const ssoOn = this.props.ssoAvailable ?? true;
      const ssoName = (this.props.ssoName || "MailFathom SSO").trim() || "MailFathom SSO";
      const provider = OAUTH.find(o => o.id === s.oauth);
      return {
        rootRef: this.rootRef,
        themeAttr: this.resolvedTheme(),
        themePicks: [["auto", "Auto", "A"], ["light", "Light", "☀"], ["dark", "Dark", "☾"]].map(([v, label, short]) => ({
          label, short,
          pick: () => this.setState({ theme: v }),
          style: pickBase + "border-radius:6px;font-size:11.5px;line-height:1.35;" +
            (this.themeChoice() === v
              ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
              : "color:var(--muted);"),
        })),
        langPicks: [["en", "English", "EN"], ["pl", "Polski", "PL"]].map(([v, label, short]) => ({
          label, short,
          pick: () => this.setState({ lang: v }),
          style: pickBase + "border-radius:6px;font-size:11.5px;line-height:1.35;font-weight:600;" +
            ((s.lang ?? "en") === v
              ? "background:var(--accent);color:var(--onaccent);font-weight:600;"
              : "color:var(--muted);"),
        })),
        rootStyle: "display:flex;" + (stacked ? "flex-direction:column;" : "") +
          "background:var(--bg);color:var(--text);font-family:'Geist',system-ui,sans-serif;overflow:hidden;" + (FR
            ? "width:" + FR.w + "px;max-width:100%;height:" + FR.h + "px;max-height:100%;margin:0 auto;border:1px solid var(--border2);border-radius:" + (phone ? 20 : 14) + "px;box-shadow:0 20px 50px var(--sh-2)"
            : "min-height:100vh"),
        brandStyle: stacked
          ? "flex:0 0 auto;display:flex;align-items:center;gap:11px;padding:" + (phone ? "18px 18px 12px 18px" : "22px 34px 14px 34px") + ";background:var(--rail);border-bottom:1px solid var(--line)"
          : "flex:1 1 44%;min-width:0;display:flex;flex-direction:column;gap:40px;padding:44px 46px;background:var(--rail);border-right:1px solid var(--line)",
        brandRowStyle: stacked ? "display:contents" : "display:flex;align-items:center;gap:12px",
        logoStyle: "width:" + (stacked ? 34 : 40) + "px;height:" + (stacked ? 34 : 40) + "px;border-radius:12px;display:block;flex:0 0 auto",
        wordmarkStyle: "font-size:" + (stacked ? 16 : 17) + "px;font-weight:600;letter-spacing:-0.01em",
        showPitch: !stacked,
        formPaneStyle: "flex:" + (stacked ? "1 1 auto" : "1 1 56%") + ";min-width:0;min-height:0;display:flex;flex-direction:column;align-items:center;justify-content:" + (stacked ? "flex-start" : "center") +
          ";gap:" + (phone ? 18 : 22) + "px;overflow:auto;padding:" + pad,
        formStyle: "width:100%;max-width:" + (phone ? "100%" : "392px") + ";display:flex;flex-direction:column;gap:" + (phone ? 18 : 22) + "px",
        sectionGap: phone ? 18 : 20,
        titleStyle: "font-size:" + (phone ? 24 : 22) + "px;font-weight:600;letter-spacing:-0.01em",
        fieldStyle: "flex:1;min-width:0;border:none;outline:none;background:transparent;font-size:" + (phone ? 16 : 14) + "px;color:var(--text)",
        revealStyle: "display:flex;align-items:center;justify-content:center;min-height:" + (phone ? 48 : 32) + "px;padding:0 " + (phone ? 12 : 6) + "px;margin-right:-7px;border-radius:12px;font-size:" + (phone ? 13 : 12) + "px;color:var(--muted);white-space:nowrap;user-select:none",
        advancedBtnStyle: "display:flex;align-items:center;gap:6px;align-self:flex-start;min-height:" + (phone ? 48 : 28) + "px;padding:" + (phone ? "0 12px 0 8px;margin-left:-8px" : "0 9px 0 5px;margin-left:-5px") + ";border-radius:12px;font-size:" + (phone ? 14 : 13) + "px;font-weight:500;color:var(--accent);user-select:none",
        backBtnStyle: "display:flex;align-items:center;gap:6px;align-self:flex-start;min-height:" + (phone ? 48 : 30) + "px;padding:" + (phone ? "0 12px 0 8px;margin-left:-8px" : "0 9px 0 6px;margin-left:-6px") + ";border-radius:12px;font-size:" + (phone ? 14 : 13) + "px;font-weight:500;color:var(--accent);user-select:none",
        ghostBtnStyle: "display:flex;align-items:center;justify-content:center;gap:8px;min-height:" + (phone ? 52 : 44) + "px;border:1px solid var(--line);background:var(--panel);color:var(--accent);border-radius:" + (phone ? 26 : 10) + "px;padding:0 16px;font-size:" + (phone ? 15 : 13.5) + "px;font-weight:600;user-select:none",
        restoreBtnStyle: "display:flex;align-items:center;gap:8px;min-height:" + (phone ? 50 : 42) + "px;border:1px dashed var(--border2);background:transparent;color:var(--text2);border-radius:12px;padding:0 14px;font-size:" + (phone ? 14 : 13) + "px;font-weight:500;user-select:none",
        helpRowStyle: "display:flex;align-items:center;min-height:" + (phone ? 48 : 24) + "px;font-size:" + (phone ? 13 : 12) + "px;color:var(--muted)",

        isLogin: !onServer,
        isServer: onServer,
        envLocked: locked,
        canChangeServer: !locked,
        openServer: locked ? () => {} : () => this.setState({ view: "server", error: "", focus: null }),
        closeServer: () => this.setState({ view: "login", error: "", focus: null }),

        serverLabel: this.server() || "-",
        defaultServerLabel: this.defaultServer(),
        customized: this.customized(),
        restoreDefaults: this.restoreDefaults,
        lockIcon: secure ? "lock" : "lock_open",
        lockColor: secure ? "var(--ok-text)" : "var(--warn-text)",

        /* The server declares the sign-in methods — in this demo: SSO + basic auth + three OAuth providers. */
        showSso: ssoOn,
        ssoBusy: !!s.sso,
        ssoIdle: !s.sso,
        ssoLabel: s.sso ? "Opening " + ssoName + "…" : "Sign in",
        ssoNote: s.sso
          ? "Finish signing in in the browser window - this screen picks up the session when it returns."
          : ssoName + " opens in your browser.",
        goSso: this.goSso,
        ssoBtnStyle: "display:flex;align-items:center;justify-content:center;gap:8px;min-height:" + (phone ? 52 : 44) + "px;border-radius:" + (phone ? 26 : 10) + "px;background:var(--accent);color:var(--onaccent);border:1px solid var(--accent);padding:0 16px;font-size:" + (phone ? 15 : 14) + "px;font-weight:600;user-select:none" + (s.sso ? ";opacity:0.85" : ""),
        showSsoDivider: ssoOn && (oauthOn || basicOn),
        showOauth: oauthOn,
        showBasic: basicOn,
        showDivider: oauthOn && basicOn,
        noMethods: !ssoOn && !oauthOn && !basicOn,
        oauthBtns: OAUTH.slice(0, Math.max(1, Math.min(12, Math.round(this.props.oauthCount ?? 3)))).map(o => ({
          label: o.label,
          title: "Continue to " + o.label + " (OAuth)",
          click: this.goOauth(o.id),
          iconStyle: "width:17px;height:17px;display:block;flex:0 0 auto;background-repeat:no-repeat;background-position:center;background-size:contain;background-image:url(https://cdn.simpleicons.org/" +
            o.slug + "/" + (dark ? o.dark : o.light) + ")" + (s.oauth === o.id ? ";opacity:0.5" : ""),
          style: "display:flex;align-items:center;justify-content:center;gap:7px;min-height:" + (phone ? 52 : 44) + "px;padding:0 10px;border:1px solid " +
            (s.oauth === o.id ? "var(--accent)" : "var(--line)") + ";background:var(--panel);border-radius:12px;color:var(--text2);user-select:none;overflow:hidden",
        })),

        host: this.server(), user: s.user, pass: s.pass,
        userPlaceholder: SERVER_SAMPLES.userPlaceholder,
        serverPlaceholder: SERVER_SAMPLES.serverPlaceholder,
        hostNote: "Port optional - without it we " + (secure ? "use 7443 (MFP over TLS)" : "use 7080 (MFP)") + ". Stored locally on this device.",
        onHost: this.setServer,
        onUser: e => this.setState({ user: e.target.value, error: "" }),
        onPass: e => this.setState({ pass: e.target.value, error: "" }),
        onKey: e => { if (e.key === "Enter") this.go(); },
        onServerKey: e => { if (e.key === "Enter") this.connectServer(); },
        focusHost: () => this.setState({ focus: "host" }),
        focusUser: () => this.setState({ focus: "user" }),
        focusPass: () => this.setState({ focus: "pass" }),
        blurAny: () => this.setState({ focus: null }),
        hostBoxStyle: this.box("host", tapH),
        userBoxStyle: this.box("user", tapH),
        passBoxStyle: this.box("pass", tapH),
        portHint: p.port ? "port " + p.port : ":" + defPort,
        /* Remember me is a basic-auth affordance only — OAuth sessions are governed by the provider. */
        toggleRemember: () => this.setState({ remember: !s.remember }),
        rememberRowStyle: "display:flex;align-items:flex-start;gap:9px;padding:" + (phone ? "4px 0 2px" : "2px 0 0") + ";user-select:none",
        rememberBoxStyle: "width:" + (phone ? 22 : 18) + "px;height:" + (phone ? 22 : 18) + "px;flex:0 0 " + (phone ? 22 : 18) + "px;margin-top:1px;border-radius:6px;display:flex;align-items:center;justify-content:center;border:1px solid " +
          (s.remember ? "var(--accent)" : "var(--line)") + ";background:" + (s.remember ? "var(--accent)" : "var(--panel)"),
        rememberTickStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 500;font-size:13px;line-height:1;color:var(--onaccent);opacity:" + (s.remember ? "1" : "0"),
        rememberHint: s.remember ? "This device stays signed in for 30 days. Applies to password sign-in only." : "Applies to password sign-in only - provider sessions follow their own rules.",

        passType: s.reveal ? "text" : "password",
        revealLabel: s.reveal ? "Hide" : "Show",
        toggleReveal: () => this.setState({ reveal: !s.reveal }),
        toggleInsecure: this.toggleInsecure,
        insecure: notls,
        insecureRowStyle: "display:flex;align-items:flex-start;gap:11px;min-height:" + (phone ? 56 : 0) + "px;border:1px solid " +
          (notls ? "var(--warn)" : "var(--line)") + ";background:" +
          (notls ? "var(--warn-soft)" : "var(--panel)") + ";border-radius:12px;padding:" + (phone ? "14px 14px" : "12px 13px"),
        checkboxStyle: "flex:0 0 auto;width:" + (phone ? 22 : 18) + "px;height:" + (phone ? 22 : 18) + "px;margin-top:1px;border-radius:6px;display:flex;align-items:center;justify-content:center;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:14px;line-height:1;border:1px solid " +
          (notls ? "var(--warn)" : "var(--border2)") + ";background:" +
          (notls ? "var(--warn)" : "transparent") + ";color:var(--onaccent)",
        checkMark: notls ? "check" : "",
        insecureNote: notls
          ? "TLS off - sign-in and messages will travel in plain text."
          : "Lets you connect to a server without a valid TLS certificate.",

        hasError: !!s.error,
        errorText: s.error,
        connecting: busy,
        probing: s.probing,
        submitLabel: provider ? "Redirecting to " + provider.label + "…" : busy ? "Connecting to " + (p.host || "the server") + "…" : "Connect",
        serverBtnLabel: s.probing ? "Checking " + (p.host || "the server") + "…" : "Connect",
        submitStyle: "display:flex;align-items:center;justify-content:center;gap:9px;min-height:" + (phone ? 52 : 46) + "px;background:var(--accent);color:var(--onaccent);border-radius:" + (phone ? 26 : 10) + "px;padding:0 18px;font-size:" + (phone ? 15 : 14) + "px;font-weight:600;user-select:none;opacity:" + ((busy || s.probing) ? "0.7" : "1"),
        submitHover: (busy || s.probing) ? "cursor:default" : "cursor:pointer;background:var(--accent-2)",
        submit: busy ? () => {} : this.go,
        connectServer: s.probing ? () => {} : this.connectServer,

        advanced: s.advanced,
        chevronRotate: s.advanced ? "transform:rotate(90deg);transition:transform 140ms ease" : "transition:transform 140ms ease",
        footRowStyle: "display:flex;align-items:center;justify-content:space-between;gap:12px;flex-wrap:wrap;width:100%;max-width:" + (phone ? "100%" : "392px") + ";font-size:12px;color:var(--muted)",
        appVersion: APP_VERSION,
        toggleAdvanced: () => this.setState({ advanced: !s.advanced }),
        protoLabel: secure ? "MFP over TLS" : "MFP unencrypted",
        parsedHost: p.host || "-",
        parsedPort: port + (p.port ? "" : " (default)"),
        certLabel: secure ? "Required" : "Skipped",
        certColor: secure ? "var(--ok-text)" : "var(--warn-text)",
      };
    }
  }

  return Component;
};

// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.artboards.toasts = (DCLogic, React) => {
  const {
    toastsSamples: SAMPLES,
    toastsBurst: BURST,
    toastsMessages: MESSAGES,
    toastsTasks: TASKS,
    toastsBlockingOperations: BLOCKING,
    toastsBlockSpecs: BLOCK_SPECS,
  } = MailFathomDesign.data;

  const FRAMES = { tablet: { w: 1024, h: 768 }, fold: { w: 884, h: 832 }, phone: { w: 390, h: 844 } };

  const KINDS = {
    neutral: { icon: "info", c: "var(--text2)", soft: "var(--hover)", bar: "var(--border2)" },
    success: { icon: "check_circle", c: "var(--ok-text)", soft: "var(--ok-soft)", bar: "var(--ok)" },
    error: { icon: "error", c: "var(--err-text)", soft: "var(--err-soft)", bar: "var(--err)" },
    warning: { icon: "warning", c: "var(--warn-text)", soft: "var(--warn-soft)", bar: "var(--warn)" },
    info: { icon: "campaign", c: "var(--accent-2)", soft: "var(--accent-soft)", bar: "var(--accent)" },
    loading: { icon: "progress_activity", c: "var(--accent-2)", soft: "var(--accent-soft)", bar: "var(--accent)" },
  };

  class Component extends DCLogic {
    state = { toasts: [], cancelAsk: null, theme: null, vw: 1200 };
    rootRef = React.createRef();

    componentDidMount() {
      this.measure();
      if (typeof ResizeObserver !== "undefined" && this.rootRef.current) {
        this.ro = new ResizeObserver(this.measure);
        this.ro.observe(this.rootRef.current);
      }
    }
    componentWillUnmount() {
      if (this.ro) this.ro.disconnect();
      Object.keys(this._tT || {}).forEach(k => clearTimeout(this._tT[k]));
      Object.keys(this._tasks || {}).forEach(k => clearTimeout(this._tasks[k]));
    }
    measure = () => {
      const el = this.rootRef.current;
      const w = (el && el.clientWidth) || 1200;
      if (w !== this.state.vw) this.setState({ vw: w });
    };

    ms = () => Number(this.props.autoHideMs ?? 5000);
    maxStack = () => Number(this.props.maxStack ?? 4);

    notify = (o) => {
      const id = "t" + (this._tSeq = (this._tSeq || 0) + 1);
      const t = {
        id: id, kind: o.kind || "neutral", title: o.title, body: o.body || "",
        actionLabel: o.action || "", ms: o.ms || this.ms(),
        sticky: o.kind === "loading", cancelText: o.cancelText || "",
      };
      this.setState(s => ({ toasts: [t].concat(s.toasts || []).slice(0, this.maxStack()) }));
      if (!t.sticky) this.arm(id, t.ms);
      return id;
    };

    arm = (id, ms) => {
      this._tT = this._tT || {};
      clearTimeout(this._tT[id]);
      this._tT[id] = setTimeout(() => this.dismiss(id), ms);
    };

    dismiss = (id) => {
      this._tT = this._tT || {};
      clearTimeout(this._tT[id]);
      this.setState(s => ({ toasts: (s.toasts || []).map(x => (x.id === id ? Object.assign({}, x, { out: true }) : x)) }));
      setTimeout(() => this.setState(s => ({ toasts: (s.toasts || []).filter(x => x.id !== id) })), 200);
    };

    patch = (id, p) => {
      this.setState(s => ({ toasts: (s.toasts || []).map(x => (x.id === id ? Object.assign({}, x, p) : x)) }));
      if (!p.sticky) this.arm(id, this.ms());
    };

    task = (o) => {
      const id = this.notify({ kind: "loading", title: o.title, body: o.body, cancelText: o.cancelText });
      this._tasks = this._tasks || {};
      this._tasks[id] = setTimeout(() => {
        delete this._tasks[id];
        this.patch(id, { kind: o.doneKind || "success", title: o.doneTitle, body: o.doneBody || "", actionLabel: o.doneAction || "", sticky: false, ms: this.ms() });
      }, o.ms || 2600);
      return id;
    };

    askCancel = (id) => {
      const t = (this.state.toasts || []).filter(x => x.id === id)[0];
      this.setState({ cancelAsk: { id: id, text: (t && t.cancelText) || MESSAGES.cancelAskText } });
    };

    abort = () => {
      const ask = this.state.cancelAsk;
      this.setState({ cancelAsk: null });
      if (!ask) return;
      if (this._tasks && this._tasks[ask.id]) { clearTimeout(this._tasks[ask.id]); delete this._tasks[ask.id]; }
      this.dismiss(ask.id);
      this.notify({ kind: "warning", title: MESSAGES.cancelled.title, body: MESSAGES.cancelled.body });
    };

    startBlock = (det) => {
      this.setState({ block: { title: det ? BLOCKING.migration.title : BLOCKING.indexRebuild.title, body: det ? BLOCKING.migration.body : BLOCKING.indexRebuild.body, det: !!det, pct: det ? 4 : null } });
      this.runBlock(!!det);
    };

    runBlock = (det) => {
      clearInterval(this._blockI);
      clearTimeout(this._blockI);
      if (det) {
        this._blockI = setInterval(() => {
          const b = this.state.block;
          if (!b) { clearInterval(this._blockI); return; }
          const pct = Math.min(100, b.pct + 6 + Math.round(Math.random() * 7));
          if (pct >= 100) {
            clearInterval(this._blockI);
            this.setState({ block: null });
            this.notify({ kind: "success", title: BLOCKING.migration.finishedTitle, body: BLOCKING.migration.finishedBody });
          } else this.setState({ block: Object.assign({}, b, { pct: pct }) });
        }, 520);
      } else {
        this._blockI = setTimeout(() => {
          this.setState({ block: null });
          this.notify({ kind: "success", title: BLOCKING.indexRebuild.finishedTitle, body: BLOCKING.indexRebuild.finishedBody });
        }, 7000);
      }
    };

    abortBlock = () => {
      clearInterval(this._blockI);
      clearTimeout(this._blockI);
      const b = this.state.block;
      this.setState({ block: null, blockAsk: false });
      this.notify({ kind: "warning", title: "Operation aborted", body: b && b.det ? BLOCKING.migration.abortedBody : BLOCKING.indexRebuild.abortedBody });
    };

    vis = (t, live) => {
      const K = KINDS[t.kind] || KINDS.neutral;
      const loading = t.kind === "loading";
      return {
        icon: K.icon,
        hasBar: !t.sticky && !t.out,
        barStyle: "position:absolute;left:0;right:0;bottom:0;height:2px;transform-origin:left;opacity:0.55;background:" + K.bar +
          (live ? ";animation:mftbar " + (t.ms || this.ms()) + "ms linear both" : ";transform:scaleX(0.62)"),
        iconWrapStyle: "flex:0 0 auto;width:34px;height:34px;display:flex;align-items:center;justify-content:center;border-radius:12px;background:" + K.soft,
        iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:21px;line-height:1;color:" + K.c + (loading ? ";animation:mfspin 1.1s linear infinite" : ""),
        style: "position:relative;overflow:hidden;display:flex;align-items:flex-start;gap:12px;padding:13px 12px 15px 13px;border-radius:15px;background:var(--panel);border:1px solid var(--line);" +
          (live
            ? "pointer-events:auto;box-shadow:0 16px 38px var(--sh-2),0 2px 6px var(--sh-1);opacity:0.8;transition:opacity .15s ease;animation:" + (t.out ? "mftout .2s ease forwards" : "mftin .24s cubic-bezier(.2,.85,.3,1) backwards")
            : "box-shadow:0 6px 16px var(--sh-1)"),
      };
    };

    renderVals() {
      const st = this.state;
      const theme = st.theme || this.props.theme || "light";
      const mode = this.props.layout ?? "auto";
      const FR = FRAMES[mode] || null;
      const w = FR ? FR.w : (st.vw || 1200);
      const isMobile = w < 700;
      /* In frame mode the overlays are absolute inside the frame so they do not spill past the phone screen. */
      const ovBase = (z) => (FR ? "position:absolute" : "position:fixed") + ";inset:0;z-index:" + z + ";display:flex;align-items:center;justify-content:center;padding:" + (isMobile ? 18 : 24) + "px;background:var(--scrim);";
      const btn = "display:flex;align-items:center;gap:8px;height:38px;padding:0 15px;border:1px solid var(--border2);border-radius:12px;font-size:13px;font-weight:500;color:var(--text2)";
      const bar = this.props.showProgressBar ?? true;

      return {
        rootRef: this.rootRef,
        themeAttr: theme,
        themeIcon: theme === "dark" ? "dark_mode" : "light_mode",
        toggleTheme: () => this.setState({ theme: theme === "dark" ? "light" : "dark" }),
        outerStyle: "min-height:100vh;display:flex;justify-content:center;background:var(--bg);color:var(--text);font-family:'Geist',system-ui,sans-serif;padding:" + (FR ? "26px 20px 40px" : isMobile ? "20px 14px 44px" : "34px 28px 60px"),
        frameStyle: FR
          ? "box-sizing:border-box;width:" + FR.w + "px;max-width:100%;height:" + FR.h + "px;max-height:calc(100vh - 66px);position:relative;overflow:hidden;display:flex;flex-direction:column;background:var(--bg);border:1px solid var(--border2);border-radius:" + (isMobile ? 20 : 16) + "px;box-shadow:0 20px 60px var(--sh-3)"
          : "width:100%;max-width:1180px;position:relative;display:flex;flex-direction:column",
        frameScrollStyle: FR
          ? "flex:1;min-height:0;overflow:auto;display:flex;flex-direction:column;gap:22px;padding:" + (isMobile ? "20px 14px 34px" : "26px 24px 34px")
          : "display:flex;flex-direction:column;gap:26px",
        ovBlockStyle: ovBase(99) + "backdrop-filter:blur(3px)",
        ovAskStyle: ovBase(100),
        ovCancelStyle: ovBase(98),
        gridStyle: "display:grid;grid-template-columns:repeat(auto-fit,minmax(" + (isMobile ? "260px" : "320px") + ",1fr));gap:14px",
        btnStyle: btn,
        compare: [
          {
            name: "Toast", where: "Top right corner, fleeting", icon: "bolt", c: "var(--accent-2)", soft: "var(--accent-soft)",
            points: [
              "A response to my own action - I sent, deleted, downloaded.",
              "Lives " + Math.round(this.ms() / 1000) + " seconds and leaves no trace.",
              "Has no read state and no history.",
              "At most one action: Undo, Retry, Show.",
            ],
          },
          {
            name: "Notification", where: "Badge in the rail and the notification centre", icon: "notifications", c: "var(--warn-text)", soft: "var(--warn-soft)",
            points: [
              "An event from outside - new mail, calendar, a change in a case.",
              "Stays until read and has an unread counter.",
              "Can be marked as read or unread.",
              "A click leads to the thread, event or case.",
            ],
          },
        ].map(c => ({
          name: c.name, where: c.where, icon: c.icon,
          cardStyle: "display:flex;flex-direction:column;gap:11px;background:var(--panel);border:1px solid var(--line);border-radius:12px;padding:15px 16px 16px",
          iconWrapStyle: "flex:0 0 auto;width:34px;height:34px;display:flex;align-items:center;justify-content:center;border-radius:12px;background:" + c.soft,
          iconStyle: "font-family:'Material Symbols Rounded';font-variation-settings:'wght' 400;font-size:20px;line-height:1;color:" + c.c,
          points: c.points.map(t => ({
            text: t,
            dotStyle: "flex:0 0 auto;width:5px;height:5px;margin-top:8px;border-radius:50%;background:" + c.c,
          })),
        })),
        secLabel: Math.round(this.ms() / 1000) + " s",
        maxLabel: this.maxStack(),
        clearAll: () => (st.toasts || []).forEach(t => this.dismiss(t.id)),

        samples: SAMPLES.map(s => {
          const K = KINDS[s.kind];
          const demoT = { kind: s.kind, sticky: s.kind === "loading", ms: this.ms() };
          const v = this.vis(demoT, false);
          return {
            name: s.name, kind: s.kind, desc: s.desc, title: s.title, body: s.body,
            hasBody: !!s.body, hasAction: !!s.action, actionLabel: s.action,
            chipStyle: "font-size:11px;padding:3px 9px;border-radius:12px;background:;font-weight:600;" + K.soft + ";color:" + K.c,
            demo: Object.assign({}, v, { hasBar: v.hasBar && bar }),
            spawn: () => {
              if (s.kind === "loading") {
                this.task({ title: s.title, body: s.body, cancelText: TASKS.archive.cancelText, doneTitle: TASKS.archive.doneTitle, doneBody: TASKS.archive.doneBody, ms: TASKS.archive.ms });
              } else {
                this.notify({ kind: s.kind, title: s.title, body: s.body, action: s.action });
              }
            },
          };
        }),

        toasts: (st.toasts || []).map(t => {
          const v = this.vis(t, true);
          const loading = t.kind === "loading";
          return Object.assign({}, v, {
            id: t.id, title: t.title, body: t.body,
            hasBody: !!t.body,
            hasBar: v.hasBar && bar,
            hasAction: !!t.actionLabel,
            actionLabel: t.actionLabel,
            action: () => { this.dismiss(t.id); this.notify({ kind: "neutral", title: MESSAGES.restored.title, body: MESSAGES.restored.body }); },
            close: loading ? () => this.askCancel(t.id) : () => this.dismiss(t.id),
            closeTitle: loading ? "Abort operation" : "Close",
          });
        }),
        toastWrapStyle: (FR ? "position:absolute" : "position:fixed") + ";z-index:97;top:" + (isMobile ? 12 : 18) + "px;" + (isMobile ? "left:12px;right:12px;" : (FR ? "right:18px;width:min(400px,calc(100% - 36px));" : "right:18px;width:min(400px,calc(100vw - 36px));")) + "display:flex;flex-direction:column;gap:10px;pointer-events:none",

        burst: () => {
          const seq = BURST;
          seq.forEach((o, i) => setTimeout(() => this.notify(o), i * 420));
        },
        runTask: () => this.task(TASKS.mailboxSync),
        runFail: () => this.task(TASKS.sendFailure),

        specs: [
          { k: "Position", v: "Top right corner, 18 px from the edge. On a narrow screen, full width at the top." },
          { k: "Timing", v: "Auto-close after " + Math.round(this.ms() / 1000) + " s; the bar at the bottom shows the time left." },
          { k: "Transparency", v: "0.8 at rest, 1.0 under the cursor - content beneath the stack stays readable." },
          { k: "Close button", v: "Always present. For a running operation it means abort and asks for confirmation." },
        ],

        startBlocking: () => this.startBlock(true),
        startBlockingIndet: () => this.startBlock(false),
        blockOpen: !!st.block,
        blockTitle: st.block ? st.block.title : "",
        blockBody: st.block ? st.block.body : "",
        blockPctLabel: st.block ? (st.block.det ? st.block.pct + "% - do not close the tab" : "No known completion time") : "",
        blockFillStyle: "height:100%;border-radius:4px;background:var(--accent);" +
          (st.block && st.block.det
            ? "width:" + (st.block ? st.block.pct : 0) + "%;transition:width .45s linear"
            : "width:38%;animation:mfslide 1.4s ease-in-out infinite"),
        askBlockCancel: () => { clearInterval(this._blockI); clearTimeout(this._blockI); this.setState({ blockAsk: true }); },
        blockAskOpen: !!st.blockAsk,
        blockAskText: st.block && st.block.det
          ? "The migration is " + (st.block ? st.block.pct : 0) + "% done. Aborting leaves some messages on the old server - the migration will have to be run again."
          : "The index rebuild is not finished. Until it completes, search runs on the old index.",
        keepBlocking: () => { this.setState({ blockAsk: false }); this.runBlock(!!(st.block && st.block.det)); },
        abortBlocking: this.abortBlock,
        blockSpecs: BLOCK_SPECS,
        cancelAskOpen: !!st.cancelAsk,
        cancelAskText: st.cancelAsk ? st.cancelAsk.text : "",
        keepTask: () => this.setState({ cancelAsk: null }),
        abortTaskNow: this.abort,
      };
    }
  }

  return Component;
};

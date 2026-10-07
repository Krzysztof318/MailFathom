// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.artboards.notificationGesture = (DCLogic, React) => {
  const {
    notificationGestureRows: NOTIFICATION_ROWS,
    notificationGestureNumbers: NUMBERS,
    notificationGestureHandovers: HANDOVERS,
  } = MailFathomDesign.data;

  class Component extends DCLogic {
    state = { theme: null };

    icon = (tone) => "flex:0 0 26px;width:26px;height:26px;display:flex;align-items:center;justify-content:center;border-radius:8px;" +
      (tone === "accent" ? "background:var(--accent-soft);color:var(--accent-d)" : tone === "ok" ? "background:var(--ok-soft);color:var(--ok-text)" : "background:var(--hover);color:var(--muted)");

    bar = (activeIdx) => ["explore", "mail", "topic", "auto_awesome", "notifications", "more_horiz"].map((ic, i) => ({
      icon: ic,
      style: "flex:1;min-width:0;display:flex;align-items:center;justify-content:center;height:100%;border-radius:8px;" +
        (i === activeIdx ? "background:var(--accent-soft);color:var(--accent-d)" : "color:var(--muted)"),
    }));

    renderVals() {
      const theme = this.state.theme ?? (this.props.theme ?? "light");
      return {
        themeAttr: theme,
        themeIcon: theme === "dark" ? "dark_mode" : "light_mode",
        toggleTheme: () => this.setState({ theme: theme === "dark" ? "light" : "dark" }),
        barIcons: this.bar(1),
        barIconsOpen: this.bar(4),
        rows: NOTIFICATION_ROWS.slice(0, 3).map(r => ({ icon: r.icon, iconWrapStyle: this.icon(r.tone) })),
        rowsFull: NOTIFICATION_ROWS.map(r => ({ icon: r.icon, iconWrapStyle: this.icon(r.tone) })),
        numbers: NUMBERS,
        handovers: HANDOVERS.map(h => ({ ...h, iconWrapStyle: this.icon(h.tone) })),
      };
    }
  }

  return Component;
};

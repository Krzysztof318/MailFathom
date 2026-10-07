// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/Krzysztof318/MailFathom

MailFathomDesign.markdown = (() => {
  /* MARKDOWN IN MESSAGE BODIES
     Plenty of mail arrives as Markdown these days (tool reports, notes, CI bots),
     so half the samples in this prototype use it. The parser handles: headings, paragraphs,
     bold/italic/strikethrough, inline and block code, plain, ordered and task lists
     (including nesting), quotes, tables, horizontal rules and links. */

  const mdInline = (s) => {
    const out = [];
    const re = /(\*\*[^*]+\*\*|__[^_]+__|~~[^~]+~~|`[^`]+`|\*[^*\n]+\*|\[[^\]]+\]\([^)]+\))/g;
    let last = 0, m;
    while ((m = re.exec(s))) {
      if (m.index > last) out.push({ t: s.slice(last, m.index) });
      const tok = m[0];
      if (tok.indexOf("**") === 0 || tok.indexOf("__") === 0) out.push({ t: tok.slice(2, -2), b: true });
      else if (tok.indexOf("~~") === 0) out.push({ t: tok.slice(2, -2), s: true });
      else if (tok.charAt(0) === "`") out.push({ t: tok.slice(1, -1), c: true });
      else if (tok.charAt(0) === "[") { const mm = /\[([^\]]+)\]\(([^)]+)\)/.exec(tok); out.push({ t: mm[1], href: mm[2] }); }
      else out.push({ t: tok.slice(1, -1), i: true });
      last = m.index + tok.length;
    }
    if (last < s.length) out.push({ t: s.slice(last) });
    return out.length ? out : [{ t: s }];
  };

  const mdParse = (src) => {
    const lines = String(src).replace(/\r/g, "").split("\n");
    const out = [];
    let i = 0;
    const isBlockStart = (l) => /^\s*(#{1,4}\s|>|\||```|([-*+]|\d+\.)\s|(-{3,}|\*{3,})\s*$)/.test(l);
    while (i < lines.length) {
      const ln = lines[i];
      if (!ln.trim()) { i++; continue; }
      if (/^```/.test(ln.trim())) {
        const buf = []; i++;
        while (i < lines.length && !/^```/.test(lines[i].trim())) { buf.push(lines[i]); i++; }
        i++; out.push({ k: "code", text: buf.join("\n") }); continue;
      }
      if (/^(-{3,}|\*{3,})$/.test(ln.trim())) { out.push({ k: "hr" }); i++; continue; }
      const h = /^(#{1,4})\s+(.*)$/.exec(ln.trim());
      if (h) { out.push({ k: "h", lvl: h[1].length, text: h[2] }); i++; continue; }
      if (/^>\s?/.test(ln.trim())) {
        const buf = [];
        while (i < lines.length && /^>\s?/.test(lines[i].trim())) { buf.push(lines[i].trim().replace(/^>\s?/, "")); i++; }
        out.push({ k: "quote", text: buf.join(" ") }); continue;
      }
      if (/^\|/.test(ln.trim()) && /^[\s|:-]+$/.test(lines[i + 1] || "x")) {
        const cells = (r) => r.trim().replace(/^\|/, "").replace(/\|$/, "").split("|").map(c => c.trim());
        const head = cells(ln); i += 2;
        const rows = [];
        while (i < lines.length && /^\|/.test(lines[i].trim())) { rows.push(cells(lines[i])); i++; }
        out.push({ k: "table", head, rows }); continue;
      }
      if (/^\s*([-*+]|\d+\.)\s+/.test(ln)) {
        const ordered = /^\s*\d+\./.test(ln);
        const items = [];
        while (i < lines.length && /^\s*([-*+]|\d+\.)\s+/.test(lines[i])) {
          const m2 = /^(\s*)([-*+]|\d+\.)\s+(.*)$/.exec(lines[i]);
          let text = m2[3], done = null;
          const tb = /^\[( |x|X)\]\s+(.*)$/.exec(text);
          if (tb) { done = tb[1].toLowerCase() === "x"; text = tb[2]; }
          items.push({ text: text, done: done, depth: Math.min(1, Math.floor(m2[1].length / 2)), num: m2[2] });
          i++;
        }
        out.push({ k: ordered ? "ol" : "ul", items: items }); continue;
      }
      const buf = [];
      while (i < lines.length && lines[i].trim() && !isBlockStart(lines[i])) { buf.push(lines[i].trim()); i++; }
      out.push({ k: "p", text: buf.join(" ") });
    }
    return out;
  };

  const mdPlain = (src) => String(src).replace(/```[\s\S]*?```/g, " ").replace(/^[#>\s|*-]+/gm, "")
    .replace(/[*_`~]/g, "").replace(/\[([^\]]+)\]\([^)]+\)/g, "$1").replace(/\s+/g, " ").trim();

  const MDRUN = {
    b: "font-weight:650;color:var(--text)",
    i: "font-style:italic",
    s: "text-decoration:line-through;color:var(--muted)",
    c: "font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:0.88em;background:var(--hover);border:1px solid var(--line);border-radius:5px;padding:1px 5px",
    a: "color:var(--accent-d);text-decoration:underline",
  };
  const mdRuns = (text) => mdInline(text).map(r => ({
    text: r.t, plain: !r.href, isLink: !!r.href, href: r.href || "",
    style: r.href ? MDRUN.a : r.b ? MDRUN.b : r.i ? MDRUN.i : r.s ? MDRUN.s : r.c ? MDRUN.c : "",
  }));

  const MDTXT = "font-size:15px;line-height:1.65;color:var(--text2);text-wrap:pretty";
  const mdBlock = (b) => {
    if (b.k === "h") return { isText: true, runs: mdRuns(b.text), style: b.lvl <= 1
      ? "font-size:19px;font-weight:650;letter-spacing:-0.015em;color:var(--text);text-wrap:pretty;margin-top:2px"
      : b.lvl === 2
      ? "font-size:16px;font-weight:650;color:var(--text);text-wrap:pretty;margin-top:2px"
      : "font-family:'Instrument Sans',system-ui,sans-serif;font-size:11px;letter-spacing:0.1em;text-transform:uppercase;color:var(--muted);margin-top:2px" };
    if (b.k === "quote") return { isText: true, runs: mdRuns(b.text),
      style: "font-size:15px;line-height:1.6;color:var(--text2);background:var(--hl);border-left:3px solid var(--hl-line);border-radius:0 8px 8px 0;padding:11px 15px;text-wrap:pretty" };
    if (b.k === "code") return { isCode: true, text: b.text,
      style: "font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px;line-height:1.6;white-space:pre-wrap;color:var(--text2);background:var(--sub);border:1px solid var(--line);border-radius:9px;padding:12px 14px;overflow:auto" };
    if (b.k === "hr") return { isHr: true, style: "height:1px;background:var(--line);margin:2px 0" };
    if (b.k === "ul" || b.k === "ol") return { isList: true, style: "display:flex;flex-direction:column;gap:6px",
      items: b.items.map((it, n) => ({
        runs: mdRuns(it.text),
        style: "display:flex;gap:9px;align-items:flex-start" + (it.depth ? ";padding-left:22px" : ""),
        marker: it.done === null ? (b.k === "ol" ? (it.num || (n + 1) + ".") : "•") : it.done ? "check_box" : "check_box_outline_blank",
        markerStyle: it.done === null
          ? "flex:0 0 auto;min-width:14px;font-size:14px;line-height:1.65;color:var(--faint)"
          : "flex:0 0 auto;font-family:'Material Symbols Rounded';font-variation-settings:'wght' 300;font-size:18px;line-height:1.4;color:" + (it.done ? "var(--ok-text)" : "var(--faint)"),
        textStyle: "flex:1;min-width:0;" + MDTXT + (it.done ? ";color:var(--muted)" : ""),
      })) };
    if (b.k === "table") {
      const cellBase = "flex:1;min-width:0;padding:9px 12px;font-size:13.5px;line-height:1.45;text-wrap:pretty;border-right:1px solid var(--line)";
      const row = (cells, head, lastRow) => ({
        style: "display:flex;align-items:stretch" + (head ? ";background:var(--sub)" : lastRow ? "" : ";border-bottom:1px solid var(--line)"),
        cells: cells.map((c, ci) => ({
          text: mdPlain(c),
          style: cellBase + (ci === cells.length - 1 ? ";border-right:none" : "") +
            (head ? ";font-weight:650;color:var(--text)" : ";color:var(--text2)"),
        })),
      });
      return { isTable: true, style: "display:flex;flex-direction:column;border:1px solid var(--line);border-radius:9px;overflow:hidden;background:var(--panel)",
        rows: [row(b.head, true, false)].concat(b.rows.map((r, ri) => row(r, false, ri === b.rows.length - 1))) };
    }
    return { isText: true, runs: mdRuns(b.text), style: MDTXT };
  };
  const mdView = (src) => mdParse(src).map(mdBlock);

  return { mdInline, mdParse, mdPlain, MDRUN, mdRuns, MDTXT, mdBlock, mdView };
})();

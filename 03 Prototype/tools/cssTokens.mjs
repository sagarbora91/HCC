// cssTokens.mjs - generic colour-literal <-> CSS-variable migration helper (CP-3; the one tokenizer since CP-5 merged the .b/.c copies).
//   tokenizeColours(css) -> {css, tokens, stats}   hex literal -> var(--c-<key>) / semantic var
//   resolveTokens(css, tokens) -> css              inverse (var(...) -> hex literal)
// Only hex literals in declaration values are converted. Skipped: url(...), comments, strings,
// selectors/at-rule preludes, and custom-property DEFINITIONS (in any block).
// rgb()/rgba()/hsl()/hsla() and named colours are left untouched (counted in stats).

export const SEMANTIC = new Map([
  ['#7a1f2b', '--primary'], ['#5e1620', '--primary-dark'], ['#f5e4e6', '--primary-tint'],
  ['#2b1d16', '--ink'], ['#6b5547', '--muted'], ['#e6d9c8', '--line'],
  ['#faf6f0', '--bg'], ['#f6efe6', '--sand-2'], ['#b5541e', '--orange'],
]);

const RULE_AT = /^@(media|supports|layer|container|document|-moz-document|keyframes|-webkit-keyframes|scope|starting-style)\b/i;
const NAMED = new Set(['white', 'black', 'transparent', 'currentcolor', 'red', 'green', 'blue', 'gray', 'grey', 'orange', 'yellow', 'purple', 'silver', 'maroon', 'navy', 'teal', 'olive', 'lime', 'aqua', 'fuchsia']);
const isHex = (c) => /[0-9a-fA-F]/.test(c);
const isWord = (c) => /[\w-]/.test(c);

/** key for a hex literal: lowercase, 3-digit expanded to 6, 4-digit to 8; 6/8 kept. */
export function hexKey(lit) {
  let h = lit.slice(1).toLowerCase();
  if (h.length === 3 || h.length === 4) h = h.split('').map((c) => c + c).join('');
  return h;
}

/** skip a comment / string / url(...) starting at i; returns end index (exclusive) or -1 */
function skipOpaque(s, i) {
  const c = s[i];
  if (c === '/' && s[i + 1] === '*') {
    const e = s.indexOf('*/', i + 2);
    return e < 0 ? s.length : e + 2;
  }
  if (c === '"' || c === "'") {
    let j = i + 1;
    while (j < s.length && s[j] !== c) { if (s[j] === '\\') j++; j++; }
    return Math.min(j + 1, s.length);
  }
  if ((c === 'u' || c === 'U') && /^url\(/i.test(s.slice(i, i + 4)) && (i === 0 || !isWord(s[i - 1]))) {
    let j = i + 4;
    while (j < s.length && s[j] !== ')') {
      if (s[j] === '"' || s[j] === "'") { j = skipOpaque(s, j); continue; }
      if (s[j] === '\\') j++;
      j++;
    }
    return Math.min(j + 1, s.length);
  }
  return -1;
}

function newStats() { return { hex: 0, semantic: 0, token: 0, rgb: 0, named: 0 }; }

/** convert one declaration value (already known to be a value) */
function convertValue(v, tokens, stats) {
  let out = '';
  let i = 0;
  while (i < v.length) {
    const e = skipOpaque(v, i);
    if (e >= 0) { out += v.slice(i, e); i = e; continue; }
    const c = v[i];
    if (c === '#' && (i === 0 || !isWord(v[i - 1]))) {
      let j = i + 1;
      while (j < v.length && isHex(v[j])) j++;
      const len = j - i - 1;
      if ([3, 4, 6, 8].includes(len) && (j >= v.length || !isWord(v[j]))) {
        const key = hexKey(v.slice(i, j));
        stats.hex++;
        const sem = key.length === 6 ? SEMANTIC.get('#' + key) : undefined;
        if (sem) { out += `var(${sem})`; stats.semantic++; }
        else { const name = `--c-${key}`; tokens.set(name, '#' + key); out += `var(${name})`; stats.token++; }
        i = j;
        continue;
      }
    }
    if (/[a-zA-Z]/.test(c) && (i === 0 || !isWord(v[i - 1]))) {
      let j = i;
      while (j < v.length && /[\w-]/.test(v[j])) j++;
      const w = v.slice(i, j).toLowerCase();
      if (/^(rgba?|hsla?)$/.test(w) && v[j] === '(') stats.rgb++;
      else if (NAMED.has(w) && v[j] !== '(') stats.named++;
      out += v.slice(i, j);
      i = j;
      continue;
    }
    out += c;
    i++;
  }
  return out;
}

export function tokenizeColours(css) {
  const tokens = new Map();
  const stats = newStats();
  let out = '';
  const stack = []; // {kind:'rules'|'decl', root:boolean}
  let i = 0;
  let prelude = ''; // text since last boundary in a rules context
  let state = 'rules'; // 'rules' | 'prop' | 'value'
  let prop = '';
  let value = '';
  const top = () => stack[stack.length - 1];
  const after = () => (top() && top().kind === 'decl' ? 'prop' : 'rules');
  const flushValue = () => {
    if (prop.trim().startsWith('--')) out += value; // custom-property definitions are never rewritten (anywhere; .c copy folded in)
    else out += convertValue(value, tokens, stats);
    value = '';
  };
  while (i < css.length) {
    const e = skipOpaque(css, i);
    if (e >= 0) {
      const chunk = css.slice(i, e);
      if (state === 'value') value += chunk;
      else if (state === 'prop') { prop += chunk; out += chunk; }
      else { prelude += chunk; out += chunk; }
      i = e;
      continue;
    }
    const c = css[i];
    if (state === 'rules') {
      out += c;
      if (c === '{') {
        const p = prelude.replace(/\/\*[\s\S]*?\*\//g, '').trim();
        if (RULE_AT.test(p)) stack.push({ kind: 'rules', root: false });
        else { stack.push({ kind: 'decl', root: /^:root$/.test(p) }); state = 'prop'; prop = ''; }
        prelude = '';
      } else if (c === '}') {
        stack.pop(); prelude = ''; state = after(); prop = '';
      } else if (c === ';') prelude = '';
      else prelude += c;
      i++;
      continue;
    }
    if (state === 'prop') {
      if (c === ':') { state = 'value'; out += c; value = ''; }
      else if (c === '{') { stack.push({ kind: 'decl', root: false }); out += c; prop = ''; }
      else if (c === '}') { out += c; stack.pop(); state = after(); prelude = ''; prop = ''; }
      else { out += c; if (c === ';') prop = ''; else prop += c; }
      i++;
      continue;
    }
    // state === 'value'
    if (c === ';') { flushValue(); out += c; state = 'prop'; prop = ''; }
    else if (c === '}') { flushValue(); out += c; stack.pop(); state = after(); prelude = ''; prop = ''; }
    else if (c === '{') { out += value + c; value = ''; stack.push({ kind: 'decl', root: false }); state = 'prop'; prop = ''; }
    else value += c;
    i++;
  }
  if (state === 'value') flushValue();
  const sorted = new Map([...tokens.entries()].sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0)));
  return { css: out, tokens: sorted, stats, left: { fn: stats.rgb, named: stats.named } };
}

/** inverse: var(--c-xxx) / var(--semantic) -> literal colour. Unknown vars are untouched. */
export function resolveTokens(css, tokens) {
  const map = new Map();
  for (const [hex, name] of SEMANTIC) map.set(name, hex);
  for (const [name, val] of tokens) map.set(name, val);
  return css.replace(/var\(\s*(--[\w-]+)\s*\)/g, (m, name) => (map.has(name) ? map.get(name) : m));
}

/** :root block text for a token map (sorted by key, one per line) */
export function tokenSheet(tokens, header) {
  const lines = [...tokens.entries()].sort((a, b) => (a[0] < b[0] ? -1 : a[0] > b[0] ? 1 : 0)).map(([n, v]) => `${n}:${v};`);
  return `${header}\n:root{\n${lines.join('\n')}\n}\n`;
}

/** The same semantic mapping as variable -> '#hex' (resolveTokens input, styles.css :root values). */
export const SEMANTIC_TOKENS = new Map([...SEMANTIC].map(([hex, name]) => [name, hex]));

/** Lowercase every hex literal and expand #abc -> #aabbcc, #abcd -> #aabbccdd, so original and resolved CSS compare equal. */
export function canonHex(css) {
  return css.replace(/#([0-9a-fA-F]{3}|[0-9a-fA-F]{4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})(?![\w-])/g, (m) => '#' + hexKey(m));
}

// ── The single token sheet (CP-5): src/styles/0-tokens.css holds every --c-<hex> light value, sorted, one per line. ──
export const TOKEN_SHEET = 'src/styles/0-tokens.css';
export const TOKEN_HEADER = '/* Colour tokens: the light palette, one --c-<hex> per colour used in the stylesheets (CP-3, merged into one sheet by CP-5). Generated: tools/cssTokens.mjs; themes re-point them in themes.css */';
/** --c-name -> '#hex' for every definition line of the token sheet. */
export function parseTokenSheet(text) {
  return new Map([...text.matchAll(/^(--c-[0-9a-f]+):(#[0-9a-f]+);$/gm)].map((m) => [m[1], m[2]]));
}
/** The canonical 0-tokens.css text for a token map. */
export const tokenSheetFile = (tokens) => tokenSheet(tokens, TOKEN_HEADER);

// ── CP-5 role rewrites: the few places where one colour plays two roles that a dark theme must separate ──
// A colour used as a solid fill BEHIND white text (e.g. maroon buttons) and the same colour used AS text on a surface cannot both
// stay readable once surfaces turn dark, so solid fills get their own role (--*-fill) and white text becomes --on-primary.
// Light values of every role equal the hex they replace (tools/themes.mjs ROLES), so the light theme renders identically.
/** var() inside background / background-color / background-image -> fill role */
export const FILL_ROLES = new Map([
  ['--primary', '--primary-fill'], ['--primary-dark', '--primary-fill-dark'], ['--c-2e6b34', '--success-fill'],
  ['--c-9a3b2a', '--danger-fill'], ['--c-8a5a10', '--warn-fill'], ['--c-4c7084', '--info-fill'],
]);
const BG_PROP = /^background(-color|-image)?$/;
const BORDER_PROP = /^border(-(top|right|bottom|left))?(-color)?$/;
const ONFILL_PROP = /^(color|outline|outline-color|accent-color)$/;
/**
 * applyRoles(css) -> css. Per rule:
 *  1. background* values: var(--primary|--primary-dark|--c-2e6b34|--c-9a3b2a|--c-8a5a10|--c-4c7084) -> the matching --*-fill role;
 *  2. in a rule changed by 1, border* values: var(--primary) / var(--primary-dark) -> --primary-fill / --primary-fill-dark;
 *  3. color / outline / outline-color / accent-color: var(--c-ffffff) -> var(--on-primary) (white text on a fill), unless the rule's
 *     own background is white (then it is a surface colour, e.g. a hidden tick);
 *  4. color: var(--primary-tint) -> var(--on-primary-soft) (pale text on a maroon hero).
 * Deterministic and idempotent; comments and everything else are copied as they are.
 */
export function applyRoles(css) {
  return css.replace(/\{([^{}]*)\}/g, (whole, body) => {
    if (body.includes('/*')) return whole;
    const decl = /(^|;)(\s*)([a-z-]+)(\s*:\s*)([^;]*)/g;
    const bgWhite = /(^|;)\s*background(-color)?\s*:\s*var\(--c-ffffff\)/.test(body);
    let filled = false;
    let out = body.replace(decl, (m, sep, sp, prop, colon, val) => {
      if (!BG_PROP.test(prop) || /url\(/i.test(val)) return m;
      const nv = val.replace(/var\((--[\w-]+)\)/g, (v, n) => (FILL_ROLES.has(n) ? `var(${FILL_ROLES.get(n)})` : v));
      if (nv !== val) filled = true;
      return sep + sp + prop + colon + nv;
    });
    const filledNow = filled || /(^|;)\s*background(-color|-image)?\s*:[^;]*var\(--[\w-]+-fill(-dark)?\)/.test(out);
    out = out.replace(decl, (m, sep, sp, prop, colon, val) => {
      let nv = val;
      if (filledNow && BORDER_PROP.test(prop)) nv = nv.replace(/var\(--primary\)/g, 'var(--primary-fill)').replace(/var\(--primary-dark\)/g, 'var(--primary-fill-dark)');
      if (ONFILL_PROP.test(prop) && !bgWhite) nv = nv.replace(/var\(--c-ffffff\)/g, 'var(--on-primary)');
      if (prop === 'color') nv = nv.replace(/var\(--primary-tint\)/g, 'var(--on-primary-soft)');
      return sep + sp + prop + colon + nv;
    });
    return '{' + out + '}';
  });
}

// cssTokens.mjs - generic colour-literal <-> CSS-variable migration helper (CP-3).
//   tokenizeColours(css) -> {css, tokens, stats}   hex literal -> var(--c-<key>) / semantic var
//   resolveTokens(css, tokens) -> css              inverse (var(...) -> hex literal)
// Only hex literals in declaration values are converted. Skipped: url(...), comments, strings,
// selectors/at-rule preludes, and custom-property DEFINITIONS inside :root blocks.
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
    const root = top() && top().root;
    if (root && prop.trim().startsWith('--')) out += value;
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
  return { css: out, tokens: sorted, stats };
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

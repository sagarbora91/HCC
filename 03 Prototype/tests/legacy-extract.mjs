// LG lane lg-extract: runs the legacy extractor's fictional self-test (tools/legacy/extract.py --self-test).
// Standalone:  node tests/legacy-extract.mjs   |   as a suite: export register({test,assert}) (add a stream-*.mjs re-export to include it in npm test).
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import path from 'node:path';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const script = path.join(root, 'tools', 'legacy', 'extract.py');

function findPython() {
  for (const c of ['python', 'python3', 'py']) {
    const r = spawnSync(c, ['-I', '--version'], {encoding: 'utf8'});
    if (r.status === 0) return c;
  }
  return null;
}

export function runSelfTest() {
  const py = findPython();
  if (!py) return {skipped: true, message: 'python not found - legacy extractor self-test skipped'};
  const r = spawnSync(py, ['-I', script, '--self-test'], {encoding: 'utf8', cwd: root});
  return {skipped: false, status: r.status, out: (r.stdout || '') + (r.stderr || '')};
}

export function register({test, assert}) {
  test('[LG-EXTRACT] extract.py --self-test passes on the fictional fixture', () => {
    const r = runSelfTest();
    if (r.skipped) { console.log('  ' + r.message); return; }
    assert.equal(r.status, 0, r.out);
    assert.ok(/self-test OK/.test(r.out), r.out);
  });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const r = runSelfTest();
  if (r.skipped) { console.log(r.message); process.exit(0); }
  console.log(r.out.trim());
  process.exit(r.status === 0 ? 0 : 1);
}

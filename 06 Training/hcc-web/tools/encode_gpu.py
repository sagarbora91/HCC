"""Step 8: ffmpeg encode. One segment per slide (still PNG + its audio, length = max(min_seconds, audio + pad)), then a stream-copy concat per chapter and for the full course.
usage: python encode.py --config training.config.json --lang en [--only 1,2]
ONE ffmpeg at a time with -threads 4 (no-heat rule; never run two languages in parallel). Resumable: existing seg/NNN.mp4 are kept.
Outputs: <output.dir>/video/<lang>/chNN.mp4 and <output.dir>/<name><suffix>.mp4 (full course; suffix from languages.<lang>.suffix)."""
import json, os, subprocess, sys
sys.path.insert(0, r'C:/Users/Sagar/.claude/skills/training-media/scripts')  # HCC copy of the skill's encode.py: Intel QSV (owner rule: always encode on GPU), still frames at 2 fps
import tmlib

ap = tmlib.parser(__doc__); ap.add_argument('--lang', required=True); ap.add_argument('--only')
a = ap.parse_args(); cfg = tmlib.load(a.config); L = cfg['languages'][a.lang]; E = cfg.get('encode', {})
FF, FP = tmlib.tool(cfg, 'ffmpeg'), tmlib.tool(cfg, 'ffprobe')
TH = str(E.get('threads', 4)); FPS = str(E.get('fps', 30)); MINS = float(E.get('min_seconds', 4.0)); PAD = float(E.get('pad_seconds', 1.0)); ABR = E.get('audio_bitrate', '96k')
man = tmlib.manifest(cfg)['slides']
work = tmlib.out(cfg, 'work', a.lang); aud = os.path.join(work, 'audio'); seg = tmlib.mk(os.path.join(work, 'seg'))
png = tmlib.out(cfg, 'deck', 'png'); vdir = tmlib.mk(tmlib.out(cfg, 'video', a.lang))
def audio(i):
    for e in ('wav', 'mp3'):
        p = os.path.join(aud, '%03d.%s' % (i, e))
        if os.path.exists(p): return p
    sys.exit('no audio for slide %d (run tts.py --lang %s)' % (i, a.lang))
def dur(p): return float(subprocess.check_output([FP, '-v', 'error', '-show_entries', 'format=duration', '-of', 'csv=p=0', p]).decode().strip())
only = None
if a.only:
    only = set()
    for part in a.only.split(','):
        if '-' in part: x, y = part.split('-'); only |= set(range(int(x), int(y) + 1))
        else: only.add(int(part))
durs = {}
for m in man:
    i = m['n']
    if only and i not in only: continue
    A = audio(i); d = max(MINS, dur(A) + PAD); durs[i] = d; out = os.path.join(seg, '%03d.mp4' % i)
    if os.path.exists(out): continue
    subprocess.run([FF, '-y', '-loglevel', 'error', '-threads', TH, '-loop', '1', '-framerate', '2', '-i', os.path.join(png, 's-%03d.png' % i), '-i', A,
                    '-af', 'aresample=44100,apad=whole_dur=%.3f' % d, '-ac', '2', '-t', '%.3f' % d, '-c:v', 'h264_qsv', '-preset', 'medium', '-global_quality', '23', '-pix_fmt', 'nv12',
                    '-r', '2', '-g', '4', '-c:a', 'aac', '-b:a', ABR, '-threads', TH, out], check=True)
    print('seg', i, end='\r')
json.dump(durs, open(os.path.join(work, 'durations.json'), 'w'))
def cat(idx, out):
    lst = os.path.join(work, 'l.txt'); open(lst, 'w').write(''.join("file 'seg/%03d.mp4'\n" % i for i in idx))
    subprocess.run([FF, '-y', '-loglevel', 'error', '-f', 'concat', '-safe', '0', '-i', lst, '-c', 'copy', '-threads', TH, out], check=True)
if only: print('\npartial run: segments only'); sys.exit(0)
chs = []
for m in man:
    if m['chapter'] is not None and m['kind'] in ('chapter', 'step', 'recap'): chs.append(m)
for cid in dict.fromkeys(m['chapter'] for m in chs):
    idx = [m['n'] for m in chs if m['chapter'] == cid]
    cat(idx, os.path.join(vdir, 'ch%02d.mp4' % int(cid)))
full = tmlib.out(cfg, tmlib.name(cfg) + L.get('suffix', '') + '.mp4'); cat([m['n'] for m in man], full)
tot = sum(durs.values()); print('\nfull course', full, '%dh %02dmin' % (tot // 3600, tot % 3600 // 60), 'size MB', round(os.path.getsize(full) / 1e6, 1))

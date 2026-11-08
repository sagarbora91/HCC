"""Authoring script for steps.json (HCC phone course, mobile shell 412x915). Edit THIS and the chapters_*.py files, never steps.json itself.
Run: python make_steps.py  -> writes steps.json.
Target syntax (driver resolve(), 03 Prototype/src/app/demo/training/targets.ts):
  sel:<css> aria:<label> btn:<text> tab:<label> field:<label> row:<text> modalbtn:<text> menutop:<Category> menuleaf:<Page> tr:<id> text:<text>
Acts (driver doAct, same folder acts.ts): nav: user: click: type:<target>|<value> select:<target>|<option> check: uncheck: key: menu: modal: wait: js: display: signout:
Capture config sets menuLeafPattern to a never-matching value, so menuleaf: targets are outlined like any other (pre-state fallback when the tile vanishes)."""
import json, os
HERE = os.path.dirname(os.path.abspath(__file__))
GUEST = {
    'name': 'Rahul Deshmukh', 'title': 'Mr.', 'city': 'Pune', 'mobile': '98XXXXXX01', 'aadhaar': 'XXXX XXXX 4321', 'car': 'MH 12 AB 1234',
    'companion': 'Mrs. Sneha Deshmukh', 'room': '203', 'type': 'Deluxe', 'plan': 'CP', 'rate': '3,000', 'arrival': '24 Oct 2026', 'departure': '26 Oct 2026',
    'note': 'fictional; same person as in the Manual System training. Business date 24 Oct 2026 (fixture); demo hotel 24 rooms 101-308.'}
CH = []; _cur = None; _n = 0

def chapter(cid, title, goal, user, uid, menu='', display=''):
    global _cur, _n
    _cur = {'id': cid, 'title': title, 'goal': goal, 'user': user, 'uid': uid, 'menu': menu, 'display': display, 'steps': []}; CH.append(_cur); _n = 0

def step(say, why='', target=None, auto=None, check=None, expect='', value=None, msg=None, msgText=None, msgwhy=None, form='', menu='', key='', action=None, hint_mr=None, allowError=False):
    global _n
    _n += 1; sid = '%s.%d' % (_cur['id'], _n)
    s = {'id': sid, 'say': say, 'why': why, 'form': form, 'menu': menu, 'key': key, 'target': target or '', 'action': action or ('click' if auto else 'read'), 'value': value or '',
         'expect': expect, 'check': check or '', 'auto': auto or [], 'shot': '%02d_%02d.png' % (_cur['id'], _n), 'msg': msg, 'msgText': msgText, 'msgwhy': msgwhy or '', 'hint_mr': hint_mr or ''}
    if allowError: s['allowError'] = True
    _cur['steps'].append(s); return s

def read(say, why='', target=None, **kw): return step(say, why, target=target, action='read', auto=kw.pop('auto', None), **kw)
def click(target, say, why='', outline=None, **kw): return step(say, why, target=outline or target, auto=['click:' + target] + kw.pop('also', []), action='click', **kw)
def typ(target, value, say, why='', **kw): return step(say, why, target=target, auto=['type:%s|%s' % (target, value)], value=kw.pop('value', value), action='type', **kw)
def pick(target, option, say, why='', **kw): return step(say, why, target=target, auto=['select:%s|%s' % (target, option)], value=kw.pop('value', option), action='select', **kw)
def tick(target, say, why='', **kw): return step(say, why, target=target, auto=['check:' + target], action='click', **kw)
def press(keys, say, why='', target=None, **kw): return step(say, why, target=target, auto=['key:' + keys], key=keys, action='key', **kw)
def tile(cat, say, why='', **kw): return step(say, why, target='menutop:' + cat, auto=['click:menutop:' + cat], menu=cat, action='click', **kw)
def leaf(cat, page, say, why='', **kw): return step(say, why, target='menuleaf:' + page, auto=['click:menuleaf:' + page], menu=cat + ' > ' + page, action='click', **kw)

for part in sorted(f for f in os.listdir(HERE) if f.startswith('chapters_') and f.endswith('.py')):
    exec(open(os.path.join(HERE, part), encoding='utf-8').read())

json.dump({'guest': GUEST, 'version': '2026-11-07', 'chapters': CH}, open(os.path.join(HERE, 'steps.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
print('chapters', len(CH), 'steps', sum(len(c['steps']) for c in CH))

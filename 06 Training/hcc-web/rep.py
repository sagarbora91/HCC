import json, sys, io
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
r = json.load(io.open('build/shots/capture_report.json', encoding='utf-8'))
print('FAILS', json.dumps(r['fails'], ensure_ascii=False)); print('NOHL', r['nohl'])

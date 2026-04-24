import json, re

with open('signals.json', encoding='utf-8') as f:
    data = json.load(f)

msgs = data.get('messages', data) if isinstance(data, dict) else data
close_msgs = [m for m in msgs if isinstance(m.get('text'), str) and re.search(r'close', m['text'], re.I)]
print('Total close messages: ' + str(len(close_msgs)))
for m in close_msgs:
    txt = m['text'][:150].replace('\n', ' ')
    mid = m['id']
    print('  id=' + str(mid) + ': ' + repr(txt))

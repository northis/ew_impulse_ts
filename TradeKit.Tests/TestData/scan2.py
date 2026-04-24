import json

with open('signals.json', encoding='utf-8') as f:
    data = json.load(f)

msgs = data.get('messages', data) if isinstance(data, dict) else data
msg_map = {m['id']: m for m in msgs}

target_ids = [11540, 11488, 11489, 6130, 6461, 6587, 6675, 5967, 6762]
for tid in target_ids:
    m = msg_map.get(tid)
    if m:
        reply = m.get('reply_to_msg_id')
        text = str(m.get('text',''))[:120].replace('\n', ' ')
        print('id=' + str(tid) + ' replyTo=' + str(reply) + ' text=' + repr(text))
    else:
        print('id=' + str(tid) + ' NOT FOUND')

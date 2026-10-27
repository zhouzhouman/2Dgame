"""抽取序章全部文本，生成可本地化的文本表
输入：举火_序章数据_全关卡_v1.1.csv
输出：举火_文本表_v1.0.csv（text_id / hub / point_id / field / zh / en / ja）
用法：python tools/extract_texts.py
"""
import csv, os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SRC = os.path.join(ROOT, '举火_序章数据_全关卡_v1.1.csv')
DST = os.path.join(ROOT, '举火_文本表_v1.0.csv')

def main():
    with open(SRC, encoding='utf-8-sig') as f:
        rows = list(csv.DictReader(f))

    out, n = [], 0
    for r in rows:
        for field in ('text_once', 'text_repeat'):
            n += 1
            out.append(dict(text_id='T%04d' % n,
                            hub=r['hub'],
                            point_id=r['id'],
                            field='once' if field == 'text_once' else 'repeat',
                            zh=r[field],
                            en='', ja=''))

    with open(DST, 'w', encoding='utf-8-sig', newline='') as f:
        w = csv.DictWriter(f, fieldnames=['text_id', 'hub', 'point_id', 'field', 'zh', 'en', 'ja'])
        w.writeheader()
        w.writerows(out)

    total = sum(len(o['zh']) for o in out)
    longest = max(out, key=lambda o: len(o['zh']))
    chars = set(''.join(o['zh'] for o in out))
    print('OK 文本条数:', len(out))
    print('总字数: %d ｜ 去重字符: %d' % (total, len(chars)))
    print('最长: %s（%d 字）' % (longest['text_id'], len(longest['zh'])))
    print('平均: %.1f 字/条' % (total / len(out)))
    print('写盘:', DST)

if __name__ == '__main__':
    main()

from pathlib import Path
import re,json
root=Path(__file__).resolve().parents[2]
s=(root/'docs/cards-v0.6.md').read_text();cards=[]
for m in re.finditer(r'(?ms)^#### WCG-(\d{3}) .*?(?=^#### |^## |\Z)',s):
 b=m[0];n=int(m[1])
 def f(k):
  q=re.search(rf'\*\*{re.escape(k)}\*\*：([^\n]+)',b);return q[1] if q else ''
 c={'id':f'WCG-{n:03d}','name':f('卡名'),'type':f('類型'),'will':f('意志屬性'),'cost_spec':'','cost_gen':int(re.search(r'\d+',f('費用'))[0]),'total_cost':int(re.search(r'\d+',f('費用'))[0]),'pp':int(f('PP（戰鬥值）')) if f('PP（戰鬥值）') else None,'dp':int(f('DP（玩家傷害）')) if f('DP（玩家傷害）') else None,'text':next(l[4:] for l in b.splitlines() if l.startswith('  > ')),'design_note':f('設計備註'),'hs_ref':f('爐石參考原型').strip('`')}
 c['keywords']=[k for k in ['嘲諷','衝鋒','劇毒','貫穿','聖盾'] if c['type']=='怪物' and re.match(r'^(?:嘲諷；|衝鋒；|聖盾；|貫穿；|劇毒；)*'+k+r'[。；]',c['text'])]
 c['arrows']= {61:['right'],73:['left','right'],147:['left','up']}.get(n,[])
 cards.append(c)
(root/'WcgWeb/Data/cards.json').write_text(json.dumps(cards,ensure_ascii=False,indent=2)+'\n')

assert len(cards)==199
print("199 張 v0.6 卡牌已從設計草案同步。")

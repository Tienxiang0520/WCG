#!/usr/bin/env python3
"""Play one isolated ranked UI match using agent-browser and DOM observations.

This is an interaction smoke test, not the AI used by BalanceLeague and not a human win-rate estimate.
Requires a running ranked battle on the dedicated localhost test port. Never attaches to user browsers.
python3 tools/BrowserRanked/check_ranked.py --session wcg-balance-20261010 --port 5186 --out output/balance/browser-match
"""
import argparse, json, subprocess
from pathlib import Path

parser=argparse.ArgumentParser()
parser.add_argument('--session',required=True)
parser.add_argument('--port',type=int,default=5186)
parser.add_argument('--out',required=True)
parser.add_argument('--max-actions',type=int,default=180)
args=parser.parse_args()
if not args.session.startswith('wcg-balance-') or args.port in (5180,5179):
    raise SystemExit('Use a dedicated balance-test session and test port.')
target=f'http://127.0.0.1:{args.port}/'
records=[];filled=set()
base=['agent-browser','--session',args.session,'--json']
def run(*command,stdin=None):
    result=subprocess.run(base+list(command),input=stdin,text=True,capture_output=True,timeout=35)
    data=json.loads(result.stdout)
    if not data.get('success'):raise RuntimeError(data.get('error') or result.stderr)
    return data.get('data',{})
script=r'''(() => {
 const num=e=>Number(e?.textContent)||0;
 const card=e=>({id:e.dataset.dragId,name:e.getAttribute('aria-label'),pp:num(e.querySelector('.field-stats b')),dp:num(e.querySelector('.field-stats b:last-child')),shield:e.classList.contains('has-shield'),taunt:e.classList.contains('has-taunt'),poison:e.classList.contains('is-poisonous'),slot:e.parentElement.dataset.slot});
 return {url:location.href,turn:document.querySelector('.turn-chip')?.textContent||'',message:document.querySelector('.battle-message')?.textContent||'',
 result:document.querySelector('[aria-label="天梯結算"]')?.textContent||null,
 energy:document.querySelector('[data-drop-zone=energy]')?.textContent,
 dialog:document.querySelector('.v06-dialog')?.getAttribute('aria-label')||null,
 choices:[...document.querySelectorAll('.v06-dialog button')].map((e,i)=>({text:e.textContent,index:i})),
 target:document.querySelector('.target-banner')?.textContent||null,
 targets:[...document.querySelectorAll('.v06-slot.legal')].map(e=>({selector:'.v06-slot[data-drop-zone="'+e.dataset.dropZone+'"][data-slot="'+e.dataset.slot+'"]',enemy:e.dataset.dropZone==='enemy',pp:num(e.querySelector('.field-stats b'))})),
 own:[...document.querySelectorAll('[data-drop-zone=own] .field-card')].map(card),
 ready:[...document.querySelectorAll('.field-card[data-drag-kind=attack]')].map(card),
 enemy:[...document.querySelectorAll('[data-drop-zone=enemy] .field-card')].filter(e=>e.querySelector('.field-stats')).map(card),
 empty:[...document.querySelectorAll('[data-drop-zone=own].vacant')].map(e=>e.dataset.slot),
 hand:[...document.querySelectorAll('.v06-hand-card')].map(e=>({id:e.dataset.dragId,name:e.querySelector('strong')?.textContent,cost:Number(e.dataset.cardCost),type:e.dataset.cardType,available:e.classList.contains('available')})),
 endEnabled:!document.querySelector('.end-turn')?.disabled};
})()'''
def observe():
    s=run('eval','--stdin',stdin=script)['result']
    if not s['url'].startswith(target) or '/ranked' not in s['url']:raise RuntimeError('Wrong browser origin or page; refusing input.')
    if not s['turn'] and not s.get('result'):raise RuntimeError('No ranked battle is open.')
    return s
def action(kind,*command):
    run(*command);records.append({'action':kind,'command':list(command)})
def visible_point(selector):
    # A fanned hand intentionally extends below the viewport; its bounding-box centre can be off-screen.
    # Find a visible hit-tested point, then use real mouse input rather than invoking the game engine.
    js="""(() => { const selector=SELECTOR, e=document.querySelector(selector);if(!e)throw new Error('Missing target');
      const r=e.getBoundingClientRect();const xs=[.5,.25,.75,.1,.9],ys=[.5,.1,.25,.75,.9];
      const hit=(x,y)=>x>0&&x<innerWidth&&y>0&&y<innerHeight&&document.elementFromPoint(x,y)?.closest(selector);
      for(const yf of ys)for(const xf of xs){let x=r.left+r.width*xf,y=r.top+r.height*yf;if(hit(x,y))return [x,y];}
      for(let y=Math.max(2,r.top+2);y<Math.min(innerHeight-2,r.bottom);y+=5)for(const xf of xs){let x=r.left+r.width*xf;if(hit(x,y))return [x,y];}
      return false;})()""".replace('SELECTOR',json.dumps(selector))
    run('wait','--fn',js)
    return run('eval','--stdin',stdin=js)['result']
def drag(card,destination):
    source=f'[data-drag-id="{card["id"]}"]'
    start=visible_point(source)
    if not isinstance(start,list):raise RuntimeError('Unexpected point: '+repr(start))
    start=[round(n) for n in start];run('mouse','move',str(start[0]),str(start[1]))
    run('eval','--stdin',stdin='new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)))')
    start=[round(n) for n in visible_point(source)];end=[round(n) for n in visible_point(destination)]
    run('mouse','move',str(start[0]),str(start[1]));run('mouse','down','left')
    run('mouse','move',str(start[0]),str(start[1]-15))
    run('wait','--fn','!!document.querySelector(".v06-board.dragging")')
    if destination=='[data-drop-zone=energy]':
        allowed=run('eval','--stdin',stdin='!!document.querySelector("[data-drop-zone=energy].legal")')['result']
        if not allowed:
            run('mouse','up','left');records.append({'action':'energy already filled; skip'});return
    if destination=='[data-drop-zone=center]':
        legal=run('eval','--stdin',stdin=r'''({center:!!document.querySelector("[data-drop-zone=center].legal"),slots:[...document.querySelectorAll(".v06-slot.legal")].map(e=>({selector:'.v06-slot[data-drop-zone="'+e.dataset.dropZone+'"][data-slot="'+e.dataset.slot+'"]',enemy:e.dataset.dropZone==="enemy",pp:Number(e.querySelector(".field-stats b")?.textContent)||0}))})''')['result']
        if not legal['center']:
            if not legal['slots']:
                run('mouse','up','left');raise RuntimeError('Playable card has no highlighted drop target.')
            destination=max(legal['slots'],key=lambda t:(t['enemy'],t['pp']))['selector']
        end=[round(n) for n in visible_point(destination)]
    run('mouse','move',str(end[0]),str(end[1]));run('mouse','up','left')
    records.append({'action':'mouse drag '+str(card['name']),'source':source,'target':destination})
    source_kind='hand' if card.get('type') else 'attack'
    origin_selector=('.v06-hand-card' if source_kind=='hand' else '.field-card')+source
    condition='!document.querySelector('+json.dumps(origin_selector)+') || document.querySelector('+json.dumps(origin_selector)+')?.dataset.dragKind!=='+json.dumps(source_kind)+' || !!document.querySelector(".v06-dialog") || !!document.querySelector(".target-banner")'
    run('wait','--fn',condition)

out=Path(args.out);out.parent.mkdir(parents=True,exist_ok=True)
try:
    # Reduced effects speeds a UI smoke run without changing rules or AI.
    run('find','role','button','click','--name','切換為精簡特效')
except RuntimeError:
    pass
try:
    for index in range(args.max_actions):
        s=observe()
        records.append({'state':{k:s.get(k) for k in ('turn','message','result','dialog','target')}})
        if s.get('result'):
            run('screenshot',str(out.with_suffix('.png')))
            out.with_suffix('.json').write_text(json.dumps({'policy':__doc__,'result':s['result'],'actions':records},ensure_ascii=False,indent=2))
            print(s['result']);break
        if '動作進行中' in s['message'] or ('電腦回合' in s['turn'] and not s['choices'] and not s['target']):
            run('wait','--fn','!document.querySelector(".battle-message")?.textContent.includes("動作進行中") && (document.querySelector(".turn-chip")?.textContent.includes("你的回合") || !!document.querySelector(".v06-dialog") || !!document.querySelector(".target-banner") || !!document.querySelector("[aria-label=天梯結算]"))')
            continue
        if s['choices']:
            choices=s['choices']
            chosen=next((c for c in choices if '確認填能量' in c['text']),None)
            if chosen is None:
                chosen=next((c for c in choices if '聖盾' in c['text'] and ('支付' in c['text'] or '使用' in c['text'])),choices[0])
            action('choice '+chosen['text'],'find','nth',str(chosen['index']),'.v06-dialog button','click')
            continue
        if s['target']:
            options=s['targets']
            if not options:raise RuntimeError('Target prompt has no legal UI targets.')
            if '犧牲' in s['target'] or '代價' in s['target']:chosen=min(options,key=lambda t:t['pp'])
            else:chosen=max(options,key=lambda t:(t['enemy'],t['pp']))
            action('target','click',chosen['selector']);continue
        turn=s['turn'];energy=[int(x) for x in __import__('re').findall(r'\d+',s['energy'] or '')][:2]
        if turn not in filled and s['hand'] and energy and energy[1]<6:
            filled.add(turn);card=max(s['hand'],key=lambda c:(c['cost'],c['type']!='怪物'))
            drag(card,'[data-drop-zone=energy]');continue
        attacked=False
        for attacker in sorted(s['ready'],key=lambda c:-c['dp']):
            foes=[e for e in s['enemy'] if e['taunt']] or s['enemy']
            if not any(e['taunt'] for e in s['enemy']) and attacker['dp']>0:
                drag(attacker,'[data-drop-zone=face]');attacked=True;break
            good=[e for e in foes if attacker['poison'] or attacker['pp']>e['pp'] or attacker['shield']]
            if good:
                victim=max(good,key=lambda c:c['pp']);drag(attacker,f'[data-drop-zone=enemy][data-slot="{victim["slot"]}"]');attacked=True;break
        if attacked:continue
        playable=[c for c in s['hand'] if c['available'] and (s['empty'] or c['type']!='怪物')]
        if playable:
            card=max(playable,key=lambda c:(c['type']=='怪物',-c['cost']))
            destination=f'[data-drop-zone=own][data-slot="{s["empty"][0]}"]' if card['type'] in ('怪物','結界') and s['empty'] else '[data-drop-zone=center]'
            drag(card,destination);continue
        if s['endEnabled']:action('end turn','click','.end-turn')
        else:raise RuntimeError('No legal action or wait condition.')
    else:
        out.with_suffix('.json').write_text(json.dumps({'incomplete':True,'actions':records},ensure_ascii=False,indent=2))
        raise SystemExit('UI action limit reached; no match result.')
except Exception as error:
    try:
        run('mouse','up','left')
    except Exception:
        pass
    out.with_suffix('.json').write_text(json.dumps({'incomplete':True,'error':str(error),'actions':records},ensure_ascii=False,indent=2))
    raise

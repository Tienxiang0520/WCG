import Phaser from 'phaser';
import {readEnergyConfirmation,saveEnergyConfirmation} from './preferences';
import type {Card,Command,Event,Hand,Monster,Receiver,Response,State,Target} from './contracts';

const colors:Record<string,number>={'狂怒':0xee7955,'理智':0x67baff,'生機':0x8fd2a5,'秩序':0xe3ca83,'深淵':0xc49cec,'中立':0xa9b5c5};
const font='"Noto Sans CJK TC", "Microsoft JhengHei", sans-serif';
function el<K extends keyof HTMLElementTagNameMap>(tag:K,text='',cls=''):HTMLElementTagNameMap[K]{const n=document.createElement(tag);n.textContent=text;if(cls)n.className=cls;return n;}
function button(text:string,run:()=>void,disabled=false){const b=el('button',text,'battle-client-button');b.type='button';b.disabled=disabled;b.onclick=run;return b;}

// Every drawing, hit zone and animation uses this geometry. Focus mode uses CSS
// pixels as logical pixels so fitting a short viewport never shrinks card labels.
function boardLayout(width:number,height:number,focus:boolean){
    const compact=width<800;
    if(compact&&focus){const cardHeight=Math.min(130,Math.floor((height-220)/3)),cardWidth=Math.floor((width-32)/5)-6;
        return {focus,width,height,cardWidth,cardHeight,perPage:Math.max(2,Math.floor((width-116)/(cardWidth+5))),handStart:108,computerLabel:77,computerEnergy:99,
            computerField:Math.max(140+cardHeight/2,height*.27),playerLabel:height*.69,playerEnergy:height-cardHeight/2-54,
            playerField:height*.56,hand:height-cardHeight/2-54,fieldHeight:cardHeight+4,divider:height*.42};}

    const cardWidth=focus?Math.min(130,Math.floor((width-184)/7)-5):(compact?96:138);
    const cardHeight=focus?Math.min(170,Math.floor((height-140)/3)):(compact?138:166);
    return {focus,width,height,cardWidth,cardHeight,handStart:compact?108:156,perPage:Math.min(7,Math.floor((width-(compact?124:172))/(cardWidth+5))),
        computerLabel:focus?38:28,computerEnergy:focus?58:72,
        computerField:focus?72+cardHeight/2:195,
        playerLabel:focus?84+cardHeight:538,playerEnergy:focus?104+cardHeight:563,
        playerField:focus?120+cardHeight*1.5:428,hand:focus?height-cardHeight/2-10:660,
        fieldHeight:focus?cardHeight+4:186,divider:focus?cardHeight+80:305};
}

// Software WebGL can consume several CPU cores for an otherwise simple 2D board.
// Prefer Phaser's Canvas renderer there; hardware WebGL retains the normal AUTO path.
function rendererType():number {
    try {
        const canvas=document.createElement('canvas');
        const gl=canvas.getContext('webgl');if(!gl)return Phaser.CANVAS;
        const extension=gl.getExtension('WEBGL_debug_renderer_info');
        const renderer=extension?String(gl.getParameter(extension.UNMASKED_RENDERER_WEBGL)):'';
        gl.getExtension('WEBGL_lose_context')?.loseContext();
        return /swiftshader|llvmpipe|softpipe|software/i.test(renderer)?Phaser.CANVAS:Phaser.AUTO;
    } catch { return Phaser.CANVAS; }
}

class BattleScene extends Phaser.Scene {
    owner!:Board;
    pieces=new Map<string,Phaser.GameObjects.Container>();
    borders=new Map<string,Phaser.GameObjects.Rectangle>();
    zones=new Map<string,{x:number;y:number;width:number;height:number}>();
    selected:string|null=null;
    drag:string|null=null;
    arrows!:Phaser.GameObjects.Graphics;
    heroHp=new Map<string,Phaser.GameObjects.Text>();
    sideLabels=new Map<string,Phaser.GameObjects.Text>();
    energyLabels=new Map<string,Phaser.GameObjects.Text>();
    visualEnergy=new Map<string,{available:number;total:number;used:number}>();
    visualFields=new Map<string,string[]>();
    visualHp=new Map<string,number>();
    ready=false;
    suppressUntil=0;
    lastFrame=0;
    painted=new Set<string>();
    homes=new Map<string,{x:number;y:number}>();
    visualSequence=0;
    spell:{card:Card;side:string;committed:boolean}|null=null;
    attack:{id:string;side:string;dead:boolean}|null=null;
    preload(){
        this.load.svg('hero-player','/battle/avatars/player.svg');
        this.load.svg('hero-computer','/battle/avatars/computer.svg');
    }
    create(){
        this.ready=true;this.arrows=this.add.graphics().setDepth(500);
        this.input.dragDistanceThreshold=9;
        this.input.on('dragstart', (_:Phaser.Input.Pointer,obj:Phaser.GameObjects.Container)=>this.beginDrag(obj));
        this.input.on('drag', (pointer:Phaser.Input.Pointer,obj:Phaser.GameObjects.Container)=>{
            if(this.drag!==obj.name)return;
            const grab=obj.getData('grabOffset')??{x:0,y:0};
            const t=performance.now();if(this.owner.state?.hand.some(h=>h.card.instanceId===obj.name)&&!this.owner.state.hand.find(h=>h.card.instanceId===obj.name)?.playTargets.length)obj.setPosition(pointer.x+grab.x,pointer.y+grab.y);this.showTargets(obj.name);this.arrow(obj.name,pointer.x,pointer.y);
            this.owner.metrics.pointerSamples.push(performance.now()-t);
            const inputAt=this.owner.nativePointerAt||t;
            requestAnimationFrame(()=>{if(!this.owner.disposed)this.owner.metrics.feedbackSamples.push(performance.now()-inputAt);});
        });
        this.input.on('dragend', (pointer:Phaser.Input.Pointer,obj:Phaser.GameObjects.Container)=>{
            if(this.drag!==obj.name)return;
            const id=this.drag;this.drag=null;this.suppressUntil=performance.now()+250;this.arrows.clear();
            const destination=pointer.upElement===this.game.canvas?this.destination(id,pointer.x,pointer.y):null;
            if(destination)this.owner.intent(destination.type,id,destination.target);
            else {this.returnHome(id);this.owner.status.textContent='已取消瞄準，尚未攻擊。';if(this.owner.state?.hand.some(h=>h.card.instanceId===id))this.owner.status.textContent='未出牌，卡牌已回到手中。請拖到標示的區域，再放開。';}
        });
        this.input.on('pointerup',(pointer:Phaser.Input.Pointer,objects:Phaser.GameObjects.GameObject[])=>{if(pointer.upElement===this.game.canvas&&!objects.length&&!this.drag&&performance.now()>this.suppressUntil)this.owner.clearSelection();});
        this.input.on('pointermove',(pointer:Phaser.Input.Pointer)=>{
            if(this.owner.busy||this.owner.state?.pending?.kind!=='target'||!this.spell)return;
            const target=this.owner.state.pending.targets.map(id=>this.zones.get(id)).find(z=>z&&Math.abs(pointer.x-z.x)<z.width/2&&Math.abs(pointer.y-z.y)<z.height/2);
            this.arrows.clear();const card=this.pieces.get(this.spell.card.instanceId);
            if(card&&target)this.arrows.lineStyle(3,0x85d5b4,1).beginPath().moveTo(card.x,card.y).lineTo(target.x,target.y).strokePath();
        });
        // FIT changes display size without changing logical coordinates. Input uses Phaser's transform.
        this.scale.on('resize',()=>{this.arrows.clear();});
        this.owner.initialize(this);
    }
    update(time:number){if(this.lastFrame)this.owner.metrics.frameIntervals.push(time-this.lastFrame);this.lastFrame=time;
        if(this.owner.metrics.frameIntervals.length>1200)this.owner.metrics.frameIntervals.shift();}
    text(x:number,y:number,text:string,size=16,color='#eaf2fc'){
        return this.add.text(x,y,text,{fontFamily:font,fontSize:size,color,align:'center'}).setOrigin(.5);
    }
    get width(){return this.scale.width;}
    get compact(){return this.width<800;}
    get layout(){const l=boardLayout(this.width,this.scale.height,this.owner.focusActive);
        if(this.spell&&(l.width-5*l.cardWidth-32)/2<l.cardWidth+16)l.perPage=Math.max(1,l.perPage-1);
        return l;}
    get handSpellDock(){const l=this.layout;return !!this.spell&&(l.width-5*l.cardWidth-32)/2<l.cardWidth+16;}
    handForLayout(s:State){return this.handSpellDock?s.hand.filter(h=>h.card.instanceId!==this.spell?.card.instanceId):s.hand;}
    get cardWidth(){return this.layout.cardWidth;}
    get cardHeight(){return this.layout.cardHeight;}
    clearBoard(){
        // Cards keep their identity across snapshots; only the board furniture is rebuilt.
        const retained=new Set(this.pieces.values());
        this.children.list.filter(o=>o!==this.arrows&&!retained.has(o as Phaser.GameObjects.Container)).forEach(o=>o.destroy());
        this.borders.clear();this.zones.clear();this.arrows.clear();this.sideLabels.clear();this.heroHp.clear();this.visualHp.clear();this.energyLabels.clear();this.visualEnergy.clear();
    }
    redraw(){
        if(!this.ready||!this.owner.state)return;
        this.clearBoard();this.painted.clear();const s=this.owner.state,w=this.width,l=this.layout;
        this.add.rectangle(w/2,l.height/2,w,l.height,0x0b1824);
        this.add.rectangle(w/2,l.computerField,5*(l.cardWidth+8),l.fieldHeight,0x162432).setStrokeStyle(1,0x3f5164);
        this.add.rectangle(w/2,l.playerField,5*(l.cardWidth+8),l.fieldHeight,0x142a2e).setStrokeStyle(1,0x426b64);
        this.visualHp.set('computer',s.computer.hp);this.visualHp.set('player',s.player.hp);
        this.zones.set('face',this.heroZone('computer'));
        this.drawHero('computer');this.drawHero('player');
        this.zones.set('play',{x:w/2,y:l.playerField,width:5*(l.cardWidth+8),height:l.fieldHeight});
        this.zones.set('energy',this.energyZone('player'));
        for(const side of [s.computer,s.player]){
            this.visualEnergy.set(side.id,{available:side.availableEnergy,total:side.totalEnergy,used:side.energy.filter(e=>e.tapped).length});
            this.drawEnergy(side.id);
        }
        for(const [side,y] of [[s.computer,l.computerField],[s.player,l.playerField]] as const){
            for(let i=0;i<5;i++){
                const x=w/2+(i-2)*(this.cardWidth+8);
                this.add.rectangle(x,y,this.cardWidth,this.cardHeight,0x0e1b26,.7).setStrokeStyle(1,0x304250);
            }
            side.field.forEach((m,i)=>this.piece(m.card,w/2+(i-2)*(this.cardWidth+8),y,'monster',m));
        }
        const perPage=l.perPage;
        const arranged=this.handForLayout(s),page=Math.min(this.owner.handPage,Math.max(0,Math.ceil(arranged.length/perPage)-1));this.owner.handPage=page;
        const hand=arranged.slice(page*perPage,(page+1)*perPage);
        hand.forEach((h,i)=>this.piece(h.card,this.handSpellDock?l.handStart+l.cardWidth/2+(i+1)*(l.cardWidth+5):(l.handStart+w-12)/2+(i-(hand.length-1)/2)*(this.cardWidth+5),l.hand,'hand',undefined,h));
        if(this.spell&&!this.painted.has(this.spell.card.instanceId)){
            const slot=this.spellSlot(this.spell.side,this.spell.card.instanceId);this.piece(this.spell.card,slot.x,slot.y,'spell');
        }
        const live=new Set([...s.hand.map(h=>h.card.instanceId),...s.player.field.map(m=>m.card.instanceId),...s.computer.field.map(m=>m.card.instanceId)]);
        for(const [id,piece] of this.pieces)if(!this.painted.has(id)){
            if(live.has(id)){piece.setVisible(false);piece.disableInteractive();}
            else {this.tweens.killTweensOf(piece);piece.destroy();this.pieces.delete(id);this.homes.delete(id);}
        }
        this.showTargets(this.selected);this.owner.refreshControls();
    }
    hp(side:string,hp:number){const s=side==='player'?this.owner.state?.player:this.owner.state?.computer;if(!s)return;
        this.visualHp.set(side,hp);this.sideLabels.get(side)?.setText(`牌庫 ${s.deckCount} · 手牌 ${s.handCount}`);
        this.heroHp.get(side)?.setText(`♥ ${hp}/7`);}
    energyZone(side:string){const l=this.layout;
        return {x:this.compact?52:78,y:side==='player'?(this.compact?l.hand:Math.min(l.hand,l.height-105)):(this.compact?95:l.computerField),width:this.compact?86:120,height:this.compact?(side==='player'?116:70):(side==='player'?100:126)};
    }
    drawEnergy(side:string){const z=this.energyZone(side),mine=side==='player';
        this.add.rectangle(z.x,z.y,z.width,z.height,mine?0x123149:0x172a37).setStrokeStyle(2,mine?0x5eadd0:0x365365);
        this.text(z.x,z.y-z.height/2+17,mine?'己方能量':'對手能量',this.compact?12:14,'#b6ddef');
        this.energyLabels.set(side,this.text(z.x,z.y+(this.compact&&!mine?7:0),'',this.compact?17:20,'#93d8ff'));
        this.paintEnergy(side);
        this.add.zone(z.x,z.y,z.width,z.height).setInteractive().on('pointerover',()=>{
            if(this.drag||this.owner.busy)return;const e=this.visualEnergy.get(side);if(e)this.owner.status.textContent=`${mine?'己方':'對手'}能量：可用 ${e.available} / ${e.total}，已使用 ${e.used}。${mine?'將手牌拖入此區填能量。':''}`;
        }).on('pointerout',()=>{if(!this.drag&&!this.owner.busy)this.owner.status.textContent=this.owner.actionHint();});
    }
    paintEnergy(side:string){const e=this.visualEnergy.get(side);if(!e)return;
        this.energyLabels.get(side)?.setText(`◆ ${e.available}/${e.total}`);

    }
    piece(card:Card,x:number,y:number,kind:string,monster?:Monster,hand?:Hand){
        const cw=this.cardWidth,ch=this.cardHeight;
        const retained=this.pieces.get(card.instanceId);
        const container=retained??this.add.container(x,y).setName(card.instanceId);
        if(!retained)container.setData('visualId',++this.visualSequence);
        container.removeAllListeners();
        const staged=this.spell?.card.instanceId===card.instanceId;
        this.painted.add(card.instanceId);if(!staged||kind!=='spell'||!this.homes.has(card.instanceId))this.homes.set(card.instanceId,{x,y});
        const place=staged?this.spellSlot(this.spell!.side,card.instanceId):{x,y};
        container.setPosition(place.x,place.y).setScale(1).setAlpha(1).setDepth(staged?300:10).setVisible(true);
        const active=hand?.canPlay||monster?.canAttack;
        const spellLabel=staged?(this.spell!.committed?'結算中':'尚未扣費'):'';
        const stamp=JSON.stringify([card,kind,monster?.pp,monster?.dp,monster?.status,monster?.problem,active,!!hand,cw,ch,this.layout.focus,this.textures.exists(card.cardId),spellLabel]);
        if(container.getData('paint')!==stamp){
        container.removeAll(true);container.setData('paint',stamp);
        const border=this.add.rectangle(0,0,cw,ch,0x233341).setStrokeStyle(active?3:2,active?0x99dbc0:(colors[card.will]??0xa9b5c5));
        container.add(border);container.setData('border',border);
        const focus=this.layout.focus;
        const artHeight=focus?Math.max(16,ch-70):ch-67,artY=focus?-ch/2+10+artHeight/2:-23;
        if(!monster?.status.includes('潛伏')&&this.textures.exists(card.cardId)){
            const art=this.add.image(0,artY,card.cardId);
            if(focus){const scale=(cw-8)/art.width,cropHeight=Math.min(art.height,artHeight/scale);art.setScale(scale).setCrop(0,(art.height-cropHeight)/2,art.width,cropHeight);}
            else art.setDisplaySize(cw-8,artHeight);
            container.add(art);
        }
        else container.add(this.add.rectangle(0,artY,cw-8,artHeight,colors[card.will]??0x556677,.25));
        container.add(this.text(0,-ch/2+10,`${card.cost} ◆`,14,'#ffe7b6').setBackgroundColor('#182630'));
        const maxName=Math.floor((cw-10)/14),label=focus&&card.name.length>maxName?card.name.slice(0,maxName-1)+'…':card.name;
        const name=this.text(0,focus?ch/2-42:ch/2-43,label,focus?14:(this.compact?12:15));if(!focus)name.setWordWrapWidth(cw-10);container.add(name);
        const handStats=!!hand&&card.type==='怪物'&&card.pp!==null&&card.dp!==null;
        const stats=monster?`${monster.pp} PP · ${monster.dp} DP`:handStats?`${card.pp} PP · ${card.dp} DP`:card.type;
        const statsText=this.text(0,focus?ch/2-25:ch/2-22,stats,focus?14:13,handStats?'#ffe7b6':'#eaf2fc');
        if(statsText.width>cw-10)statsText.setFontSize(Math.floor((focus?14:13)*(cw-10)/statsText.width));
        container.add(statsText);
        if(handStats)container.add(this.text(0,ch/2-8,card.type,focus?14:12,'#b7d9da'));
        if(spellLabel)container.add(this.text(0,ch/2-8,spellLabel,focus?14:12,'#ffe7b6').setName('spell-badge'));
        if(monster){const status=monster.status.join(' · ')||(monster.canAttack?'可攻擊':monster.problem==='目前不能攻擊'?'':monster.problem),limit=Math.max(3,Math.floor((cw-8)/13));
            container.add(this.text(0,ch/2-8,focus&&status.length>limit?status.slice(0,limit-1)+'…':status,focus?14:(this.compact?10:11),'#b7d9da'));}
        if(monster?.status.some(s=>s.startsWith('聖盾'))){container.add(this.add.rectangle(cw/4,0,cw/2,ch-6,0x69c8cc,.25).setStrokeStyle(2,0x69c8cc).setName('shield'));
            container.add(this.text(cw/4,-ch/2+30,monster.status.find(s=>s.startsWith('聖盾'))!,12,'#d5ffff').setBackgroundColor('#164452').setPadding(3).setName('shield-count'));}
        if(monster?.status.includes('沉默'))container.add(this.add.rectangle(-cw/4,0,cw/2,ch-6,0xa5a5c4,.2).setStrokeStyle(2,0xa5a5c4));
        if(monster?.status.includes('冰凍'))container.add(this.add.rectangle(0,0,cw,ch,0x64baff,.2).setStrokeStyle(3,0x64baff));
        }
        this.borders.set(card.instanceId,container.getData('border'));
        container.setSize(cw,ch).setInteractive(new Phaser.Geom.Rectangle(0,0,cw,ch),Phaser.Geom.Rectangle.Contains);
        if(container.input)container.input.cursor='pointer';
        container.on('pointerdown',(pointer:Phaser.Input.Pointer)=>{container.setData('grabOffset',{x:container.x-pointer.x,y:container.y-pointer.y});});
        container.on('pointerup',(pointer:Phaser.Input.Pointer)=>{if(pointer.downElement===this.game.canvas&&pointer.upElement===this.game.canvas&&!this.drag&&!this.owner.busy&&performance.now()>this.suppressUntil)this.choose(card.instanceId);});
        container.on('pointerover',(pointer:Phaser.Input.Pointer)=>{
            if(pointer.event?.target!==this.game.canvas)return;
            if(!staged&&!this.drag&&!this.owner.busy&&!this.tweens.isTweening(container)){container.setDepth(100).setScale(1.06);this.owner.inspect(card,container.x,container.y);}
            if(this.selected)this.owner.showPreview(this.selected,card.instanceId);
        });
        container.on('pointerout',()=>{if(!staged&&!this.drag&&!this.owner.busy&&!this.tweens.isTweening(container))container.setScale(1).setDepth(10);});
        const mine=!!hand||!!monster&&this.owner.state!.player.field.some(m=>m.card.instanceId===card.instanceId);
        this.input.setDraggable(container,mine);
        if(kind==='monster'||kind==='hand')this.zones.set(card.instanceId,{x,y,width:cw,height:ch});
        this.pieces.set(card.instanceId,container);
    }
    choose(id:string){
        const state=this.owner.state!;
        if(state.pending?.kind==='choice'&&state.pending.options.some(o=>o.id===id)){this.owner.intent('choice',undefined,undefined,id);return;}
        if(state.pending?.targets.includes(id)){this.owner.intent('target',undefined,id);return;}
        if(state.pending)return;
        if(this.selected){const hand=state.hand.find(h=>h.card.instanceId===this.selected);if(hand?.canPlay&&hand.playTargets.includes(id)){this.owner.intent('play',hand.card.instanceId,id);return;}const attacker=state.player.field.find(m=>m.card.instanceId===this.selected);
            if(attacker?.targets.some(t=>t.id===id)){this.owner.intent('attack',attacker.card.instanceId,id);return;}}
        this.selected=id;this.showTargets(id);this.owner.select(id);
    }
    beginDrag(obj:Phaser.GameObjects.Container){
        if(this.owner.busy||this.owner.state?.pending)return;
        const id=obj.name;
        const h=this.owner.state!.hand.find(c=>c.card.instanceId===id);
        const m=this.owner.state!.player.field.find(m=>m.card.instanceId===id);
        if(!(h&&(h.canPlay||h.canEnergy)||m?.canAttack)){this.selected=id;this.owner.select(id);this.redraw();return;}
        this.owner.hidePreview();this.tweens.killTweensOf(obj);this.drag=id;this.selected=id;obj.setDepth(200).setScale(1.08);this.showTargets(id);this.owner.selected=this.owner.findCard(id);this.owner.refreshControls();
    }
    destination(id:string,x:number,y:number):{type:string;target?:string}|null{
        const s=this.owner.state!;const h=s.hand.find(c=>c.card.instanceId===id);
        const m=s.player.field.find(m=>m.card.instanceId===id);
        if(h?.canPlay)for(const target of h.playTargets){const z=this.zones.get(target);if(z&&Math.abs(x-z.x)<=z.width/2&&Math.abs(y-z.y)<=z.height/2)return {type:'play',target};}
        const possible=h?[...(h.canEnergy?['energy']:[]),...(h.canPlay?['play']:[])]:m?.targets.map(t=>t.id)||[];
        for(const name of possible){const z=this.zones.get(name);if(z&&Math.abs(x-z.x)<=z.width/2&&Math.abs(y-z.y)<=z.height/2)
            return h?{type:name}:{type:'attack',target:name==='face'?undefined:name};}
        if(h?.canPlay&&h.card.type==='法術'){const z=this.spellSlot('player',id);if(Math.abs(x-z.x)<=z.width/2&&Math.abs(y-z.y)<=z.height/2)return {type:'play'};}
        return null;
    }
    showTargets(id:string|null){
        this.borders.forEach((border,key)=>{const c=this.owner.findCard(key);border.setStrokeStyle(key===id?3:2,key===id?0xffe3a0:(colors[c?.will||'']??0xa9b5c5));});
        this.children.list.filter(o=>o.name==='highlight').forEach(o=>o.destroy());
        const s=this.owner.state;if(!s||this.owner.busy||s.isOver||this.owner.energyCandidate)return;
        const h=s.hand.find(h=>h.card.instanceId===id);const m=s.player.field.find(m=>m.card.instanceId===id);
        const targets=s.pending?(s.pending.kind==='choice'?s.pending.options.map(o=>o.id):s.pending.targets):(h?[...(h.canEnergy?['energy']:[]),...(h.canPlay?[...h.playTargets,'play']:[])]:m?.targets.map(t=>t.id)||[]);
        for(const key of targets){const z=h&&key==='play'?(h.card.type==='怪物'?this.fieldSlot('player',s.player.field.length):this.spellSlot('player',h.card.instanceId)):this.zones.get(key);if(!z)continue;
            const color=key==='energy'?0x75bfff:0x85d5b4;
            this.add.rectangle(z.x,z.y,z.width,z.height,color,.07).setStrokeStyle(3,color).setName('highlight').setDepth(50);
            if(h&&(key==='energy'||key==='play')){
                const label=key==='energy'?(readEnergyConfirmation()?'放開後確認':'放開填能量'):h.card.type==='怪物'?'召喚區':'施放區';
                const y=key==='energy'?z.y+z.height/2-11:z.y-z.height/2+14;
                this.text(z.x-z.width/2+6,y,label,this.compact?12:14,key==='energy'?'#a9d4ff':'#b7f1d6').setOrigin(0,.5).setBackgroundColor('#10212d').setPadding(3,0).setName('highlight').setDepth(51);
            }
        }
    }
    arrow(id:string,x:number,y:number){
        this.arrows.clear();const m=this.owner.state!.player.field.find(m=>m.card.instanceId===id),h=this.owner.state!.hand.find(h=>h.card.instanceId===id);if(!m&&!h?.playTargets.length)return;
        const source=this.homes.get(id);if(!source)return;
        this.arrows.lineStyle(4,0xffd58a,1).beginPath().moveTo(source.x,source.y).lineTo(x,y).strokePath();
        const angle=Math.atan2(y-source.y,x-source.x),size=14;
        this.arrows.fillStyle(0xffd58a).fillTriangle(x,y,x-size*Math.cos(angle-.5),y-size*Math.sin(angle-.5),x-size*Math.cos(angle+.5),y-size*Math.sin(angle+.5));
        const destination=this.destination(id,x,y);if(destination){if(m)this.owner.showPreview(id,destination.target||'face');else this.owner.status.textContent='放開以施放法術；Esc 取消。';}
        else this.owner.status.textContent='拖向亮起的目標後放開；移到空白處放開或按 Esc 取消。';
    }
    returnHome(id:string){const card=this.pieces.get(id),home=this.homes.get(id);if(!card||!home)return;
        this.tweens.killTweensOf(card);card.setDepth(200);
        this.tweens.add({targets:card,x:home.x,y:home.y,scaleX:1,scaleY:1,duration:this.owner.reduced?0:220,ease:'Cubic.Out',onComplete:()=>card.setDepth(10)});
    }
    cancel(){if(this.owner.busy||this.owner.state?.pending)return;this.drag=null;this.selected=null;this.arrows?.clear();
        // Energy confirmation keeps a released card lifted until confirm/cancel.
        for(const [id,card] of this.pieces){const home=this.homes.get(id);
            if(home&&(card.x!==home.x||card.y!==home.y||card.scaleX!==1))this.returnHome(id);
        }
        this.showTargets(null);}
    fieldSlot(side:string,index:number){const l=this.layout;return {x:l.width/2+(index-2)*(l.cardWidth+8),y:side==='player'?l.playerField:l.computerField,width:l.cardWidth,height:l.cardHeight};}
    spellSlot(side:string,id:string){const l=this.layout,left=this.fieldSlot(side,0).x-l.cardWidth/2;
        // The upper-left margin belongs to opponent energy. Resolve spells in the
        // free lower-left margin, or reserve a hand-row slot on narrow screens.
        return {x:left>=l.cardWidth+16?left/2:l.handStart+l.cardWidth/2,
            y:left>=l.cardWidth+16?l.playerField:l.hand,width:l.cardWidth,height:l.cardHeight};
    }
    heroZone(side:string){const l=this.layout;
        if(this.compact)return {x:l.width-44,y:(side==='player'?l.playerLabel:l.computerLabel)+10,width:58,height:66};
        const size=Math.min(80,l.cardHeight-8);
        return {x:l.width-78,y:side==='player'?l.playerField:l.computerField,width:size,height:size+8};
    }
    drawHero(side:string){
        const z=this.heroZone(side),key=`hero-${side}`;
        if(this.textures.exists(key))this.add.image(z.x,z.y-8,key).setDisplaySize(z.width-8,z.width-8).setDepth(20);
        else this.add.circle(z.x,z.y-8,(z.width-8)/2,side==='player'?0x427b80:0xb96659).setDepth(20);
        this.heroHp.set(side,this.text(z.x,z.y+z.width/2-10,'',16,'#fff1e6').setBackgroundColor('#592d3b').setPadding(5,1).setDepth(21));
        const infoX=this.compact?z.x-120:z.x;
        this.text(infoX,this.compact?z.y-15:z.y-57,side==='player'?'玩家':'電腦',14,'#d6e6ef');
        this.sideLabels.set(side,this.text(infoX,this.compact?z.y+9:z.y+55,'',12,'#9bb3c5'));
        this.hp(side,this.visualHp.get(side)??7);
        if(side==='computer')this.add.zone(z.x,z.y,z.width,z.height).setDepth(22).setInteractive({useHandCursor:true})
            .on('pointerover',()=>{if(this.selected&&!this.owner.busy)this.owner.showPreview(this.selected,'face');})
            .on('pointerup',(pointer:Phaser.Input.Pointer)=>{if(pointer.downElement!==this.game.canvas||pointer.upElement!==this.game.canvas)return;const s=this.owner.state;
                if(s&&!s.pending&&!this.owner.busy&&!this.drag&&performance.now()>this.suppressUntil&&this.selected&&s.player.field.find(m=>m.card.instanceId===this.selected)?.targets.some(t=>t.id==='face'))this.owner.intent('attack',this.selected);
            });
    }
    attackMetric(phase:string,id:string){const p=this.pieces.get(id);if(!p)return;
        this.owner.metrics.attacks.push({phase,instanceId:id,visualId:p.getData('visualId'),x:p.x,y:p.y});
        if(this.owner.metrics.attacks.length>200)this.owner.metrics.attacks.shift();}
    async finishAttack(generation:number){
        const attack=this.attack;if(!attack)return;
        const p=this.pieces.get(attack.id),index=this.visualFields.get(attack.side)?.indexOf(attack.id)??-1;
        if(p&&!attack.dead&&index>=0){const home=this.fieldSlot(attack.side,index);
            this.attackMetric('return',attack.id);
            await this.tween(p,{x:home.x,y:home.y,scaleX:1,scaleY:1,duration:this.owner.fast?110:260,ease:'Cubic.Out'});
            if(generation!==this.owner.generation)return;
            p.setDepth(10);this.zones.set(attack.id,home);this.attackMetric('rest',attack.id);
        }
        if(generation===this.owner.generation)this.attack=null;
    }
    async animateAttack(event:Event,generation:number){
        await this.finishAttack(generation);if(generation!==this.owner.generation||!event.instanceId)return;
        const p=this.pieces.get(event.instanceId),target=event.targetId?this.zones.get(event.targetId):this.heroZone(event.side==='player'?'computer':'player');
        if(!p||!target)return;
        this.attack={id:event.instanceId,side:event.side,dead:false};p.setDepth(300);
        const dx=target.x-p.x,dy=target.y-p.y,distance=Math.hypot(dx,dy)||1;
        // Stop at the target's near edge; the original attacker remains here through the outcome.
        const gap=event.targetId?this.cardHeight*.65:Math.min(this.cardHeight*.55,distance*.5);
        const ratio=Math.max(0,(distance-gap)/distance);
        this.attackMetric('outbound',event.instanceId);
        await this.tween(p,{x:p.x+dx*ratio,y:p.y+dy*ratio,scaleX:1.06,scaleY:1.06,duration:this.owner.fast?100:230,ease:'Cubic.In'});
        if(generation!==this.owner.generation)return;
        this.attackMetric('impact',event.instanceId);
        const cue=this.add.rectangle(target.x,target.y,target.width,target.height,0xffd58a,.25).setStrokeStyle(3,0xffd58a).setDepth(350);
        await this.tween(cue,{alpha:0,duration:this.owner.fast?60:140});cue.destroy();
    }
    async stageSpell(card:Card,side:string,committed:boolean){
        let piece=this.pieces.get(card.instanceId);
        if(!piece){this.piece(card,this.width/2,side==='player'?this.layout.hand:this.layout.computerLabel,'hand');piece=this.pieces.get(card.instanceId)!;}
        const first=this.spell?.card.instanceId!==card.instanceId;
        this.spell={card,side,committed};piece.setVisible(true).setDepth(300);this.tweens.killTweensOf(piece);
        const label=committed?'結算中':'尚未扣費';
        const badge=piece.list.find(child=>child.name==='spell-badge') as Phaser.GameObjects.Text|undefined;
        if(badge)badge.setText(label);else piece.add(this.text(0,this.cardHeight/2-8,label,this.layout.focus?14:12,'#ffe7b6').setName('spell-badge'));
        const to=this.spellSlot(side,card.instanceId),from={x:piece.x,y:piece.y};
        this.owner.metrics.spells.push({phase:committed?'cast':'prepare',instanceId:card.instanceId,visualId:piece.getData('visualId'),from,to:{x:to.x,y:to.y}});
        if(this.owner.metrics.spells.length>100)this.owner.metrics.spells.shift();
        if(first||Math.abs(from.x-to.x)>1||Math.abs(from.y-to.y)>1)
            await this.tween(piece,{x:to.x,y:to.y,scaleX:1,scaleY:1,duration:this.owner.reduced?0:this.owner.fast?100:280,ease:'Cubic.Out'});
    }
    async finishSpell(state:State,generation:number){
        const spell=this.spell;if(!spell||state.pending&&!state.isOver)return;
        const card=this.pieces.get(spell.card.instanceId),home=this.homes.get(spell.card.instanceId);
        const cancelled=!spell.committed&&state.hand.some(h=>h.card.instanceId===spell.card.instanceId);
        if(card){
            this.owner.metrics.spells.push({phase:cancelled?'cancel':'resolve',instanceId:spell.card.instanceId,visualId:card.getData('visualId'),from:{x:card.x,y:card.y},to:cancelled&&home?home:{x:card.x,y:card.y}});
            await this.tween(card,cancelled&&home?{x:home.x,y:home.y,scaleX:1,scaleY:1,duration:this.owner.reduced?0:this.owner.fast?90:220,ease:'Cubic.Out'}:
                {alpha:0,scaleX:.88,scaleY:.88,duration:this.owner.reduced?0:this.owner.fast?80:180});
        }
        if(generation===this.owner.generation)this.spell=null;
    }
    async animateSummon(event:Event){
        if(!event.card||!event.instanceId)return;
        // Replay the visible field in event order, including creatures that leave in this batch.
        const field=this.visualFields.get(event.side)??[];
        if(field.includes(event.instanceId))return;
        const index=field.length;field.push(event.instanceId);this.visualFields.set(event.side,field);
        const target=this.fieldSlot(event.side,index);
        let card=this.pieces.get(event.instanceId);
        if(!card){this.piece(event.card,this.width/2,event.side==='player'?this.layout.hand:this.layout.computerLabel,'hand');card=this.pieces.get(event.instanceId)!;}
        card.setVisible(true);this.tweens.killTweensOf(card);
        const from={x:card.x,y:card.y};card.setDepth(300);
        this.owner.metrics.movements.push({instanceId:event.instanceId,visualId:card.getData('visualId'),from,to:{x:target.x,y:target.y}});
        if(this.owner.metrics.movements.length>100)this.owner.metrics.movements.shift();
        await this.tween(card,{x:target.x,y:target.y,scaleX:1,scaleY:1,duration:this.owner.fast?150:360,ease:'Cubic.Out'});
        this.zones.set(event.instanceId,target);card.setDepth(10);
    }
    async turnCue(event:Event,turn:number){
        this.owner.presentation={side:event.side,turn,stage:'交接中'};this.owner.paintTurn();
        this.owner.status.textContent=`第 ${turn} 回合 · ${event.side==='player'?'輪到你了，正在準備手牌與能量。':'輪到電腦，請等待它完成行動。'}`;
        this.owner.metrics.turns.push({phase:'handoff',side:event.side,turn});
        if(this.owner.metrics.turns.length>100)this.owner.metrics.turns.shift();
        if(this.owner.reduced||document.hidden)return;
        const l=this.layout,cue=this.add.rectangle(l.width/2,event.side==='player'?l.playerField:l.computerField,l.width-28,l.fieldHeight,0x85d5b4,.08)
            .setStrokeStyle(3,event.side==='player'?0x85d5b4:0xe3ca83).setDepth(2);
        await this.tween(cue,{alpha:0,duration:this.owner.fast?100:320});cue.destroy();
    }
    async drawCue(side:string,count:number){
        if(this.owner.presentation){this.owner.presentation.stage='抽牌中';this.owner.paintTurn();}
        this.owner.status.textContent=`${side==='player'?'你':'電腦'}抽了 ${count} 張牌。`;
        this.owner.metrics.turns.push({phase:'draw',side,count,turn:this.owner.presentation?.turn??this.owner.state?.turn??0});
        if(this.owner.reduced||document.hidden)return;
        // Highlight the existing deck/hand readout, without revealing the opponent's card.
        const label=this.sideLabels.get(side);if(!label)return;
        const cue=this.add.rectangle(label.x,label.y,Math.min(this.width-30,label.width+16),label.height+4,0x85d5b4,.15).setStrokeStyle(2,0x85d5b4).setDepth(2);
        await this.tween(cue,{alpha:0,duration:this.owner.fast?90:220});cue.destroy();
    }
    async animate(event:Event,generation:number){
        if(event.type==='damage'||event.type==='heal')this.hp(event.side,Math.min(7,Math.max(0,(this.visualHp.get(event.side)||0)+(event.type==='damage'?-event.amount:event.amount))));
        if(event.type==='pay'){
            const energy=this.visualEnergy.get(event.side);if(energy){energy.available=Math.max(0,energy.available-event.amount);energy.used+=event.amount;this.paintEnergy(event.side);}
            return;
        }
        // "play" and "summon" describe one monster entry, not two visual movements.
        if(event.type==='play'&&event.card?.type==='怪物')return;
        if(event.type==='play'&&event.card){
            await this.stageSpell(event.card,event.side,true);if(generation!==this.owner.generation)return;
            this.owner.status.textContent=`${event.card.name}：效果結算中。`;
            const target=event.targetId?this.zones.get(event.targetId):undefined;
            if(target&&!this.owner.reduced){const cue=this.add.rectangle(target.x,target.y,target.width,target.height,0x85d5b4,.14).setStrokeStyle(3,0xb7f1d6).setDepth(250);
                await this.tween(cue,{alpha:0,duration:this.owner.fast?90:180});cue.destroy();}
            return;
        }
        // Selection instructions belong beside the controls, not in a floating centre card.
        if(event.type==='target'||event.type==='choice')return;
        if(this.owner.reduced||document.hidden)return;
        if(event.type==='summon'){await this.animateSummon(event);return;}
        if(this.owner.animationPrototype)return;
        const piece=event.instanceId?this.pieces.get(event.instanceId):undefined;
        const duration=this.owner.fast?100:240;
        if(event.type==='attack'&&piece){
            await this.animateAttack(event,generation);
        }else if((event.type==='death'||event.type==='bounce')&&piece){
            if(this.attack?.id===event.instanceId){this.attack.dead=true;this.attackMetric('death',event.instanceId!);}
            await this.tween(piece,{alpha:0,scaleX:.65,scaleY:.65,duration});
            if(generation!==this.owner.generation)return;
            const field=(this.visualFields.get(event.side)??[]).filter(id=>id!==event.instanceId);this.visualFields.set(event.side,field);
            // A sacrifice can free a slot before another summon in the same response.
            await Promise.all(field.map(async(id,index)=>{const card=this.pieces.get(id),slot=this.fieldSlot(event.side,index);this.zones.set(id,slot);
                if(card&&id!==this.attack?.id&&(card.x!==slot.x||card.y!==slot.y))await this.tween(card,{x:slot.x,y:slot.y,duration:this.owner.fast?80:180,ease:'Cubic.Out'});
            }));
        }else{
            if(event.type==='status'&&event.label.includes('聖盾'))piece?.list.find(child=>child.name==='shield')?.destroy();
            if(this.attack||event.type==='damage'||event.type==='heal'){
                const place=(event.type==='damage'||event.type==='heal')?this.heroZone(event.side):piece;
                this.owner.status.textContent=`${event.card?.name??''} ${event.label}${event.amount?' '+event.amount:''}`;
                if(place){const notice=this.text(place.x,place.y,event.label+(event.amount?' '+event.amount:''),18,'#ffdf9e').setBackgroundColor('#142432').setDepth(400);
                    await this.tween(notice,{y:place.y-14,alpha:0,duration:this.owner.fast?120:260});notice.destroy();}
                return;
            }
            if(this.spell){this.owner.status.textContent=`${this.spell.card.name}：${event.label}${event.amount?' '+event.amount:''}`;return;}
            const x=this.width/2,y=event.side==='computer'?this.layout.computerField:this.layout.playerField;
            const notice=this.text(x,y,event.card?`${event.card.name} · ${event.label}`:`${event.label}${event.amount?' '+event.amount:''}`,19,'#ffdf9e').setDepth(400);
            await this.tween(notice,{y:y-20,alpha:0,duration:duration*1.5});notice.destroy();
        }
        if(generation!==this.owner.generation)return;
    }
    tween(targets:Phaser.GameObjects.GameObject,config:object):Promise<void>{
        return new Promise(resolve=>{const finish=()=>{clearTimeout(timeout);resolve();};
            // Timer also completes when a tween is killed on resync or the tab stops ticking.
            const timeout=setTimeout(finish,1600);this.tweens.add({targets,...config,onComplete:finish,onStop:finish});});
    }
}

class Board {
    game:Phaser.Game;scene:BattleScene;
    state:State|null=null;receiver:Receiver;root:HTMLElement;mount:HTMLElement;controls:HTMLElement;status:HTMLElement;primary:HTMLElement;focusActive=false;resizeFrame=0;
    selected:Card|null=null;energyCandidate:string|null=null;handPage=0;busy=false;busySince=0;watchdogLimit=8500;disposed=false;fast=false;reduced=matchMedia('(prefers-reduced-motion: reduce)').matches;sound=false;generation=0;
    logCard:string|null=null;
    history:{id:string;text:string;card:Card|null}[]=[];seen=new Set<string>();requestedArt=new Set<string>();audio:{enabled:boolean;play:(type:string)=>Promise<void>;setEnabled:(value:boolean)=>boolean;dispose:()=>void}|null=null;
    preview!:HTMLElement;modal:HTMLDialogElement;panel:string|null=null;
    nativePointerAt=0;
    presentation:{side:string;turn:number;stage:string}|null=null;
    metrics={turns:[] as {phase:string;side:string;turn:number;count?:number}[],attacks:[] as {phase:string;instanceId:string;visualId:number;x:number;y:number}[],movements:[] as {instanceId:string;visualId:number;from:{x:number;y:number};to:{x:number;y:number}}[],spells:[] as {phase:string;instanceId:string;visualId:number;from:{x:number;y:number};to:{x:number;y:number}}[],pointerSamples:[] as number[],feedbackSamples:[] as number[],frameIntervals:[] as number[],commands:0,acks:0};
    abort=new AbortController();
    watchdog:ReturnType<typeof setInterval>;
    resizeObserver:ResizeObserver;
    syncing=false;
    readyResolve!:()=>void;readyReject!:(e:unknown)=>void;
    ready:Promise<void>;initialized=false;
    constructor(root:HTMLElement,receiver:Receiver,public animationPrototype=false,public sessionActions=false){
        this.root=root;this.receiver=receiver;root.replaceChildren();
        this.status=el('p','載入戰場…','battle-client-status');this.status.setAttribute('role','status');this.status.setAttribute('aria-live','polite');
        this.mount=el('div','','battle-canvas');this.controls=el('div','','battle-client-controls');
        this.primary=el('div','','battle-primary-controls');this.primary.setAttribute('aria-label','主要對戰操作');this.preview=el('aside','','battle-card-preview');this.preview.hidden=true;this.preview.setAttribute('aria-label','卡牌放大');this.modal=el('dialog','','battle-modal');root.append(this.status,this.mount,this.primary,this.controls,this.preview,this.modal);this.modal.addEventListener('cancel',e=>{e.preventDefault();this.closePanel();});this.modal.addEventListener('click',e=>{if(e.target===this.modal){const r=this.modal.getBoundingClientRect();if(e.clientX<r.left||e.clientX>r.right||e.clientY<r.top||e.clientY>r.bottom)this.closePanel();}});
        this.ready=new Promise((resolve,reject)=>{this.readyResolve=resolve;this.readyReject=reject;});
        const audioPath=new URL('./battle-audio.js',import.meta.url).href;
        void import(audioPath).then(module=>{if(this.disposed)return;this.audio=module.createBattleAudio(root);this.sound=this.audio!.enabled;}).catch(()=>{});
        const logicalWidth=root.clientWidth<800?560:1180;
        this.mount.style.height=`${this.mount.clientWidth*760/logicalWidth}px`;
        this.resizeObserver=new ResizeObserver(()=>this.queueLayout());this.resizeObserver.observe(root);this.resizeObserver.observe(this.mount);
        this.scene=new BattleScene('Battle');this.scene.owner=this;
        try{this.game=new Phaser.Game({type:rendererType(),parent:this.mount,width:logicalWidth,height:760,
            backgroundColor:'#0b1824',scene:this.scene,scale:{mode:Phaser.Scale.FIT,autoCenter:Phaser.Scale.CENTER_BOTH},
            render:{antialias:true},audio:{noAudio:true},fps:{target:60},input:{activePointers:2}});}
        catch(error){this.resizeObserver.disconnect();root.replaceChildren();throw error;}
        this.watchdog=setInterval(()=>{
            if(this.busy&&!document.hidden&&!this.syncing&&performance.now()-this.busySince>this.watchdogLimit){
                this.syncing=true;void this.receiver.invokeMethodAsync('Resync').catch(()=>{}).finally(()=>this.syncing=false);
            }
        },2000);
        const options={signal:this.abort.signal};
        window.addEventListener('resize',()=>this.queueLayout(),options);
        document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!this.busy){
            if(this.modal.open){this.closePanel();return;}this.hidePreview();
            if(this.state?.pending){if(this.state.pending.canCancel)this.intent('cancel');return;}
            this.energyCandidate=null;this.scene.cancel();this.selected=null;this.refreshControls();}},options);
        this.game.canvas?.addEventListener('pointermove',()=>{this.nativePointerAt=performance.now();},{...options,capture:true});
        this.game.canvas?.addEventListener('pointercancel',()=>this.scene.cancel(),options);
        window.addEventListener('blur',()=>this.scene.cancel(),options);
        document.addEventListener('visibilitychange',()=>{if(document.hidden){this.generation++;this.scene.tweens.killAll();this.busy=false;}
            else if(this.state){this.scene.redraw();void this.receiver.invokeMethodAsync('Resync');}},options);
    }
    queueLayout(){
        if(this.disposed||this.resizeFrame)return;
        this.resizeFrame=requestAnimationFrame(()=>{this.resizeFrame=0;this.fitLayout();});
    }
    fitLayout(){
        if(!this.initialized||this.disposed)return;
        // Finish the current movement before adopting new geometry; keep its card alive.
        if(this.busy)return;
        const focus=!!this.root.closest('.phaser-page.in-game');
        const changed=focus!==this.focusActive;this.focusActive=focus;
        this.root.classList.toggle('battle-focus',focus);
        if(focus)this.mount.style.height='100%';
        const width=focus?Math.floor(this.mount.clientWidth):(this.root.clientWidth<800?560:1180);
        const height=focus?Math.floor(this.mount.clientHeight):760;
        if(!focus)this.mount.style.height=`${this.mount.clientWidth*760/width}px`;
        if(width<1||height<1)return;
        // The room-to-battle layout changes the parent without a window resize.
        // FIT must see the new parent bounds before fitting the new game size.
        this.game.scale.setParentSize(this.mount.clientWidth,this.mount.clientHeight);
        if(changed||width!==this.scene.scale.width||height!==this.scene.scale.height){
            // The authoritative command/ack path continues during a viewport change.
            this.scene.drag=null;this.scene.arrows.clear();this.scene.tweens.killAll();
            this.game.scale.setGameSize(width,height);this.scene.redraw();
        }
    }
    initialize(scene:BattleScene){
        scene.load.on('loaderror',()=>{/* Missing art uses the colored card placeholder. */});
        this.initialized=true;this.readyResolve();this.status.textContent='戰場已就緒';this.queueLayout();
    }
    findCard(id:string):Card|null{const s=this.state;return s?.hand.find(h=>h.card.instanceId===id)?.card
        ||[...(s?.player.field||[]),...(s?.computer.field||[])].find(m=>m.card.instanceId===id)?.card||null;}
    async loadArt(state:State,events:Event[]=[]){
        const cards=[...state.hand.map(h=>h.card),...state.player.field.map(m=>m.card),...state.computer.field.map(m=>m.card),...events.flatMap(e=>e.card?[e.card]:[])];
        const unique=new Map(cards.map(c=>[c.cardId,c]));let count=0;
        unique.forEach(c=>{if(!this.scene.textures.exists(c.cardId)&&!this.requestedArt.has(c.cardId)){this.requestedArt.add(c.cardId);this.scene.load.image(c.cardId,c.art);count++;}});
        if(!count)return;
        this.status.textContent=`載入卡圖（${count} 張）…`;
        await new Promise<void>(resolve=>{const timeout=setTimeout(resolve,4000);
            this.scene.load.once('complete',()=>{clearTimeout(timeout);resolve();});this.scene.load.start();});
    }
    async update(response:Response){
        await this.ready;if(this.disposed)return;
        if(this.state?.matchId===response.state.matchId&&response.state.revision<this.state.revision){await this.ack(response);return;}
        this.hidePreview();const generation=++this.generation;this.scene.tweens.killAll();this.scene.attack=null;this.presentation=null;this.scene.drag=null;this.scene.arrows.clear();this.scene.selected=null;this.scene.showTargets(null);
        this.busy=true;this.scene.showTargets(null);this.busySince=performance.now();this.watchdogLimit=Math.min(36000,Math.max(8500,response.events.length*650+3000));this.root.querySelectorAll('button').forEach(b=>b.disabled=true);this.paintTurn();
        const old=this.state;const changedMatch=old?.matchId!==response.state.matchId;
        this.scene.visualFields=new Map([['player',old?.player.field.map(m=>m.card.instanceId)??[]],['computer',old?.computer.field.map(m=>m.card.instanceId)??[]]]);
        if(changedMatch){this.logCard=null;this.history=[];this.seen.clear();this.handPage=0;this.scene.spell=null;}
        await this.loadArt(response.state,response.events);if(this.disposed||generation!==this.generation)return;
        if(changedMatch||!old){this.state=response.state;this.scene.redraw();}
        this.status.textContent=response.message||'動作結算中…';
        let animationFailed=false;
        try{
            const preparing=response.state.hand.find(h=>h.card.instanceId===response.state.pending?.sourceInstanceId&&h.card.type==='法術');
            if(preparing&&this.scene.spell?.card.instanceId!==preparing.card.instanceId)await this.scene.stageSpell(preparing.card,'player',false);
            for(let index=0;index<response.events.length;index++){
                const event=response.events[index];
                if(generation!==this.generation||this.disposed)break;
                if(this.seen.has(event.id))continue;this.seen.add(event.id);
                this.history.unshift({id:event.id,card:event.card,text:`${event.side==='player'?'玩家':'電腦'}：${event.card?event.card.name+' · ':''}${event.label}${event.amount?' '+event.amount:''}`});
                this.history=this.history.slice(0,50);this.tone(event.type);
                if(event.type==='turn'){await this.scene.finishAttack(generation);if(generation!==this.generation)break;
                    await this.scene.turnCue(event,response.state.turn);continue;}
                if(event.type==='draw'){
                    // Opening hands already exist in the initial snapshot. Do not replay 14 floating notices.
                    if(changedMatch)continue;
                    const previous=response.events[index-1];if(previous?.type==='draw'&&previous.side===event.side)continue;
                    let count=1;while(response.events[index+count]?.type==='draw'&&response.events[index+count].side===event.side)count++;
                    await this.scene.drawCue(event.side,count);continue;
                }
                await this.scene.animate(event,generation);
            }
            if(generation===this.generation)await this.scene.finishAttack(generation);
            if(generation===this.generation)await this.scene.finishSpell(response.state,generation);
        }catch{animationFailed=true;}finally{
            if(!this.disposed&&generation===this.generation){this.state=response.state;this.busy=false;
                this.finishPresentation();
                if(animationFailed&&!response.state.pending)this.scene.spell=null;
                this.selected=null;this.energyCandidate=null;this.scene.redraw();this.queueLayout();this.status.textContent=response.message||response.state.outcome||(animationFailed?'動畫已略過，已同步目前對局。':this.actionHint());
                await this.ack(response);}
        }
    }
    async ack(response:Response){if(this.disposed)return;this.metrics.acks++;
        try{await this.receiver.invokeMethodAsync('Acknowledge',response.state.matchId,response.state.revision,response.batchId);}catch{}}
    intent(type:string,id?:string,target?:string,option?:string){
        if(this.busy||!this.state)return;
        if(type==='energy'&&readEnergyConfirmation()&&this.findCard(id||'')){
            this.scene.returnHome(id||'');this.hidePreview();this.selected=this.findCard(id||'');this.energyCandidate=id||null;this.scene.showTargets(null);this.refreshControls();return;
        }
        void this.submit(type,id,target,option);
    }
    async submit(type:string,id?:string,target?:string,option?:string){
        if(!this.state||this.busy||this.disposed)return;
        this.hidePreview();this.busy=true;this.scene.arrows.clear();this.scene.showTargets(null);this.busySince=performance.now();this.watchdogLimit=8500;this.metrics.commands++;this.refreshControls();this.status.textContent=type==='end'?'正在結束你的回合…':'等待確認…';
        if(type==='end'){this.presentation={side:this.state.decisionPlayerId,turn:this.state.turn,stage:'回合結束中'};this.paintTurn();}
        const command:Command={commandId:crypto.randomUUID(),matchId:this.state.matchId,expectedRevision:this.state.revision,type,instanceId:id,targetId:target,optionId:option};
        try{const result=await this.receiver.invokeMethodAsync<Response>('Submit',command);await this.update(result);}
        catch{this.busy=false;this.presentation=null;this.scene.redraw();this.status.textContent='連線中斷，請等待恢復後重新操作。';}
    }
    select(id:string){this.selected=this.findCard(id);this.refreshControls();const p=this.scene.pieces.get(id);if(this.selected&&p)this.inspect(this.selected,p.x,p.y);}
    playHint(h:Hand){
        if(!h.canPlay)return `${h.problem}${h.canEnergy?' 可改填能量。':h.energyProblem||''}`;
        const step=h.preparation==='target'?'再選目標（尚未扣費）。':h.preparation==='choice'?'再選效果（尚未扣費）。':h.preparation==='sacrifice'?'再選犧牲的怪物（尚未扣費）。':h.card.type==='怪物'?'召喚後依卡牌效果操作。':'依卡牌效果結算。';
        return `${h.warning?h.warning+' ':''}${h.playTargets.length?'拖向亮起的目標':'拖到綠色區'}；${step}`;
    }
    actionHint(){
        const s=this.state;if(!s||s.phase==='NotStarted')return '選擇牌組並開始對戰。';
        if(this.busy)return '動作結算中，請稍候。';
        if(s.isOver)return s.outcome;
        if(s.decisionPlayerId!=='player')return '電腦行動中，請等待你的回合。';
        if(this.energyCandidate)return '確認後才會將手牌轉為能量，不抽牌；按「取消」可保留手牌。';
        if(s.pending){const spell=this.scene.spell;const label=spell?`${spell.card.name} · ${spell.committed?'已扣費，請完成結算':'尚未扣費，可取消'}。`:'';
            return label+(s.pending.kind==='target'?'請點選亮起的目標。':'請先完成效果選擇，再繼續出牌。');}
        const m=s.player.field.find(m=>m.card.instanceId===this.selected?.instanceId);
        if(m)return m.canAttack?'拖曳瞄準亮起的目標後放開，或點選目標攻擊；Esc 取消。':m.problem;
        const h=s.hand.find(h=>h.card.instanceId===this.selected?.instanceId);
        return h?`${this.playHint(h)}${h.canEnergy?(readEnergyConfirmation()?' 藍色區：填能量，需確認。':' 藍色區：直接填能量。'):''}`:'';
    }
    showPreview(attacker:string,target:string){
        const entry=this.state?.player.field.find(m=>m.card.instanceId===attacker)?.targets.find(t=>t.id===target);
        if(!entry?.preview)return;const p=entry.preview;
        if(target==='face'){this.status.textContent=`攻擊預覽：電腦生命 ${this.state!.computer.hp} → ${Math.max(0,this.state!.computer.hp-p.playerDamage)}。宣告觸發與離場能力另行結算。`;return;}
        this.status.textContent=`交戰預覽 → ${entry.label}：己方${p.attackerDies?'消滅':p.attackerShieldBreaks?'消耗一層聖盾':'存活'}、敵方${p.defenderDies?'消滅':p.defenderShieldBreaks?'消耗一層聖盾':'存活'}${p.playerDamage?'，玩家傷害 '+p.playerDamage:''}。離場能力另行結算。`;
    }
    finishPresentation(){if(this.presentation)this.metrics.turns.push({phase:'ready',side:this.presentation.side,turn:this.presentation.turn});this.presentation=null;}
    turnText(){
        const s=this.state;if(!s||s.phase==='NotStarted')return '準備對局';
        if(s.isOver)return s.outcome;
        const p=this.presentation,side=p?.side??s.activePlayerId,turn=p?.turn??s.turn;
        return `第 ${turn} 回合 · ${side==='player'?'你的回合':'電腦回合'} · ${p?.stage??(this.busy?'效果結算中':s.decisionPlayerId!==side?`等待${s.decisionPlayerId==='player'?'你':'電腦'}選擇`:s.pending?'等待選擇':side==='player'?'可以行動':'行動中')}`;
    }
    paintTurn(){this.root.dataset.resolving=String(this.busy);const side=this.presentation?.side??this.state?.activePlayerId;
        this.root.querySelectorAll<HTMLElement>('.battle-turn-indicator').forEach(n=>{n.textContent=this.turnText();n.dataset.side=side??'';});
        this.root.querySelectorAll<HTMLButtonElement>('.battle-end-turn').forEach(n=>{n.textContent=this.busy?'結算中…':this.state?.decisionPlayerId==='computer'?'電腦行動中':'結束回合';});
    }
    hidePreview(){this.preview.hidden=true;}
    clearSelection(){this.hidePreview();if(this.busy||this.state?.pending||this.energyCandidate)return;this.selected=null;this.scene.cancel();this.refreshControls();}
    inspect(card:Card,x?:number,y?:number){
        if(this.busy||this.scene.drag||this.energyCandidate||this.modal.open)return;
        this.preview.replaceChildren(this.cardDetail(card));this.preview.hidden=false;
        if(this.preview.scrollHeight>this.preview.clientHeight){const art=this.preview.querySelector('img');if(art)art.hidden=true;}
        const board=this.root.getBoundingClientRect(),canvas=this.game.canvas.getBoundingClientRect();
        const px=x===undefined?board.width/2:canvas.left-board.left+x*canvas.width/this.scene.width;
        const py=y===undefined?board.height/2:canvas.top-board.top+y*canvas.height/this.scene.scale.height;
        const w=this.preview.offsetWidth,h=this.preview.offsetHeight;
        this.preview.style.left=`${Math.max(8,Math.min(board.width-w-8,px+w+65<board.width?px+65:px-w-65))}px`;
        const desiredY=board.width<800?(py>board.height/2?py-h-this.scene.cardHeight/2-12:py+this.scene.cardHeight/2+12):py-h/2;
        this.preview.style.top=`${Math.max(48,Math.min(board.height-h-8,desiredY))}px`;
    }
    cardDetail(card:Card){
        const box=el('article','','battle-card-detail'),art=el('img');art.src=card.art;art.alt='';art.onerror=()=>art.hidden=true;
        box.append(art,el('h3',card.name),el('p',`${card.will} · ${card.type} · ${card.cost} 費`));
        if(card.pp!==null)box.append(el('strong',`${card.pp} PP · ${card.dp} DP`));
        box.append(el('p',card.text||'無異能','battle-card-effect'));
        const warning=this.state?.hand.find(h=>h.card.instanceId===card.instanceId)?.warning;
        if(warning)box.append(el('p',warning,'battle-card-warning'));
        const monster=[...(this.state?.player.field??[]),...(this.state?.computer.field??[])].find(m=>m.card.instanceId===card.instanceId);
        if(monster?.status.length)box.append(el('p',monster.status.join(' · ')));
        if(monster?.attachment)box.append(el('p',`附著：${monster.attachment.name} — ${monster.attachment.text}`));
        return box;
    }
    openPanel(name:string){this.logCard=null;this.hidePreview();this.panel=name;this.refreshControls();}
    closePanel(){
        if(this.busy)return;
        if(this.energyCandidate){this.energyCandidate=null;this.scene.cancel();}
        else if(this.state?.pending&&this.panel==='choice'){if(this.state.pending.canCancel)this.intent('cancel');else return;}
        this.panel=null;this.modal.close();this.refreshControls();
    }
    async sessionAction(type:string){
        if(this.busy||!this.sessionActions||this.disposed)return;
        this.panel=null;this.modal.close();this.hidePreview();this.busy=true;this.busySince=performance.now();this.refreshControls();
        try{await this.receiver.invokeMethodAsync('RequestSessionAction',type);}
        catch{if(!this.disposed)this.status.textContent='操作未完成，請重試。';}
        finally{if(!this.disposed){this.busy=false;this.refreshControls();}}
    }
    refreshControls(){
        const s=this.state;if(!s)return;this.status.textContent=this.actionHint();
        this.controls.replaceChildren();this.primary.replaceChildren();
        const indicator=el('span',this.turnText(),'battle-turn-indicator');indicator.setAttribute('aria-live','polite');this.primary.append(indicator);
        const tools=el('nav','','battle-tools');tools.setAttribute('aria-label','對戰選單');
        for(const [name,label,icon] of [['player','我的墓地','▱'],['computer','對手墓地','▱'],['logs','對戰紀錄','☷'],['settings','設定','⚙']]){
            const b=button(name==='player'?'我墓':name==='computer'?'敵墓':icon,()=>this.openPanel(name));b.title=label;b.setAttribute('aria-label',label);tools.append(b);
        }
        if(s.revealedCards.length)tools.prepend(button('公開卡牌',()=>this.openPanel('reveal')));
        this.primary.append(tools);
        const end=button('結束回合',()=>this.intent('end'),this.busy||s.isOver||s.decisionPlayerId!=='player'||!!s.pending||!!this.energyCandidate);end.classList.add('battle-end-turn');if(!this.scene.compact)end.style.top=`${(this.scene.layout.computerField+this.scene.layout.playerField)/2-20}px`;this.primary.append(end);
        const perPage=this.scene.layout.perPage,pages=Math.ceil(this.scene.handForLayout(s).length/perPage);
        if(pages>1){const pager=el('div','','battle-hand-pages');pager.append(button('‹',()=>{this.hidePreview();this.handPage--;this.scene.redraw();},this.handPage<=0||this.busy),el('span',`${this.handPage+1}/${pages}`),button('›',()=>{this.hidePreview();this.handPage++;this.scene.redraw();},this.handPage>=pages-1||this.busy));pager.firstElementChild?.setAttribute('aria-label','上一頁手牌');pager.lastElementChild?.setAttribute('aria-label','下一頁手牌');this.primary.append(pager);}
        const p=s.pending;
        const directIds=new Set([...s.hand.map(h=>h.card.instanceId),...s.player.field.map(m=>m.card.instanceId),...s.computer.field.map(m=>m.card.instanceId)]);
        const directChoice=p?.kind==='choice'&&p.options.some(o=>directIds.has(o.id));
        if(p){
            const prompt=el('div','','battle-prompt');prompt.append(el('span',p.title));
            if(p.canCancel)prompt.append(button('取消',()=>this.intent('cancel'),this.busy));
            if(directChoice)for(const o of p.options.filter(o=>!directIds.has(o.id)))prompt.append(button(o.title,()=>this.intent('choice',undefined,undefined,o.id),this.busy));
            this.controls.append(prompt);
        }
        // Keyboard equivalents remain off-screen until focused, without a duplicate visible hand list.
        const accessible=el('div','','battle-keyboard-cards');accessible.setAttribute('aria-label','卡牌操作');
        for(const c of [...s.hand.map(h=>h.card),...s.player.field.map(m=>m.card),...s.computer.field.map(m=>m.card)]){
            const b=button(c.name,()=>this.scene.choose(c.instanceId),this.busy);b.onfocus=()=>{const z=this.scene.zones.get(c.instanceId);if(z)this.inspect(c,z.x,z.y);};accessible.append(b);
        }
        if(this.selected&&!p){const h=s.hand.find(h=>h.card.instanceId===this.selected?.instanceId);if(h)accessible.append(button('出牌',()=>this.intent('play',h.card.instanceId),!h.canPlay||this.busy),button('填能量',()=>this.intent('energy',h.card.instanceId),!h.canEnergy||this.busy));}
        if(this.selected&&s.player.field.find(m=>m.card.instanceId===this.selected?.instanceId)?.targets.some(t=>t.id==='face'))accessible.append(button('攻擊對手頭像',()=>this.intent('attack',this.selected!.instanceId),this.busy));
        this.controls.append(accessible);
        if(this.energyCandidate)this.panel='energy';
        else if(p?.kind==='choice'&&!directChoice&&!['settings','logs','player','computer','reveal'].includes(this.panel??''))this.panel='choice';
        else if(this.panel==='energy'||this.panel==='choice')this.panel=null;
        if(this.panel){
            const scroll=this.modal.scrollTop;
            const active=this.modal.querySelector<HTMLElement>(':focus')?.dataset.key;
            const content=el('div','','battle-modal-content');
            const title=this.panel==='energy'?'確認填能量':this.panel==='choice'?p!.title:this.panel==='settings'?'設定':this.panel==='logs'?'對戰紀錄':this.panel==='reveal'?s.revealTitle:this.panel==='player'?'我的墓地':'對手墓地';
            const heading=el('h2',title);heading.id='battle-modal-title';content.append(heading);this.modal.setAttribute('aria-labelledby',heading.id);
            const canClose=this.panel!=='choice'||!!p?.canCancel;
            if(canClose)content.append(button(this.panel==='energy'?'取消':'關閉',()=>this.closePanel(),this.busy));
            else if(this.sessionActions)content.append(button('設定',()=>this.openPanel('settings'),this.busy));
            if(this.panel==='energy'){
                const h=s.hand.find(h=>h.card.instanceId===this.energyCandidate);
                if(h){content.append(el('p',`將「${h.card.name}」填為能量？這張牌會離開手牌，不抽牌。`),button('確認填能量',()=>{const id=this.energyCandidate!;this.energyCandidate=null;this.panel=null;this.modal.close();void this.submit('energy',id);},this.busy));}
            }else if(this.panel==='choice'){
                content.append(el('p',p!.description));const choices=el('div','','battle-choice-options');
                for(const o of p!.options){const b=button(o.card?'':o.title,()=>this.intent('choice',undefined,undefined,o.id),this.busy);b.setAttribute('aria-label',o.title);b.dataset.key=o.id;if(o.card)b.append(this.cardDetail(o.card));else if(o.subtitle)b.append(el('p',o.subtitle));choices.append(b);}content.append(choices);
            }else if(this.panel==='settings'){
                const check=(label:string,value:boolean,change:(v:boolean)=>void)=>{const row=el('label'),i=el('input');i.type='checkbox';i.checked=value;i.onchange=()=>{try{change(i.checked);}catch{i.checked=!i.checked;content.append(el('p','設定無法儲存，請重試。'));}};row.append(i,document.createTextNode(label));content.append(row);};
                check('填能量前再次確認',readEnergyConfirmation(),saveEnergyConfirmation);
                check('加速動畫',this.fast,v=>{this.fast=v;void this.receiver.invokeMethodAsync('SetFast',v);});
                check('減少動畫',this.reduced,v=>this.reduced=v);
                check('音效',this.sound,v=>{this.sound=v;this.audio?.setEnabled(v);});
                content.append(button('全螢幕',()=>{if(document.fullscreenElement)void document.exitFullscreen();else void this.root.requestFullscreen();}));
                if(this.sessionActions){const actions=el('div','','battle-session-actions');
                    actions.append(button('投降',()=>void this.sessionAction('surrender'),this.busy||s.isOver),button('返回房間',()=>void this.sessionAction('reset'),this.busy));
                    if(s.isOver)actions.append(button('同牌組再玩一局',()=>void this.sessionAction('start'),this.busy));
                    content.append(actions);
                }
            }else if(this.panel==='logs'){
                if(!this.history.length)content.append(el('p','尚無紀錄'));
                for(const entry of this.history){
                    const row=el('div','','battle-log-entry'),line=el('p');
                    if(entry.card){
                        const card=entry.card,index=entry.text.indexOf(card.name),expanded=this.logCard===entry.id;
                        const link=button(card.name,()=>{this.logCard=expanded?null:entry.id;this.refreshControls();});
                        link.classList.add('battle-log-card');link.dataset.key=entry.id;
                        link.setAttribute('aria-label',`查看卡牌：${card.name}`);link.setAttribute('aria-expanded',String(expanded));
                        line.append(document.createTextNode(entry.text.slice(0,index)),link,document.createTextNode(entry.text.slice(index+card.name.length)));
                        row.append(line);
                        if(expanded){
                            const detail=el('div','','battle-log-detail');detail.append(this.cardDetail(card),button('收起卡牌',()=>{
                                this.logCard=null;this.refreshControls();
                                this.modal.querySelector<HTMLElement>(`[data-key="${CSS.escape(entry.id)}"]`)?.focus({preventScroll:true});
                            }));row.append(detail);
                        }
                    }else{line.textContent=entry.text;row.append(line);}
                    content.append(row);
                }
            }else{
                const cards=this.panel==='reveal'?s.revealedCards:(this.panel==='player'?s.player:s.computer).graveyard;
                if(!cards.length)content.append(el('p','墓地目前沒有卡牌'));
                const list=el('div','','battle-card-collection');for(const card of cards)list.append(this.cardDetail(card));content.append(list);
            }
            this.modal.replaceChildren(content);if(!this.modal.open)this.modal.showModal();
            if(active)this.modal.querySelector<HTMLElement>(`[data-key="${CSS.escape(active)}"]`)?.focus({preventScroll:true});
            if(this.panel==='logs')this.modal.scrollTop=scroll;
        }else if(this.modal.open)this.modal.close();
        this.paintTurn();
    }
    tone(type:string){if(this.sound&&!document.hidden)void this.audio?.play(type);}
    dispose(){this.disposed=true;this.generation++;this.abort.abort();this.resizeObserver.disconnect();cancelAnimationFrame(this.resizeFrame);clearInterval(this.watchdog);this.scene.tweens?.killAll();this.game.destroy(true);this.audio?.dispose();this.root.replaceChildren();}
}
const boards=new Map<string,Board>();
export async function create(root:HTMLElement,receiver:Receiver,animationPrototype=false,sessionActions=false){const b=new Board(root,receiver,animationPrototype,sessionActions);const id=crypto.randomUUID();boards.set(id,b);
    let timeout:ReturnType<typeof setTimeout>|undefined;
    try{await Promise.race([b.ready,new Promise((_,reject)=>{timeout=setTimeout(()=>reject(new Error('戰場初始化逾時')),8000);})]);}
    catch(error){b.dispose();boards.delete(id);throw error;}finally{clearTimeout(timeout);}return id;}
export async function update(id:string,response:Response){await boards.get(id)?.update(response);}
export function dispose(id:string){boards.get(id)?.dispose();boards.delete(id);}
// Read-only diagnostics for verification; no bypass of the real command/interaction path.
export function diagnostics(){return [...boards.values()].map(b=>({state:b.state,metrics:b.metrics,busy:b.busy,presentation:b.presentation,selected:b.scene.selected,spell:b.scene.spell,logicalWidth:b.initialized?b.scene.width:null,logicalHeight:b.initialized?b.scene.scale.height:null,layout:b.initialized?b.scene.layout:null,zones:[...b.scene.zones],
    pieces:[...b.scene.pieces].map(([id,p])=>({id,x:p.x,y:p.y,alpha:p.alpha,visible:p.visible,visualId:p.getData('visualId')})),version:Phaser.VERSION,renderer:b.game.renderer?.type===Phaser.CANVAS?'Canvas':'WebGL'}));}

export async function measureRoundTrip(){const b=[...boards.values()][0];if(!b)return [];const result:number[]=[];
    for(let i=0;i<5;i++){const start=performance.now();await b.receiver.invokeMethodAsync('Ping');result.push(performance.now()-start);}return result;}

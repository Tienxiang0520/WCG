export interface Card { instanceId:string; cardId:string; name:string; type:string; will:string; cost:number; pp:number|null; dp:number|null; text:string; art:string }
export interface Hand { card:Card; canPlay:boolean; canEnergy:boolean; problem:string; preparation:'none'|'target'|'choice'|'sacrifice'; energyProblem:string; playTargets:string[]; warning:string }
export interface Preview { attackerDies:boolean; defenderDies:boolean; attackerShieldBreaks:boolean; defenderShieldBreaks:boolean; playerDamage:number }
export interface Target { id:string; label:string; preview:Preview|null }
export interface Monster { card:Card; pp:number; dp:number; status:string[]; canAttack:boolean; problem:string; targets:Target[]; attachment:Card|null }
export interface Side { id:string; hp:number; deckCount:number; handCount:number; availableEnergy:number; totalEnergy:number; energy:{instanceId:string;tapped:boolean}[]; field:Monster[]; graveyard:Card[] }
export interface Pending { kind:string; title:string; description:string; canCancel:boolean; sourceInstanceId:string|null; options:{id:string;title:string;subtitle:string;card:Card|null}[]; targets:string[] }
export interface State { matchId:string; revision:number; turn:number; phase:string; decisionPlayerId:string; activePlayerId:string; isOver:boolean; outcome:string; player:Side; computer:Side; hand:Hand[]; pending:Pending|null; logs:string[];revealedCards:Card[];revealTitle:string }
export interface Event { id:string; revision:number; order:number; type:string; side:string; instanceId:string|null; targetId:string|null; amount:number; card:Card|null; label:string }
export interface Response { success:boolean; code:string; message:string; batchId:string; state:State; events:Event[] }
export interface Receiver { invokeMethodAsync<T=unknown>(method:string,...args:unknown[]):Promise<T> }
export interface Command { commandId:string;matchId:string;expectedRevision:number;type:string;instanceId?:string;targetId?:string;optionId?:string }

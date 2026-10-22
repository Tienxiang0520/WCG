const energyConfirmationKey='wcg.confirmEnergy';
const legacyEnergyConfirmationKey='lcg.confirmEnergy';

export function readEnergyConfirmation():boolean {
    try { return (localStorage.getItem(energyConfirmationKey)??localStorage.getItem(legacyEnergyConfirmationKey))!=='false'; }
    catch { return true; }
}

export function saveEnergyConfirmation(enabled:boolean):void {
    // Let the settings page report storage failures instead of claiming a save succeeded.
    (window as Window & {wcgCloud?:{beforePreference:(key:string,value:string)=>void}}).wcgCloud?.beforePreference(energyConfirmationKey,String(enabled));
    localStorage.setItem(energyConfirmationKey,String(enabled));
    localStorage.removeItem(legacyEnergyConfirmationKey);
    window.dispatchEvent(new CustomEvent('wcg-preference-saved'));
}

// 「顯示原始數值與加成」: off by default. Kept in this browser only (not part of cloud saves or backups).
const statBreakdownKey='wcg.showStatBreakdown';
export function readStatBreakdown():boolean {
    try { return localStorage.getItem(statBreakdownKey)==='true'; }
    catch { return false; }
}
export function saveStatBreakdown(enabled:boolean):void {
    localStorage.setItem(statBreakdownKey,String(enabled));
    window.dispatchEvent(new CustomEvent('wcg-preference-saved'));
}

const energyConfirmationKey='wcg.confirmEnergy';
const legacyEnergyConfirmationKey='lcg.confirmEnergy';

export function readEnergyConfirmation():boolean {
    try { return (localStorage.getItem(energyConfirmationKey)??localStorage.getItem(legacyEnergyConfirmationKey))!=='false'; }
    catch { return true; }
}

export function saveEnergyConfirmation(enabled:boolean):void {
    // Let the settings page report storage failures instead of claiming a save succeeded.
    localStorage.setItem(energyConfirmationKey,String(enabled));
    localStorage.removeItem(legacyEnergyConfirmationKey);
}

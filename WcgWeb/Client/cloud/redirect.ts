import { broadcastResponseToMainFrame } from '@azure/msal-browser/redirect-bridge';
broadcastResponseToMainFrame().catch(()=>{
    // No tokens, codes or detailed authentication errors are reflected into the page or logs.
    document.querySelector('[role=status]')!.textContent='登入未完成，請回遊戲再試一次。Sign-in did not complete. Please return to the game.';
});

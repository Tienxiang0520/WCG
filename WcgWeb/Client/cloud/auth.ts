import { PublicClientApplication, BrowserCacheLocation } from '@azure/msal-browser';
export async function createAuth(clientId:string, authority:string, redirectUri:string) {
    // MSAL retains the selected account across tabs and respects Microsoft's Keep me signed in choice.
    const client=new PublicClientApplication({auth:{clientId,authority,redirectUri,postLogoutRedirectUri:location.origin+'/settings'},cache:{cacheLocation:BrowserCacheLocation.LocalStorage}});
    await client.initialize();
    const response=await client.handleRedirectPromise();
    if(response?.account)client.setActiveAccount(response.account);
    return client;
}

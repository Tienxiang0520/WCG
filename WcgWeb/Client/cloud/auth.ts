import { PublicClientApplication, BrowserCacheLocation } from '@azure/msal-browser';
export async function createAuth(clientId:string, authority:string, redirectUri:string) {
    const client=new PublicClientApplication({auth:{clientId,authority,redirectUri,postLogoutRedirectUri:location.origin+'/settings'},cache:{cacheLocation:BrowserCacheLocation.SessionStorage}});
    await client.initialize();
    const response=await client.handleRedirectPromise();
    if(response?.account)client.setActiveAccount(response.account);
    return client;
}

import { readdir, stat, readFile } from 'node:fs/promises';
import { resolve, relative } from 'node:path';
const directory=resolve(process.argv[2]??'.build-tmp/azure-site/dist');
let bytes=0,files=0;
async function inspect(folder){
    for(const entry of await readdir(folder,{withFileTypes:true})){
        const path=resolve(folder,entry.name);
        const name=relative(directory,path).replaceAll('\\','/');
        if(/(?:^|\/)(?:\.git|\.openai|\.runtime|node_modules|玩家存檔備份|\.env(?:\..*)?|custom_decks\.json|ranked\.json)(?:\/|$)/.test(name))
            throw new Error(`Local-only data in public package: ${name}`);
        if(entry.isDirectory())await inspect(path);
        else if(entry.isFile()){bytes+=(await stat(path)).size;files++;}
        else throw new Error(`Unsupported package entry: ${name}`);
    }
}
await inspect(directory);
if(bytes>250_000_000||files>15_000)throw new Error('Package exceeds Azure Static Web Apps Free limits.');
const config=JSON.parse(await readFile(resolve(directory,'staticwebapp.config.json'),'utf8'));
if(config.navigationFallback?.rewrite!=='/index.html'||config.mimeTypes?.['.wasm']!=='application/wasm')
    throw new Error('Missing Azure routing or WebAssembly configuration.');
const cards=JSON.parse(await readFile(resolve(directory,'data/cards.json'),'utf8'));
if(cards.length!==199)throw new Error('Expected v0.6 catalog with 199 cards.');
for(const card of cards)await stat(resolve(directory,'card-art',`${card.Id??card.id}.webp`));
for(const route of ['','battle','cards','deckbuilder','ranked','rules','settings'])
    await stat(resolve(directory,route,'index.html'));
console.log(`Azure Free package validated: ${files} files, ${(bytes/1_000_000).toFixed(1)} MB, 199 card images.`);

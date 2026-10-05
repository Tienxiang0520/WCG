import { spawnSync } from 'node:child_process';
import { cp, mkdir, readdir, unlink, rm } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const project=resolve(dirname(fileURLToPath(import.meta.url)),'..');
const root=resolve(project,'..');
const site=resolve(root,'SoulOath.Site');
function run(command,args,cwd=project){
    const result=spawnSync(command,args,{cwd,stdio:'inherit'});
    if(result.status!==0)process.exit(result.status??1);
}
run('npm',['run','build'],resolve(root,'LcgWeb/Client'));
run('node',['tools/prepare-assets.mjs']);
run('node',['--test','tools/storage.test.mjs','tools/deck-print.test.mjs']);
await rm(resolve(project,'bin/site-publish'),{recursive:true,force:true});
run('dotnet',['publish','SoulOath.Static.csproj','--nologo','-c','Release','-o',resolve(project,'bin/site-publish')]);
await mkdir(site,{recursive:true});
await rm(resolve(site,'dist'),{recursive:true,force:true});
await cp(resolve(project,'bin/site-publish/wwwroot'),resolve(site,'dist'),{recursive:true});
async function strip(directory){
    for(const item of await readdir(directory,{withFileTypes:true})){
        const path=resolve(directory,item.name);
        if(item.isDirectory())await strip(path);
        else if(/\.(?:map|pdb)(?:\.(?:gz|br))?$/.test(item.name)||item.name==='web.config')await unlink(path);
    }
}
await strip(resolve(site,'dist'));
// Real index files keep every public route refreshable on a plain static host.
for (const route of ['battle','cards','deckbuilder','ranked','rules','settings','legacy','not-found']) {
    await mkdir(resolve(site,'dist',route),{recursive:true});
    await cp(resolve(site,'dist/index.html'),resolve(site,'dist',route,'index.html'));
}
process.stdout.write(`Site files ready: ${resolve(site,'dist')}\n`);

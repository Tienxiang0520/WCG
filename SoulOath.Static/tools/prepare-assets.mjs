import { spawnSync } from 'node:child_process';
import { cp, mkdir, copyFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
const project = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const root = resolve(project, '..');
const client = resolve(root, 'WcgWeb/Client');
if (!existsSync(resolve(root, 'WcgWeb/wwwroot/battle/board.js'))
    || !existsSync(resolve(root, 'WcgWeb/wwwroot/battle/preferences.js'))) {
    for (const args of [
        ...(['phaser', 'typescript', 'esbuild'].some(name => !existsSync(resolve(client, 'node_modules', name, 'package.json'))) ? [['ci']] : []),
        ['run', 'build']
    ]) {
        const result = spawnSync('npm', args, { cwd: client, stdio: 'inherit' });
        if (result.status !== 0) process.exit(result.status ?? 1);
    }
}
const printArt = spawnSync('python3', [resolve(root, 'WcgWeb/tools/prepare-print-art.py')], { stdio: 'inherit' });
if (printArt.status !== 0) process.exit(printArt.status ?? 1);
for (const name of ['app.css','wcg.css','favicon.png','sidebar.js','battle-drag.js','deck-print.css','deck-print.js','print-art','lib','battle']) {
    await cp(resolve(root,'WcgWeb/wwwroot',name),resolve(project,'wwwroot',name),{recursive:true});
}
await mkdir(resolve(project,'wwwroot/data'),{recursive:true});
for (const name of ['cards.json','preset_decks.json','ranked_decks.json'])
    await copyFile(resolve(root,'WcgWeb/Data',name),resolve(project,'wwwroot/data',name));

const result = spawnSync('python3', [resolve(project, 'tools/prepare-card-art.py')], { stdio: 'inherit' });
if (result.status !== 0) process.exit(result.status ?? 1);

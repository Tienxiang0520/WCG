import { readdir, readFile, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { resolve, relative } from 'node:path';
export async function createAssetManifest(directory) {
    const assets = [];
    async function walk(folder) {
        for (const entry of await readdir(folder, { withFileTypes: true })) {
            const path = resolve(folder, entry.name);
            if (entry.isDirectory()) await walk(path);
            else {
                const name = relative(directory, path).replaceAll('\\', '/');
                if (!/\.(?:js|css|json|wasm|dat|blat|webp|jpg|png|svg|ico|ogg|mp3|wav|woff2?|ttf)$/.test(name)
                    || ['asset-manifest.json','asset-worker.js','staticwebapp.config.json'].includes(name)) continue;
                const bytes = await readFile(path);
                assets.push({ path: name, size: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex') });
            }
        }
    }
    await walk(directory);
    assets.sort((a,b) => a.path.localeCompare(b.path, 'en'));
    const version = createHash('sha256').update(JSON.stringify(assets)).digest('hex');
    await writeFile(resolve(directory, 'asset-manifest.json'), JSON.stringify({ version, assets }));
    console.log(`Full preload: ${assets.length} resources, ${(assets.reduce((n,a)=>n+a.size,0)/1000000).toFixed(1)} MB.`);
    return { version, assets };
}

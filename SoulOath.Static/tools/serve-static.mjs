import { createServer } from 'node:http';
import { stat, readFile } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { resolve, extname, sep } from 'node:path';
// node serve-static.mjs <dist> [port]                 plain preview of a build folder
// node serve-static.mjs --blob-stage <stage> [port]   emulate the Azure Blob static website
//   using deploy-blob.py --stage output: exact stored bytes and headers (incl. Content-Encoding),
//   no encoding negotiation, extension-less route aliases and the not-found error document.
const args = process.argv.slice(2);
const blobMode = args[0] === '--blob-stage';
if (blobMode) args.shift();
const directory = resolve(args[0] ?? 'dist');
const port = Number(args[1] ?? 5180);
const types = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.json':'application/json; charset=utf-8','.wasm':'application/wasm','.webp':'image/webp','.png':'image/png','.svg':'image/svg+xml','.woff':'font/woff','.woff2':'font/woff2','.dat':'application/octet-stream'};
const blobHeaders = blobMode ? JSON.parse(await readFile(resolve(directory, 'headers.json'), 'utf8')) : null;
async function sendBlob(request, response, name, status = 200) {
    const path = resolve(directory, 'blobs', name.replaceAll('/', '%2F'));
    const info = await stat(path);
    response.writeHead(status, { ...blobHeaders[name], 'Content-Length': info.size, 'X-Content-Type-Options': 'nosniff' });
    if (request.method === 'HEAD') response.end(); else createReadStream(path).pipe(response);
}
async function serveBlob(request, response, pathname) {
    // Blob static website: "/" and "folder/" map to index.html; otherwise exact blob names.
    let name = pathname.slice(1);
    if (name === '' || name.endsWith('/')) name += 'index.html';
    if (blobHeaders[name]) return sendBlob(request, response, name);
    if (blobHeaders[`${name}/index.html`] && !blobHeaders[name]) return sendBlob(request, response, `${name}/index.html`);
    return sendBlob(request, response, 'not-found/index.html', 404);
}
createServer(async (request,response)=>{
    try {
        if (!['GET','HEAD'].includes(request.method)) {response.writeHead(405);response.end();return;}
        const pathname = decodeURIComponent(new URL(request.url,'http://localhost').pathname);
        if (blobMode) { await serveBlob(request, response, pathname); return; }
        let path = resolve(directory,'.'+pathname);
        if (path!==directory && !path.startsWith(directory+sep)) {response.writeHead(403);response.end();return;}
        let info;
        try {info=await stat(path);}catch{}
        if (info?.isDirectory()) {
            path=resolve(path,'index.html');
            try {info=await stat(path);}catch {info=undefined;}
        }
        if (!info?.isFile()) {
            if (extname(pathname)) {response.writeHead(404);response.end('Not found');return;}
            path=resolve(directory,'index.html');info=await stat(path);
        }
        response.writeHead(200,{'Content-Type':types[extname(path)]??'application/octet-stream','Content-Length':info.size,'Cache-Control':'no-cache','X-Content-Type-Options':'nosniff'});
        if(request.method==='HEAD')response.end();else createReadStream(path).pipe(response);
    } catch {response.writeHead(500);response.end('Unable to read static file');}
}).listen(port,'127.0.0.1',()=>process.stdout.write(`${blobMode ? 'Blob emulation' : 'Static preview'}: http://127.0.0.1:${port}\n`));

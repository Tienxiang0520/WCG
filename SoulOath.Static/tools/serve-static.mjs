import { createServer } from 'node:http';
import { stat } from 'node:fs/promises';
import { createReadStream } from 'node:fs';
import { resolve, extname, sep } from 'node:path';
const directory = resolve(process.argv[2] ?? 'dist');
const port = Number(process.argv[3] ?? 5180);
const types = {'.html':'text/html; charset=utf-8','.css':'text/css; charset=utf-8','.js':'text/javascript; charset=utf-8','.json':'application/json; charset=utf-8','.wasm':'application/wasm','.webp':'image/webp','.png':'image/png','.svg':'image/svg+xml','.woff':'font/woff','.woff2':'font/woff2','.dat':'application/octet-stream'};
createServer(async (request,response)=>{
    try {
        if (!['GET','HEAD'].includes(request.method)) {response.writeHead(405);response.end();return;}
        const pathname = decodeURIComponent(new URL(request.url,'http://localhost').pathname);
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
}).listen(port,'127.0.0.1',()=>process.stdout.write(`Static preview: http://127.0.0.1:${port}\n`));

// Brotli payloads for deploy-blob.py (Python's standard library has no Brotli).
// stdin: JSON [{ "source": path, "output": path }]. An existing output is reused only
// when it decodes back to the exact source bytes; otherwise it is re-encoded at quality 11.
// Every output is verified by decoding before this script reports success.
import { readFileSync, writeFileSync, existsSync, renameSync, mkdirSync } from 'node:fs';
import { dirname } from 'node:path';
import { brotliCompressSync, brotliDecompressSync, constants } from 'node:zlib';
const jobs = JSON.parse(readFileSync(0, 'utf8'));
let encoded = 0, reused = 0;
for (const { source, output } of jobs) {
    const bytes = readFileSync(source);
    if (existsSync(output)) {
        try { if (brotliDecompressSync(readFileSync(output)).equals(bytes)) { reused++; continue; } } catch { /* re-encode */ }
    }
    const packed = brotliCompressSync(bytes, { params: {
        [constants.BROTLI_PARAM_QUALITY]: 11,
        [constants.BROTLI_PARAM_SIZE_HINT]: bytes.length,
        [constants.BROTLI_PARAM_MODE]: /\.(?:js|css|json|html|svg|txt|md)$/.test(source) ? constants.BROTLI_MODE_TEXT : constants.BROTLI_MODE_GENERIC } });
    if (!brotliDecompressSync(packed).equals(bytes)) throw new Error(`Brotli round trip failed: ${source}`);
    mkdirSync(dirname(output), { recursive: true });
    writeFileSync(`${output}.tmp`, packed); renameSync(`${output}.tmp`, output);
    encoded++;
}
process.stdout.write(JSON.stringify({ encoded, reused }) + '\n');

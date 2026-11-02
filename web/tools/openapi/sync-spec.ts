/**
 * `npm run api:sync [-- <path-or-url>]` — pins the Api spec the portal builds against
 * (LOYALTY_MICROSERVICES_ARCHITECTURE.md 7.3/7.4): copies openapi.json from a dengage.loyalty.api
 * checkout (default: ../dengage.loyalty.api, next to this repository), a file, or an http(s) URL
 * (for example an api build artifact), then regenerates the types and records the source in
 * openapi/SPEC_SOURCE. Commit all three. Replaced by the @dengage/loyalty-api-spec package once
 * the Nexus npm repository exists.
 */
import { createHash } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, relative, resolve, sep } from 'node:path';
import { SIBLING_API_SPEC, SPEC_PATH, SPEC_SOURCE_PATH, TYPES_PATH, WEB_ROOT } from './paths';
import { renderTypes } from './render-types';

const source = process.argv[2] ?? SIBLING_API_SPEC;

let text: string;
let label = source;
if (/^https?:\/\//i.test(source)) {
  const response = await fetch(source);
  if (!response.ok) throw new Error(`GET ${source} → ${response.status} ${response.statusText}`);
  text = await response.text();
} else {
  const path = resolve(source);
  if (!existsSync(path))
    throw new Error(`${path} not found — pass the path or URL of a dengage.loyalty.api openapi.json.`);
  text = readFileSync(path, 'utf8');
  // Recorded relative to this repository, so SPEC_SOURCE doesn't carry a developer's local path.
  label = relative(WEB_ROOT, path).split(sep).join('/');
}

const spec = JSON.parse(text) as { openapi?: string; paths?: object };
if (!spec.openapi?.startsWith('3.') || !spec.paths)
  throw new Error(`${source} is not an OpenAPI 3 document.`);

writeFileSync(SPEC_PATH, text);
mkdirSync(dirname(TYPES_PATH), { recursive: true });
writeFileSync(TYPES_PATH, await renderTypes(text));
const sha256 = createHash('sha256').update(text).digest('hex');
writeFileSync(SPEC_SOURCE_PATH, `source: ${label}\nsha256: ${sha256}\nsynced: ${new Date().toISOString()}\n`);
console.log(
  `Pinned ${label} (${Object.keys(spec.paths).length} paths); types regenerated. Commit openapi/ and the generated types.`,
);

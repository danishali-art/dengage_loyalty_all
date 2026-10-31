import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

export const WEB_ROOT = resolve(here, '../..');
export const REPO_ROOT = resolve(WEB_ROOT, '..');
export const SPEC_PATH = resolve(WEB_ROOT, 'openapi/openapi.json');
/** Where the pinned spec came from (written by api:sync), for review and for the next sync. */
export const SPEC_SOURCE_PATH = resolve(WEB_ROOT, 'openapi/SPEC_SOURCE');
export const TYPES_PATH = resolve(WEB_ROOT, 'src/shared/api/generated/schema.d.ts');
/** The spec in a dengage.loyalty.api checkout next to this repository (api:sync's default). */
export const SIBLING_API_SPEC = resolve(WEB_ROOT, '../dengage.loyalty.api/openapi/openapi.json');

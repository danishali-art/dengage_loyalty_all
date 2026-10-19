import { dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const here = dirname(fileURLToPath(import.meta.url));

export const WEB_ROOT = resolve(here, '../..');
export const REPO_ROOT = resolve(WEB_ROOT, '..');
export const SPEC_PATH = resolve(WEB_ROOT, 'openapi/openapi.json');
export const TYPES_PATH = resolve(WEB_ROOT, 'src/shared/api/generated/schema.d.ts');

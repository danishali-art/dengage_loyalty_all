/** `npm run api:types` — writes src/shared/api/generated/schema.d.ts from openapi/openapi.json. */
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname } from 'node:path';
import { SPEC_PATH, TYPES_PATH } from './paths';
import { renderTypes } from './render-types';

const types = await renderTypes(readFileSync(SPEC_PATH, 'utf8'));
mkdirSync(dirname(TYPES_PATH), { recursive: true });
writeFileSync(TYPES_PATH, types);
console.log('src/shared/api/generated/schema.d.ts written');

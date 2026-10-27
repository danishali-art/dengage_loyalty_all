/** `npm run api:spec` — writes openapi/openapi.json from the backend's C# source. */
import { writeFileSync } from 'node:fs';
import { buildSpec, serializeSpec } from './build-spec';
import { REPO_ROOT, SPEC_PATH } from './paths';

const spec = buildSpec({ repoRoot: REPO_ROOT });
writeFileSync(SPEC_PATH, serializeSpec(spec));
const paths = Object.keys(spec['paths'] as object).length;
const schemas = Object.keys((spec['components'] as { schemas: object }).schemas).length;
console.log(`openapi/openapi.json: ${paths} paths, ${schemas} schemas`);

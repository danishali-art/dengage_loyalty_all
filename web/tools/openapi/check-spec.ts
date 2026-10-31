/**
 * `npm run api:check` (also runs before every build): fails if the generated types differ from the
 * ones the pinned spec (openapi/openapi.json) produces. It needs no backend source, so the portal
 * builds on its own — as in the split's dengage.loyalty.frontend, where the spec is pinned by
 * `npm run api:sync` and the Api build owns the drift and breaking-change gates.
 *
 * `npm run api:check:backend` (`--backend`): while the backend still lives in this repository, also
 * regenerates the spec from the C# source and fails if it differs from the committed openapi.json.
 */
import { existsSync, readFileSync } from 'node:fs';
import { buildSpec, serializeSpec } from './build-spec';
import { REPO_ROOT, SPEC_PATH, TYPES_PATH } from './paths';
import { renderTypes } from './render-types';

if (!existsSync(SPEC_PATH)) {
  console.error(
    '✖ openapi/openapi.json is missing — run `npm run api:spec` or pin a spec with `npm run api:sync`.',
  );
  process.exit(1);
}
const spec = readFileSync(SPEC_PATH, 'utf8');

if (process.argv.includes('--backend') && serializeSpec(buildSpec({ repoRoot: REPO_ROOT })) !== spec) {
  console.error('✖ openapi/openapi.json is out of date with the backend C# source — run `npm run api:spec`.');
  process.exit(1);
}

const types = await renderTypes(spec);
if (!existsSync(TYPES_PATH) || readFileSync(TYPES_PATH, 'utf8') !== types) {
  console.error(
    '✖ src/shared/api/generated/schema.d.ts is out of date with openapi/openapi.json — run `npm run api:types`.',
  );
  process.exit(1);
}
console.log(
  process.argv.includes('--backend')
    ? '✔ OpenAPI spec and generated types match the backend source.'
    : '✔ Generated types match the pinned OpenAPI spec.',
);

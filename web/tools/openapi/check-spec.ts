/**
 * `npm run api:check` (also runs before every build): regenerates the spec from the C# source and
 * the types from the spec, and fails if either differs from the committed file. This is the drift
 * guard: a backend DTO or route change that hasn't been carried into the portal stops the build
 * instead of failing at runtime.
 */
import { existsSync, readFileSync } from 'node:fs';
import { buildSpec, serializeSpec } from './build-spec';
import { REPO_ROOT, SPEC_PATH, TYPES_PATH } from './paths';
import { renderTypes } from './render-types';

const problems: string[] = [];

const spec = serializeSpec(buildSpec({ repoRoot: REPO_ROOT }));
if (!existsSync(SPEC_PATH) || readFileSync(SPEC_PATH, 'utf8') !== spec) {
  problems.push('openapi/openapi.json is out of date with the backend C# source — run `npm run api:spec`.');
}

const types = await renderTypes(spec);
if (!existsSync(TYPES_PATH) || readFileSync(TYPES_PATH, 'utf8') !== types) {
  problems.push('src/shared/api/generated/schema.d.ts is out of date — run `npm run api:types`.');
}

if (problems.length) {
  for (const p of problems) console.error(`✖ ${p}`);
  process.exit(1);
}
console.log('✔ OpenAPI spec and generated types match the backend source.');

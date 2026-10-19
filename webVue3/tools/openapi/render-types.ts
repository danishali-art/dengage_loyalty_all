import openapiTS, { astToString, type OpenAPI3 } from 'openapi-typescript';

const HEADER =
  '/**\n * GENERATED from openapi/openapi.json by `npm run api:types` — do not edit.\n' +
  ' * The spec itself is generated from the backend C# source (`npm run api:spec`).\n */\n\n';

/** The TypeScript the app imports, rendered the same way by api:types and api:check. */
export async function renderTypes(specJson: string): Promise<string> {
  const ast = await openapiTS(JSON.parse(specJson) as OpenAPI3, { alphabetize: true });
  return HEADER + astToString(ast);
}

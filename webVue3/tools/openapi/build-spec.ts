/**
 * Builds the OpenAPI 3.1 document from the backend's C# source (scope change
 * docs/scope-changes/2026-10-12-vue3-portal-rewrite.md, D4). Reads files only — the backend is
 * never changed or run. Every mapping rule below mirrors how the API actually serializes:
 *
 * - `JsonConventions.Options` (Api.Framework/Json): camelCase names, `WhenWritingNull` — a null
 *   member is OMITTED, so nullable members are optional — and `DecimalStringJsonConverter`, so
 *   `decimal` travels as a string.
 * - `[JsonPropertyName]` wins over the camelCase policy (the engine's DSL models).
 * - No `JsonStringEnumConverter` is registered, so enums travel as integers unless the enum
 *   itself opts in.
 *
 * Anything that doesn't fit these rules throws: the fix is an override in openapi/overrides.ts
 * (with a comment naming the C# source), never a guess.
 */
import { existsSync, readFileSync, readdirSync, statSync } from 'node:fs';
import { join, relative, sep } from 'node:path';
import {
  CsEnumType,
  CsFile,
  CsInterface,
  CsController,
  CsObjectType,
  CsRoute,
  CsType,
  parseController,
  parseFile,
  parseTypes,
  splitTopLevel,
} from './csharp-parse';
import { OVERRIDES as DEFAULT_OVERRIDES, type Overrides } from '../../openapi/overrides';

type Schema = Record<string, unknown>;

export interface BuildOptions {
  /** Repository root (contains `src/`). */
  repoRoot: string;
  /** Defaults to openapi/overrides.ts; tests pass their own. */
  overrides?: Overrides;
}

/** Projects whose types can appear on the wire. */
const TYPE_SOURCE_DIRS = [
  'src/dEngage.Loyalty.Api',
  'src/dEngage.Loyalty.Api.Framework',
  'src/dEngage.Loyalty.RuleEngine',
  'src/dEngage.Loyalty.Shared',
  'src/dEngage.Loyalty.Contract',
  'src/dEngage.Loyalty.Schema',
  'src/dEngage.Loyalty.Ledger',
  'src/dEngage.Loyalty.Engine.Framework',
];

/**
 * Controller-private query helpers (`CustomersController`), by name → the schema of the value they read.
 * A helper that isn't listed here makes the generator fail.
 */
const QUERY_HELPERS: Record<string, Schema> = {
  QueryString: { type: 'string' },
  QueryGuid: { type: 'string', format: 'uuid' },
  QueryUtc: { type: 'string', format: 'date-time' },
};

const PRIMITIVES: Record<string, Schema> = {
  string: { type: 'string' },
  char: { type: 'string' },
  bool: { type: 'boolean' },
  byte: { type: 'integer' },
  short: { type: 'integer' },
  int: { type: 'integer', format: 'int32' },
  long: { type: 'integer', format: 'int64' },
  float: { type: 'number' },
  double: { type: 'number' },
  // DecimalStringJsonConverter: numeric(20,4) on the server, a string on the wire.
  decimal: { type: 'string', format: 'decimal', pattern: '^-?\\d+(\\.\\d+)?$' },
  Guid: { type: 'string', format: 'uuid' },
  DateTime: { type: 'string', format: 'date-time' },
  DateTimeOffset: { type: 'string', format: 'date-time' },
  DateOnly: { type: 'string', format: 'date' },
  TimeSpan: { type: 'string' },
  // Free-form JSON (jsonb configs such as reward `typeConfig`); their inner keys are validated
  // by the resource's registry/validator, not described here.
  JsonElement: {},
  JsonDocument: {},
};

const LIST_TYPES = new Set([
  'List',
  'IList',
  'IReadOnlyList',
  'IEnumerable',
  'ICollection',
  'IReadOnlyCollection',
  'HashSet',
  'ISet',
]);
const DICT_TYPES = new Set(['Dictionary', 'IDictionary', 'IReadOnlyDictionary']);

interface TypeRef {
  name: string;
  args: TypeRef[];
  nullable: boolean;
  array: boolean;
}

export function parseTypeRef(text: string): TypeRef {
  let t = text.trim();
  let nullable = false;
  let array = false;
  if (t.endsWith('?')) {
    nullable = true;
    t = t.slice(0, -1).trim();
  }
  if (t.endsWith('[]')) {
    array = true;
    t = t.slice(0, -2).trim();
  }
  const lt = t.indexOf('<');
  if (lt === -1) return { name: stripNs(t), args: [], nullable, array };
  const args = splitTopLevel(t.slice(lt + 1, t.lastIndexOf('>'))).map(parseTypeRef);
  return { name: stripNs(t.slice(0, lt)), args, nullable, array };
}

function stripNs(name: string): string {
  const parts = name.split('.');
  return parts[parts.length - 1] ?? name;
}

function camel(name: string): string {
  return name.charAt(0).toLowerCase() + name.slice(1);
}

function walk(dir: string, out: string[]): void {
  for (const entry of readdirSync(dir)) {
    if (entry === 'bin' || entry === 'obj') continue;
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith('.cs')) out.push(full);
  }
}

export function buildSpec({ repoRoot, overrides = DEFAULT_OVERRIDES }: BuildOptions): Schema {
  const OVERRIDES = overrides;
  const files: CsFile[] = [];
  for (const dir of TYPE_SOURCE_DIRS) {
    const paths: string[] = [];
    if (!existsSync(join(repoRoot, dir))) continue;
    walk(join(repoRoot, dir), paths);
    for (const p of paths.sort()) {
      files.push(parseFile(relative(repoRoot, p).split(sep).join('/'), readFileSync(p, 'utf8')));
    }
  }

  // ── type index ──
  const byName = new Map<string, CsType[]>();
  const fileOf = new Map<CsType, CsFile>();
  for (const f of files) {
    for (const t of parseTypes(f)) {
      byName.set(t.name, [...(byName.get(t.name) ?? []), t]);
      fileOf.set(t, f);
    }
  }

  /** Resolves a simple type name as the C# compiler would from `context`'s namespace + usings. */
  function resolve(name: string, context: CsFile): CsType {
    const candidates = byName.get(name) ?? [];
    if (candidates.length === 1 && candidates[0]) return candidates[0];
    const visible = candidates.filter((c) => {
      const ns = c.namespace;
      return (
        context.namespace === ns || context.namespace.startsWith(ns + '.') || context.usings.includes(ns)
      );
    });
    if (visible.length === 1 && visible[0]) return visible[0];
    throw new Error(
      candidates.length === 0
        ? `Unknown type '${name}' (referenced from ${context.path})`
        : `Ambiguous type '${name}' from ${context.path}: ${candidates.map((c) => c.file).join(', ')}`,
    );
  }

  // ── schemas ──
  const components: Record<string, Schema> = {};
  const inProgress = new Set<string>();
  const usedOverrides = new Set<string>();

  function schemaFor(ref: TypeRef, context: CsFile, generics: Map<string, Schema> = new Map()): Schema {
    let schema = baseSchema(ref, context, generics);
    if (ref.array) schema = { type: 'array', items: schema };
    return schema;
  }

  function baseSchema(ref: TypeRef, context: CsFile, generics: Map<string, Schema>): Schema {
    const generic = generics.get(ref.name);
    if (generic) return generic;
    if (ref.name === 'Nullable' && ref.args[0]) return schemaFor(ref.args[0], context, generics);
    const primitive = PRIMITIVES[ref.name];
    if (primitive) return { ...primitive };
    if (LIST_TYPES.has(ref.name) && ref.args[0]) {
      return { type: 'array', items: schemaFor(ref.args[0], context, generics) };
    }
    if (DICT_TYPES.has(ref.name) && ref.args[1]) {
      return { type: 'object', additionalProperties: schemaFor(ref.args[1], context, generics) };
    }
    if (ref.name === 'object' || ref.name === 'dynamic') {
      throw new Error(`'${ref.name}' has no wire shape (referenced from ${context.path}); add an override`);
    }
    const type = resolve(ref.name, context);
    return { $ref: `#/components/schemas/${componentFor(type, ref.args, context, generics)}` };
  }

  function componentFor(
    type: CsType,
    args: TypeRef[],
    context: CsFile,
    generics: Map<string, Schema>,
  ): string {
    if (type.kind === 'interface') throw new Error(`Interface '${type.name}' used as a wire type`);
    const argNames = args.map((a) => typeRefName(a));
    const name = argNames.length ? `${type.name}_${argNames.join('_')}` : type.name;
    if (components[name] || inProgress.has(name)) return name;
    inProgress.add(name);
    const typeFile = fileOf.get(type);
    if (!typeFile) throw new Error(`No file for ${type.name}`);
    if (type.kind === 'enum') {
      components[name] = enumSchema(type);
    } else {
      const bound = new Map<string, Schema>();
      type.typeParams.forEach((p, i) => {
        const arg = args[i];
        if (!arg) throw new Error(`Generic '${type.name}' used without type argument ${p}`);
        bound.set(p, schemaFor(arg, context, generics));
      });
      components[name] = objectSchema(type, typeFile, bound);
    }
    inProgress.delete(name);
    return name;
  }

  function typeRefName(ref: TypeRef): string {
    const base = ref.args.length ? `${ref.name}_${ref.args.map(typeRefName).join('_')}` : ref.name;
    return ref.array ? `${base}Array` : base;
  }

  function enumSchema(type: CsEnumType): Schema {
    if (type.asString) return { type: 'string', enum: type.members.map((m) => m.name) };
    return {
      type: 'integer',
      enum: type.members.map((m) => m.value),
      'x-enum-varnames': type.members.map((m) => m.name),
      description: `C# enum ${type.name} — serialized as its number (no JsonStringEnumConverter).`,
    };
  }

  function collectProperties(
    type: CsObjectType,
    file: CsFile,
  ): { props: CsObjectType['properties']; file: CsFile }[] {
    const own = { props: type.properties, file };
    if (!type.baseType) return [own];
    const baseRef = parseTypeRef(type.baseType);
    let base: CsType;
    try {
      base = resolve(baseRef.name, file);
    } catch {
      return [own]; // framework base type (e.g. Exception) — nothing serialized from it
    }
    if (base.kind === 'enum' || base.kind === 'interface') return [own];
    const baseFile = fileOf.get(base);
    return baseFile ? [...collectProperties(base, baseFile), own] : [own];
  }

  function objectSchema(type: CsObjectType, file: CsFile, generics: Map<string, Schema>): Schema {
    const properties: Record<string, Schema> = {};
    const required: string[] = [];
    // A defaulted positional parameter (`string Rounding = "down"`) may be left out of a request
    // body, but a response always carries every non-null member. DTOs are named `*Request` /
    // `*Response` (backend-api.md), so the name tells which side the type is on.
    const isRequest = /Request$/.test(type.name);
    for (const { props, file: f } of collectProperties(type, file)) {
      for (const p of props) {
        const wireName = p.jsonName ?? camel(p.name);
        const override = OVERRIDES.stringUnions[`${type.name}.${wireName}`];
        if (override) usedOverrides.add(`${type.name}.${wireName}`);
        const ref = parseTypeRef(p.type);
        let schema: Schema = override
          ? { type: 'string', enum: override.values }
          : schemaFor(ref, f, generics);
        // A list member (`string[]` or `IReadOnlyList<string>`) holds the allowed values per item.
        const isList = ref.array || (LIST_TYPES.has(ref.name) && ref.args.length === 1);
        if (override && isList) schema = { type: 'array', items: schema };
        // WhenWritingNull: a nullable member is left out of the JSON when null.
        if (ref.nullable) {
          schema = schema['$ref'] ? { anyOf: [schema, { type: 'null' }] } : withNull(schema);
        } else if (!(p.hasDefault && isRequest)) {
          required.push(wireName);
        }
        properties[wireName] = schema;
      }
    }
    const result: Schema = { type: 'object', properties };
    if (required.length) result['required'] = required;
    return result;
  }

  function withNull(schema: Schema): Schema {
    const t = schema['type'];
    if (typeof t === 'string') {
      const nullable: Schema = { ...schema, type: [t, 'null'] };
      if (Array.isArray(schema['enum'])) nullable['enum'] = [...(schema['enum'] as unknown[]), null];
      return nullable;
    }
    if (Object.keys(schema).length === 0) return schema; // free-form JSON already admits null
    return { anyOf: [schema, { type: 'null' }] };
  }

  // ── paths ──
  const paths: Record<string, Record<string, Schema>> = {};
  const operationIds = new Set<string>();
  const apiFiles = files.filter((f) => /^src\/dEngage\.Loyalty\.Api\/[^/]+\/[^/]+Controller\.cs$/.test(f.path));

  for (const f of apiFiles) {
    const module = parseController(f);
    if (!module || OVERRIDES.excludeControllers.includes(module.name)) continue;
    for (const route of module.routes) {
      const operation = buildOperation(module, route, f);
      const key = route.path || '/';
      paths[key] ??= {};
      if (paths[key][route.method]) throw new Error(`Duplicate route ${route.method.toUpperCase()} ${key}`);
      paths[key][route.method] = operation;
    }
  }

  function serviceInterface(module: CsController,varName: string, f: CsFile): CsInterface {
    const typeName = module.ctorParams.get(varName);
    if (!typeName) throw new Error(`${module.name}: '${varName}' is not a constructor parameter`);
    const t = resolve(typeName, f);
    if (t.kind !== 'interface') throw new Error(`${module.name}: ${typeName} is not an interface`);
    return t;
  }

  function buildOperation(module: CsController, route: CsRoute, f: CsFile): Schema {
    const where = `${module.name} ${route.method.toUpperCase()} ${route.path}`;
    const body = route.body;
    const resource = module.name.replace(/Controller$/, '');

    // Path parameters.
    const parameters: Schema[] = [];
    for (const m of route.path.matchAll(/\{(\w+)\}/g)) {
      const name = m[1] ?? '';
      const isGuid = new RegExp(`RouteGuidParam\\(\\s*"${name}"`).test(body);
      parameters.push({
        name,
        in: 'path',
        required: true,
        schema: isGuid ? { type: 'string', format: 'uuid' } : { type: 'string' },
      });
    }

    // Query parameters.
    const query = new Map<string, Schema>();
    if (/\bReadPageRequest\(\)/.test(body)) {
      query.set('page', { type: 'integer', minimum: 1 });
      query.set('pageSize', { type: 'integer', minimum: 1, maximum: 100 });
    }
    // CustomersController.ReadLimit(): `limit`, clamped to 1..100, default 25.
    if (/\bReadLimit\(\)/.test(body)) query.set('limit', { type: 'integer', minimum: 1, maximum: 100 });
    for (const m of body.matchAll(/(?:(\w+)\.TryParse\(\s*)?\(string\?\)\s*Request\.Query\["(\w+)"\]/g)) {
      const parsedAs = m[1];
      const schema: Schema =
        parsedAs === 'Guid'
          ? { type: 'string', format: 'uuid' }
          : parsedAs === 'DateTime' || parsedAs === 'DateTimeOffset'
            ? { type: 'string', format: 'date-time' }
            : parsedAs === 'bool'
              ? { type: 'boolean' }
              : parsedAs === 'int'
                ? { type: 'integer' }
                : { type: 'string' };
      query.set(m[2] ?? '', schema);
    }
    for (const m of body.matchAll(/\b(Query\w+)\(\s*"(\w+)"\s*\)/g)) {
      const helper = QUERY_HELPERS[m[1] ?? ''];
      if (!helper) throw new Error(`${where}: unknown query helper ${m[1]}(…) — add it to QUERY_HELPERS`);
      query.set(m[2] ?? '', helper);
    }
    for (const [name, schema] of [...query].sort(([a], [b]) => a.localeCompare(b))) {
      parameters.push({ name, in: 'query', required: false, schema });
    }
    // EventsController.IdempotencyKey(): the header becomes the event id.
    if (/\bIdempotencyKey\(\)/.test(body)) {
      parameters.push({ name: 'Idempotency-Key', in: 'header', required: false, schema: { type: 'string' } });
    }

    // Request body.
    let requestType: string | null = null;
    const validated = /ReadValidatedJsonBodyAsync\(\s*(\w+)/.exec(body);
    if (validated) {
      const validatorType = module.ctorParams.get(validated[1] ?? '');
      const vm = validatorType ? /^IValidator<(.+)>$/.exec(validatorType) : null;
      if (!vm) throw new Error(`${where}: cannot find the request type of validator '${validated[1]}'`);
      requestType = vm[1] ?? null;
    }
    const unvalidated = /ReadJsonBodyAsync<([^>]+)>/.exec(body);
    if (unvalidated) requestType = unvalidated[1] ?? null;

    // Response.
    const responses: Record<string, Schema> = {};
    const problem = {
      description: 'Error (RFC 7807 problem details)',
      content: {
        'application/problem+json': { schema: { $ref: '#/components/schemas/ProblemDetailsBody' } },
      },
    };
    if (/JsonResponses\.NoContent\(\)/.test(body)) {
      responses['204'] = { description: 'No content' };
    } else {
      const services = [...module.ctorParams].filter(([, t]) => /^I\w+AppService$/.test(t)).map(([v]) => v);
      const call = new RegExp(`\\b(${services.join('|')})\\.(\\w+)\\s*\\(`).exec(body);
      if (!call) throw new Error(`${where}: no app-service call found for the response`);
      const iface = serviceInterface(module, call[1] ?? '', f);
      const methods = iface.methods.filter((m) => m.name === call[2]);
      const returnTypes = new Set(methods.map((m) => m.returnType));
      if (returnTypes.size !== 1)
        throw new Error(`${where}: ${iface.name}.${call[2]} not found or overloaded`);
      let ret = parseTypeRef([...returnTypes][0] ?? '');
      if (ret.name === 'Task' || ret.name === 'ValueTask') {
        if (!ret.args[0])
          throw new Error(
            `${where}: ${iface.name}.${call[2]} returns no value but the route responds with one`,
          );
        ret = ret.args[0];
      }
      const ifaceFile = fileOf.get(iface);
      if (!ifaceFile) throw new Error(`No file for ${iface.name}`);
      const status = /\bAccepted\(|HttpStatusCode\.Accepted/.test(body)
        ? '202'
        : /HttpStatusCode\.Created/.test(body)
          ? '201'
          : '200';
      responses[status] = {
        description: 'OK',
        content: { 'application/json': { schema: schemaFor(ret, ifaceFile) } },
      };
    }
    responses['default'] = problem;

    const operationId = `${camel(resource)}_${route.method}_${route.path
      .replace(/^\/api\/v1\//, '')
      .replace(/[{}]/g, '')
      .replace(/[^\w]+/g, '_')}`;
    if (operationIds.has(operationId)) throw new Error(`${where}: duplicate operationId ${operationId}`);
    operationIds.add(operationId);

    const operation: Schema = { operationId, tags: [resource], responses };
    if (parameters.length) operation['parameters'] = parameters;
    if (requestType) {
      operation['requestBody'] = {
        required: true,
        content: { 'application/json': { schema: schemaFor(parseTypeRef(requestType), f) } },
      };
    }
    if (module.name === 'AuthController') operation['security'] = [];
    return operation;
  }

  // The error body every route can return.
  const problemType = resolve(
    'ProblemDetailsBody',
    files.find((f) => f.path.includes('ErrorResponseFactory')) ?? files[0]!,
  );
  componentFor(problemType, [], fileOf.get(problemType)!, new Map());

  // An override whose member was renamed or removed in C# must not linger unnoticed.
  const stale = Object.keys(OVERRIDES.stringUnions).filter((k) => !usedOverrides.has(k));
  if (stale.length)
    throw new Error(`Overrides match no C# member (renamed or removed?): ${stale.join(', ')}`);

  return {
    openapi: '3.1.0',
    info: {
      title: 'dEngage.Loyalty API',
      version: '1',
      description:
        'GENERATED by webVue3/tools/openapi from the backend C# source — do not edit. ' +
        'Run `npm run api:spec` in webVue3 after a backend DTO or route change.',
    },
    servers: [{ url: '/' }],
    security: [{ bearer: [] }],
    paths: sortKeys(paths),
    components: {
      securitySchemes: { bearer: { type: 'http', scheme: 'bearer', bearerFormat: 'JWT' } },
      schemas: sortKeys(components),
    },
  };
}

function sortKeys<T>(obj: Record<string, T>): Record<string, T> {
  return Object.fromEntries(Object.entries(obj).sort(([a], [b]) => a.localeCompare(b)));
}

/** Stable text form, so the committed file only changes when the contract does. */
export function serializeSpec(spec: Schema): string {
  return JSON.stringify(spec, null, 2) + '\n';
}

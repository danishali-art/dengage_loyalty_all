/**
 * A deliberately small C# reader for the generator. It understands exactly the shapes the
 * backend uses (`.claude/rules/backend-api.md`): positional `sealed record` DTOs, classes with
 * `{ get; set/init; }` properties, enums, app-service interfaces and Nancy modules. Anything
 * else is reported as an error by the caller rather than guessed.
 */

export interface CsProperty {
  /** C# member name, e.g. `MinPoints`. */
  name: string;
  /** Raw C# type text, e.g. `IReadOnlyList<TierResponse>?`. */
  type: string;
  /** From `[JsonPropertyName("…")]`; overrides the camelCase policy. */
  jsonName: string | null;
  /** Positional parameter with a default value (`= null`), i.e. optional in a request body. */
  hasDefault: boolean;
  /** `required` modifier on a class property. */
  isRequired: boolean;
}

export interface CsObjectType {
  kind: 'record' | 'class' | 'struct';
  name: string;
  typeParams: string[];
  baseType: string | null;
  properties: CsProperty[];
  namespace: string;
  file: string;
}

export interface CsEnumType {
  kind: 'enum';
  name: string;
  members: { name: string; value: number }[];
  /** `[JsonConverter(typeof(JsonStringEnumConverter))]` on the enum — serialized by name. */
  asString: boolean;
  namespace: string;
  file: string;
}

export interface CsInterface {
  kind: 'interface';
  name: string;
  methods: { name: string; returnType: string }[];
  namespace: string;
  file: string;
}

export type CsType = CsObjectType | CsEnumType | CsInterface;

export interface CsFile {
  path: string;
  namespace: string;
  usings: string[];
  /** Source with comments removed (string literals kept). */
  code: string;
}

/** Removes `//` and `/* *\/` comments, leaving string and char literals intact. */
export function stripComments(src: string): string {
  let out = '';
  let i = 0;
  while (i < src.length) {
    const c = src[i];
    const next = src[i + 1];
    if (c === '/' && next === '/') {
      while (i < src.length && src[i] !== '\n') i++;
      continue;
    }
    if (c === '/' && next === '*') {
      const end = src.indexOf('*/', i + 2);
      i = end === -1 ? src.length : end + 2;
      continue;
    }
    if (c === '"' || c === "'") {
      const verbatim = src[i - 1] === '@';
      let j = i + 1;
      while (j < src.length) {
        if (!verbatim && src[j] === '\\') {
          j += 2;
          continue;
        }
        if (src[j] === c) {
          if (verbatim && src[j + 1] === c) {
            j += 2;
            continue;
          }
          break;
        }
        j++;
      }
      out += src.slice(i, j + 1);
      i = j + 1;
      continue;
    }
    out += c;
    i++;
  }
  return out;
}

const OPEN: Record<string, string> = { '(': ')', '{': '}', '[': ']', '<': '>' };

/** Index of the bracket that closes the one at `start`. Skips string literals. */
export function matchBracket(code: string, start: number): number {
  const open = code[start] ?? '';
  const close = OPEN[open];
  if (!close) throw new Error(`matchBracket: '${open}' at ${start} is not an opening bracket`);
  let depth = 0;
  for (let i = start; i < code.length; i++) {
    const ch = code[i];
    if (ch === '"') {
      i = skipString(code, i);
      continue;
    }
    if (ch === open) depth++;
    else if (ch === close) {
      depth--;
      if (depth === 0) return i;
    }
  }
  throw new Error(`matchBracket: unbalanced '${open}' at ${start}`);
}

function skipString(code: string, i: number): number {
  const verbatim = code[i - 1] === '@';
  let j = i + 1;
  while (j < code.length) {
    if (!verbatim && code[j] === '\\') {
      j += 2;
      continue;
    }
    if (code[j] === '"') return j;
    j++;
  }
  return j;
}

/** Splits on commas that are not nested inside <>, (), [] or {}. */
export function splitTopLevel(text: string, sep = ','): string[] {
  const parts: string[] = [];
  let depth = 0;
  let current = '';
  for (let i = 0; i < text.length; i++) {
    const ch = text[i] ?? '';
    if (ch === '"') {
      const end = skipString(text, i);
      current += text.slice(i, end + 1);
      i = end;
      continue;
    }
    if ('<([{'.includes(ch)) depth++;
    else if ('>)]}'.includes(ch)) depth--;
    if (ch === sep && depth === 0) {
      parts.push(current);
      current = '';
    } else current += ch;
  }
  if (current.trim()) parts.push(current);
  return parts.map((p) => p.trim()).filter(Boolean);
}

export function parseFile(path: string, source: string): CsFile {
  const code = stripComments(source);
  const ns = /^\s*namespace\s+([\w.]+)\s*[;{]/m.exec(code)?.[1] ?? '';
  const usings = [...code.matchAll(/^\s*using\s+(?:static\s+)?([\w.]+)\s*;/gm)].map((m) => m[1] ?? '');
  return { path, namespace: ns, usings, code };
}

/** Pulls `[JsonPropertyName("x")]` / `[JsonIgnore]` out of leading attributes. */
function takeAttributes(text: string): { rest: string; jsonName: string | null; ignore: boolean } {
  let rest = text.trim();
  let jsonName: string | null = null;
  let ignore = false;
  while (rest.startsWith('[')) {
    const end = matchBracket(rest, 0);
    const attr = rest.slice(1, end);
    const name = /JsonPropertyName\s*\(\s*"([^"]+)"\s*\)/.exec(attr)?.[1];
    if (name) jsonName = name;
    if (/\bJsonIgnore\b/.test(attr)) ignore = true;
    if (/\bJsonConverter\b/.test(attr)) {
      throw new Error(`[JsonConverter] on a member is not supported by the generator: [${attr}]`);
    }
    rest = rest.slice(end + 1).trim();
  }
  return { rest, jsonName, ignore };
}

function parsePositional(paramText: string): CsProperty[] {
  const props: CsProperty[] = [];
  for (const raw of splitTopLevel(paramText)) {
    const { rest, jsonName, ignore } = takeAttributes(raw);
    if (ignore) continue;
    const [decl, defaultValue] = splitTopLevel(rest, '=');
    const m = /^(.*\S)\s+(\w+)$/.exec((decl ?? '').trim());
    if (!m) throw new Error(`Cannot read record parameter '${raw}'`);
    props.push({
      type: (m[1] ?? '').trim(),
      name: m[2] ?? '',
      jsonName,
      hasDefault: defaultValue !== undefined,
      isRequired: false,
    });
  }
  return props;
}

const MODIFIERS = new Set(['public', 'required', 'virtual', 'override', 'new', 'sealed', 'readonly']);

/** Public instance properties of a class/record body (what System.Text.Json serializes). */
function parseBodyProperties(body: string): CsProperty[] {
  const props: CsProperty[] = [];
  let i = 0;
  let header = '';
  while (i < body.length) {
    const ch = body[i];
    if (ch === '"') {
      const end = skipString(body, i);
      header += body.slice(i, end + 1);
      i = end + 1;
      continue;
    }
    if (ch === '[' && header.trim() === '') {
      // attribute block before a member — keep it in the header.
      const end = matchBracket(body, i);
      header += body.slice(i, end + 1);
      i = end + 1;
      continue;
    }
    if (ch === '{') {
      const end = matchBracket(body, i);
      const block = body.slice(i + 1, end);
      // A '(' outside the attributes means a method/constructor, not a property.
      if (/\bget\b/.test(block) && !takeAttributes(header).rest.includes('(')) {
        const prop = readPropertyHeader(header);
        if (prop) props.push(prop);
        // Skip an initializer: `{ get; set; } = new();`
        let j = end + 1;
        while (j < body.length && /\s/.test(body[j] ?? '')) j++;
        if (body[j] === '=') j = body.indexOf(';', j) + 1;
        else j = end + 1;
        i = j;
      } else {
        i = end + 1; // method or nested type body
      }
      header = '';
      continue;
    }
    if (ch === ';') {
      // Expression-bodied property `public T X => …;` is serialized too.
      const arrow = header.indexOf('=>');
      if (arrow > 0 && !takeAttributes(header.slice(0, arrow)).rest.includes('(')) {
        const prop = readPropertyHeader(header.slice(0, arrow));
        if (prop) props.push(prop);
      }
      header = '';
      i++;
      continue;
    }
    header += ch;
    i++;
  }
  return props;
}

function readPropertyHeader(header: string): CsProperty | null {
  const { rest, jsonName, ignore } = takeAttributes(header);
  if (ignore) return null;
  const words = rest.split(/\s+/);
  if (!words.includes('public') || words.includes('static') || words.includes('const')) return null;
  const isRequired = words.includes('required');
  const declWords = words.filter((w) => !MODIFIERS.has(w));
  const decl = declWords.join(' ');
  const m = /^(.*\S)\s+(\w+)$/.exec(decl);
  if (!m) throw new Error(`Cannot read property '${header.trim()}'`);
  return { type: (m[1] ?? '').trim(), name: m[2] ?? '', jsonName, hasDefault: false, isRequired };
}

/** Every record/class/struct/enum/interface declared in the file. */
export function parseTypes(file: CsFile): CsType[] {
  const { code } = file;
  const types: CsType[] = [];
  const re = /\b(record|class|struct|enum|interface)\s+(?:(?:class|struct)\s+)?(\w+)\s*(<[^>{(;]*>)?/g;
  for (const m of code.matchAll(re)) {
    const keyword = m[1] as 'record' | 'class' | 'struct' | 'enum' | 'interface';
    const name = m[2] ?? '';
    const typeParams = m[3] ? splitTopLevel(m[3].slice(1, -1)) : [];
    let i = (m.index ?? 0) + m[0].length;
    const skipWs = (): void => {
      while (i < code.length && /\s/.test(code[i] ?? '')) i++;
    };
    skipWs();

    if (keyword === 'enum') {
      const open = code.indexOf('{', i);
      const close = matchBracket(code, open);
      let value = 0;
      const members = splitTopLevel(code.slice(open + 1, close)).map((raw) => {
        const [n, v] = raw.split('=').map((s) => s.trim());
        if (v !== undefined) value = Number(v);
        const member = { name: (n ?? '').replace(/^\[.*?\]\s*/, ''), value };
        value++;
        return member;
      });
      const before = code.slice(Math.max(0, (m.index ?? 0) - 300), m.index);
      types.push({
        kind: 'enum',
        name,
        members,
        asString: /JsonStringEnumConverter/.test(before.split(/[;}]/).pop() ?? ''),
        namespace: file.namespace,
        file: file.path,
      });
      continue;
    }

    let positional: CsProperty[] = [];
    if (code[i] === '(') {
      const close = matchBracket(code, i);
      positional = parsePositional(code.slice(i + 1, close));
      i = close + 1;
      skipWs();
    }
    let baseType: string | null = null;
    if (code[i] === ':') {
      const stop = code.slice(i + 1).search(/[{;]|\bwhere\b/);
      const bases = splitTopLevel(code.slice(i + 1, i + 1 + stop));
      // A base call such as `Base(x)` keeps only the type name.
      const first = (bases[0] ?? '').replace(/\(.*$/s, '').trim();
      baseType = first && !/^I[A-Z]/.test(first) ? first : null;
      i = i + 1 + stop;
    }
    const brace = code.indexOf('{', i);
    const semi = code.indexOf(';', i);
    let body = '';
    if (brace !== -1 && (semi === -1 || brace < semi)) {
      const close = matchBracket(code, brace);
      body = code.slice(brace + 1, close);
    }

    if (keyword === 'interface') {
      const methods: { name: string; returnType: string }[] = [];
      for (const member of body.split(';')) {
        const mm = /^\s*(?:\[[^\]]*\]\s*)*(.*?\S)\s+(\w+)\s*(?:<[^>]*>)?\s*\(/s.exec(member);
        if (mm) methods.push({ returnType: (mm[1] ?? '').trim(), name: mm[2] ?? '' });
      }
      types.push({ kind: 'interface', name, methods, namespace: file.namespace, file: file.path });
      continue;
    }

    types.push({
      kind: keyword,
      name,
      typeParams,
      baseType,
      properties: [
        ...positional,
        ...(keyword === 'record' || keyword === 'class' || keyword === 'struct'
          ? parseBodyProperties(body)
          : []),
      ],
      namespace: file.namespace,
      file: file.path,
    });
  }
  return types;
}

// ── Nancy modules ──

export interface CsRoute {
  method: 'get' | 'post' | 'patch' | 'put' | 'delete';
  /** Full path, e.g. `/api/v1/tenants/{tenantId}/programs/{programId}/tiers/{tierId}`. */
  path: string;
  /** The lambda body. */
  body: string;
}

export interface CsModule {
  name: string;
  file: string;
  /** Constructor parameters: variable name → C# type. */
  ctorParams: Map<string, string>;
  routes: CsRoute[];
}

export function parseModule(file: CsFile): CsModule | null {
  const cls = /\bclass\s+(\w+Module)\s*:\s*(\w+)/.exec(file.code);
  if (!cls) return null;
  const name = cls[1] ?? '';
  const ctor = new RegExp(`\\b${name}\\s*\\(`).exec(file.code.slice((cls.index ?? 0) + cls[0].length));
  const ctorParams = new Map<string, string>();
  let basePath = '';
  let ctorBodyStart = (cls.index ?? 0) + cls[0].length;
  if (ctor) {
    const open = ctorBodyStart + (ctor.index ?? 0) + ctor[0].length - 1;
    const close = matchBracket(file.code, open);
    for (const p of splitTopLevel(file.code.slice(open + 1, close))) {
      const pm = /^(.*\S)\s+(\w+)$/.exec(p);
      if (pm) ctorParams.set(pm[2] ?? '', (pm[1] ?? '').trim());
    }
    const after = file.code.slice(close + 1);
    basePath = /^\s*:\s*base\s*\(\s*"([^"]*)"/.exec(after)?.[1] ?? '';
    ctorBodyStart = close + 1;
  }

  const routes: CsRoute[] = [];
  const re =
    /(?<![\w.])(MapGet|MapPost|MapPatch|MapPut|MapDelete|Get|Post|Patch|Put|Delete)\s*\(\s*"([^"]*)"/g;
  re.lastIndex = ctorBodyStart;
  for (const m of file.code.matchAll(re)) {
    if ((m.index ?? 0) < ctorBodyStart) continue;
    const verb = (m[1] ?? '').replace(/^Map/, '').toLowerCase() as CsRoute['method'];
    const isMapped = (m[1] ?? '').startsWith('Map');
    const open = file.code.indexOf('(', m.index);
    const close = matchBracket(file.code, open);
    const rel = m[2] ?? '';
    routes.push({
      method: verb,
      path: isMapped ? basePath + rel : rel,
      body: file.code.slice(open, close + 1),
    });
  }
  return { name, file: file.path, ctorParams, routes };
}

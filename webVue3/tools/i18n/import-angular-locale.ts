/**
 * `npx tsx tools/i18n/import-angular-locale.ts <locale>` — converts the Angular portal's
 * `web/public/i18n/<locale>.json` into a vue-i18n message file, so both portals keep the same
 * keys (scope change 2026-10-12). Run once per locale; afterwards the Vue file is edited by hand.
 *
 * - Flat dotted keys (`programs.slug.label`) become nested objects.
 * - ngx-translate `{{name}}` placeholders become vue-i18n `{name}`.
 * - vue-i18n's special characters (`@ | { } $`) in plain text are escaped as `{'@'}` literals.
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { REPO_ROOT, WEB_ROOT } from '../openapi/paths';

type Messages = { [key: string]: string | Messages };

export function convertMessage(text: string): string {
  const parts = text.split(/(\{\{\s*\w+\s*\}\})/);
  return parts
    .map((part) => {
      const placeholder = /^\{\{\s*(\w+)\s*\}\}$/.exec(part);
      if (placeholder) return `{${placeholder[1]}}`;
      return part.replace(/[@|{}$]/g, (ch) => `{'${ch}'}`);
    })
    .join('');
}

export function nest(flat: Record<string, string>): Messages {
  const root: Messages = {};
  for (const [key, value] of Object.entries(flat)) {
    const segments = key.split('.');
    let node = root;
    segments.forEach((segment, i) => {
      const last = i === segments.length - 1;
      const existing = node[segment];
      if (last) {
        if (existing !== undefined) throw new Error(`Key '${key}' is also a prefix of other keys`);
        node[segment] = convertMessage(value);
      } else {
        if (typeof existing === 'string')
          throw new Error(`Key '${segments.slice(0, i + 1).join('.')}' is both a message and a prefix`);
        node = (node[segment] ??= {}) as Messages;
      }
    });
  }
  return root;
}

if (process.argv[1]?.endsWith('import-angular-locale.ts')) {
  const locale = process.argv[2] ?? 'en';
  const flat = JSON.parse(
    readFileSync(resolve(REPO_ROOT, `web/public/i18n/${locale}.json`), 'utf8'),
  ) as Record<string, string>;
  const out = resolve(WEB_ROOT, `src/shared/i18n/locales/${locale}.json`);
  writeFileSync(out, JSON.stringify(nest(flat), null, 2) + '\n');
  console.log(`${Object.keys(flat).length} messages → ${out}`);
}

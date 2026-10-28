import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import tr from './locales/tr.json';

type Messages = { [key: string]: string | Messages };

/** Every leaf key as a dotted path, with its message. */
function flatten(messages: Messages, prefix = ''): Map<string, string> {
  const out = new Map<string, string>();
  for (const [key, value] of Object.entries(messages)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (typeof value === 'string') out.set(path, value);
    else for (const [k, v] of flatten(value, path)) out.set(k, v);
  }
  return out;
}

/** `{name}` placeholders, ignoring vue-i18n literal escapes like `{'{'}`. */
const placeholders = (message: string): string[] =>
  [...message.matchAll(/\{(\w+)\}/g)].map((m) => m[1] ?? '').sort();

describe('locale files', () => {
  const english = flatten(en);
  const turkish = flatten(tr);

  it('Turkish has exactly the English keys', () => {
    expect([...turkish.keys()].filter((k) => !english.has(k))).toEqual([]);
    expect([...english.keys()].filter((k) => !turkish.has(k))).toEqual([]);
  });

  it('Turkish keeps every placeholder of each English message', () => {
    const mismatched = [...english].filter(
      ([key, message]) => placeholders(message).join() !== placeholders(turkish.get(key) ?? '').join(),
    );
    expect(mismatched.map(([key]) => key)).toEqual([]);
  });

  it('no Turkish message is left empty', () => {
    expect([...turkish].filter(([, message]) => message.trim() === '').map(([key]) => key)).toEqual([]);
  });
});

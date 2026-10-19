/**
 * A deep copy of JSON data loaded from the API, for a form to edit without touching the cached
 * copy. Not `structuredClone`: Vue Query hands its data out as reactive proxies, which
 * `structuredClone` refuses (DataCloneError).
 */
export function cloneJson<T>(value: T): T {
  return JSON.parse(JSON.stringify(value)) as T;
}

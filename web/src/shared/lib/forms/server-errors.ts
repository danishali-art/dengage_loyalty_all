import { ApiError } from '../../api/api-error';

export interface MappedServerErrors {
  /** Field path (VeeValidate dot notation) → first message, for `setErrors`. */
  fields: Record<string, string>;
  /** Messages that matched no field, for a form-level summary. */
  form: string[];
}

/**
 * For VeeValidate: splits an `ApiError`'s
 * `fieldErrors` (`{ "conditions[2].value": ["…"] }`) into the ones a form has a field for and the
 * rest. Server field names are camelCase (the API's JSON policy), the same as form field names.
 */
export function mapServerErrors(
  error: unknown,
  knownFields: readonly string[],
  fallback: string,
): MappedServerErrors {
  if (!(error instanceof ApiError)) return { fields: {}, form: [fallback] };
  const fields: Record<string, string> = {};
  const form: string[] = [];
  for (const [rawPath, messages] of Object.entries(error.fieldErrors)) {
    const path = rawPath.replace(/\[(\d+)\]/g, '.$1');
    const root = path.split('.')[0] ?? path;
    const field = knownFields.find((f) => f.toLowerCase() === root.toLowerCase());
    if (field && messages[0]) fields[field === root ? path : field] = messages[0];
    else form.push(...messages);
  }
  // A failure with no field map at all still deserves a summary line.
  if (form.length === 0 && Object.keys(fields).length === 0) form.push(error.message);
  return { fields, form };
}

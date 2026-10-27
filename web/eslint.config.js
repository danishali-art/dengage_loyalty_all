// @ts-check
/**
 * Lint enforces the architecture (.claude/rules/frontend.md):
 * Feature-Sliced layers and slice boundaries, the UI kit only behind shared/ui, HTTP only in
 * shared/api, thin presentational components, and no bare UI strings.
 */
import js from '@eslint/js';
import tseslint from 'typescript-eslint';
import pluginVue from 'eslint-plugin-vue';
import boundaries from 'eslint-plugin-boundaries';
import prettier from 'eslint-config-prettier';

const LAYERS = ['app', 'pages', 'widgets', 'features', 'entities', 'shared'];

const SLICED = new Set(['pages', 'widgets', 'features', 'entities']);

/**
 * A slice may import anything inside itself, and from layers below its own — but another slice
 * only through its public `index.ts`.
 */
function layerPolicy(layer, below, sameSlice) {
  return {
    from: { element: { type: layer } },
    allow: {
      to: [
        ...below.map((type) => ({
          element: SLICED.has(type) ? { type, fileInternalPath: 'index.ts' } : { type },
        })),
        ...(sameSlice
          ? [{ element: { type: layer, captured: { slice: '{{ from.element.captured.slice }}' } } }]
          : []),
      ],
    },
  };
}

const restricted = (patterns) => ['error', { patterns }];

const UI_KIT = {
  group: [
    'reka-ui',
    'reka-ui/*',
    'vue-sonner',
    '@lucide/vue',
    'class-variance-authority',
    'clsx',
    'tailwind-merge',
    '@/shared/ui/primitives',
    '@/shared/ui/primitives/*',
    '@/shared/ui/lib',
    '@/shared/ui/lib/*',
  ],
  message:
    'The UI kit (shadcn-vue primitives, Reka UI, icons) is only used inside src/shared/ui — use an App* wrapper.',
};
const HTTP = {
  group: ['axios', 'openapi-fetch'],
  message: 'HTTP lives in src/shared/api only — call an entity/feature api function instead.',
};
const SERVER_STATE = {
  group: ['@tanstack/vue-query', 'pinia', '@/shared/model', '@/shared/model/*', '**/api', '**/api/*'],
  message: 'Presentational components take props and emit events — no queries, stores or API calls.',
};

export default tseslint.config(
  { ignores: ['dist/**', 'coverage/**', 'node_modules/**', 'src/shared/api/generated/**'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...pluginVue.configs['flat/recommended'],
  {
    files: ['**/*.vue'],
    languageOptions: { parserOptions: { parser: tseslint.parser, extraFileExtensions: ['.vue'] } },
  },
  {
    languageOptions: { globals: { console: 'readonly', process: 'readonly' } },
    rules: {
      '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_', varsIgnorePattern: '^_' }],
      'vue/multi-word-component-names': 'off',
      // TypeScript already resolves identifiers (DOM types included); typescript-eslint advises off.
      'no-undef': 'off',
      'vue/no-bare-strings-in-template': [
        'error',
        { allowlist: ['L·', '›', '→', '▾', '…', '·', '—', '(', ')', ':', '-', '/', '%', '#', '.', '+'] },
      ],
    },
  },

  {
    // shadcn-vue generated code, kept as generated so `shadcn-vue add` can refresh it. Its built-in
    // English close labels are never rendered: the App* wrappers turn them off and supply
    // translated ones.
    files: ['src/shared/ui/primitives/**'],
    rules: { 'vue/require-default-prop': 'off', 'vue/no-bare-strings-in-template': 'off' },
  },

  // ── Feature-Sliced Design boundaries ──
  {
    files: ['src/**/*.{ts,vue}'],
    // Specs may reach into a slice to mock its api seam; they are not part of the architecture.
    ignores: ['src/**/*.spec.ts'],
    plugins: { boundaries },
    settings: {
      'import/resolver': { typescript: { project: './tsconfig.app.json' } },
      'boundaries/include': ['src/**/*'],
      'boundaries/elements': LAYERS.map((type) =>
        type === 'app' || type === 'shared'
          ? { type, pattern: `src/${type}` }
          : { type, pattern: `src/${type}/*`, capture: ['slice'] },
      ),
    },
    rules: {
      'boundaries/dependencies': [
        'error',
        {
          default: 'disallow',
          policies: [
            layerPolicy('app', ['app', 'pages', 'widgets', 'features', 'entities', 'shared'], false),
            layerPolicy('pages', ['widgets', 'features', 'entities', 'shared'], true),
            layerPolicy('widgets', ['features', 'entities', 'shared'], true),
            layerPolicy('features', ['entities', 'shared'], true),
            layerPolicy('entities', ['shared'], true),
            layerPolicy('shared', ['shared'], false),
          ],
        },
      ],
    },
  },

  // ── Library and responsibility boundaries ──
  {
    files: ['src/**/*.{ts,vue}'],
    ignores: ['src/shared/ui/**', 'src/shared/api/**'],
    rules: { 'no-restricted-imports': restricted([UI_KIT, HTTP]) },
  },
  {
    files: ['src/shared/api/**'],
    rules: { 'no-restricted-imports': restricted([UI_KIT]) },
  },
  {
    // fetch() only in the API client — and app-config.json's one bootstrap read.
    files: ['src/**/*.{ts,vue}'],
    ignores: ['src/shared/api/**', 'src/shared/config/app-config.ts', 'src/**/*.spec.ts'],
    rules: {
      'no-restricted-globals': ['error', { name: 'fetch', message: 'HTTP lives in src/shared/api only.' }],
      'no-restricted-syntax': [
        'error',
        {
          selector: "CallExpression[callee.object.name='window'][callee.property.name='fetch']",
          message: 'HTTP lives in src/shared/api only.',
        },
      ],
    },
  },
  {
    files: ['src/entities/*/ui/**', 'src/features/*/ui/**'],
    rules: { 'no-restricted-imports': restricted([UI_KIT, HTTP, SERVER_STATE]) },
  },
  {
    files: ['src/shared/ui/**'],
    rules: { 'no-restricted-imports': restricted([HTTP, SERVER_STATE]) },
  },
  {
    // Stores hold client state; they never fetch (server data belongs to Vue Query).
    files: ['src/**/*.store.ts'],
    rules: {
      'no-restricted-imports': restricted([
        UI_KIT,
        HTTP,
        {
          group: ['@/shared/api', '@/shared/api/*', '**/api', '**/api/*', '@tanstack/vue-query'],
          message: 'Stores never call the API — use a query in an entity/feature.',
        },
      ]),
    },
  },
  {
    files: ['tools/**/*.ts', 'e2e/**/*.ts', '*.config.{ts,js}'],
    languageOptions: { globals: { process: 'readonly', console: 'readonly' } },
  },
  prettier,
);

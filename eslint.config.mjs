// ESLint config for the TypeScript workspace (src/typescript/*, samples/typescript/*).
import js from '@eslint/js';
import tseslint from 'typescript-eslint';

const pkgSrc = 'src/typescript/*/src';

export default tseslint.config(
  {
    ignores: [
      '**/dist/**',
      '**/node_modules/**',
      '**/coverage/**',
      'src/dotnet/**',
      'tests/**',
      'samples/dotnet/**',
      'artifacts/**',
      'specs/**',
      'docs/**',
      '.superpowers/**',
    ],
  },
  js.configs.recommended,
  ...tseslint.configs.strict,
  {
    files: ['**/scripts/*.cjs'],
    languageOptions: {
      sourceType: 'commonjs',
      globals: { require: 'readonly', process: 'readonly', console: 'readonly' },
    },
    rules: { '@typescript-eslint/no-require-imports': 'off' },
  },
  {
    files: ['**/scripts/*.mjs'],
    languageOptions: { globals: { process: 'readonly', console: 'readonly' } },
  },
  // Constitution v1.2.0, Principle I — layer boundaries for TypeScript packages.
  {
    files: [`${pkgSrc}/domain/**/*.ts`],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/application/**', '**/infrastructure/**'],
              message: 'domain/ must not import application/ or infrastructure/.',
            },
            { regex: '^node:', message: 'domain/ must not import Node built-ins.' },
          ],
        },
      ],
    },
  },
  {
    files: [`${pkgSrc}/application/**/*.ts`],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['**/infrastructure/**'],
              message: 'application/ must not import infrastructure/.',
            },
            { regex: '^node:', message: 'application/ must not import Node built-ins.' },
          ],
        },
      ],
    },
  },
);

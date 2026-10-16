// ESLint flat config. Security rules implement NFR-SEC-9 "Code rules":
// no dangerouslySetInnerHTML, eval or new Function; no raw HTML sinks; no
// server rendering APIs. Any future HTML rendering must go through a single
// reviewed Trusted Types policy module, which will be the only file exempted.
import js from '@eslint/js';
import tseslint from 'typescript-eslint';

const htmlSinkMessage =
  'Raw HTML sinks are banned (NFR-SEC-9). Render with React, or route through the reviewed Trusted Types policy.';

export default tseslint.config(
  { ignores: ['dist/', 'node_modules/', 'playwright-report/', 'test-results/'] },
  js.configs.recommended,
  ...tseslint.configs.strict,
  {
    files: ['**/*.{ts,tsx,js,mjs}'],
    rules: {
      'no-eval': 'error',
      'no-implied-eval': 'error',
      'no-new-func': 'error',
      'no-script-url': 'error',
      'no-restricted-syntax': [
        'error',
        {
          selector: "JSXAttribute[name.name='dangerouslySetInnerHTML']",
          message: 'dangerouslySetInnerHTML is banned (NFR-SEC-9).',
        },
        {
          selector: 'AssignmentExpression > MemberExpression.left[property.name=/^(innerHTML|outerHTML)$/]',
          message: htmlSinkMessage,
        },
        {
          selector:
            'CallExpression[callee.property.name=/^(insertAdjacentHTML|createContextualFragment|setHTMLUnsafe)$/]',
          message: htmlSinkMessage,
        },
      ],
      'no-restricted-properties': [
        'error',
        { object: 'document', property: 'write', message: htmlSinkMessage },
        { object: 'document', property: 'writeln', message: htmlSinkMessage },
      ],
      'no-restricted-imports': [
        'error',
        {
          paths: [
            { name: 'react-dom/server', message: 'Server rendering is banned (D1).' },
            { name: 'react-dom/static', message: 'Server rendering is banned (D1).' },
          ],
          patterns: [
            { group: ['react-dom/server.*', 'react-server-dom-*'], message: 'Server rendering is banned (D1).' },
          ],
        },
      ],
    },
  },
  {
    files: ['scripts/**/*.mjs', 'tests/**/*.mjs'],
    languageOptions: {
      globals: { process: 'readonly', console: 'readonly' },
    },
  },
);

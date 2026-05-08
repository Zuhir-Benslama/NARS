// @ts-check
import pluginVue from 'eslint-plugin-vue'
import vueParser from 'vue-eslint-parser'
import tsParser from '@typescript-eslint/parser'
import tsPlugin from '@typescript-eslint/eslint-plugin'
import prettier from 'eslint-config-prettier'

export default [
    ...pluginVue.configs['flat/recommended'],
    {
        files: ['**/*.ts'],
        languageOptions: {
            parser: tsParser,
            parserOptions: {
                ecmaVersion: 2020,
                sourceType: 'module',
            },
        },
        plugins: {
            '@typescript-eslint': tsPlugin,
        },
        rules: {
            // TypeScript
            '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
            '@typescript-eslint/no-explicit-any': 'error',
            '@typescript-eslint/explicit-function-return-type': 'off',

            // General
            'no-console': ['warn', { allow: ['warn', 'error'] }],
            'no-debugger': 'error',
            'prefer-const': 'error',
            'eqeqeq': ['error', 'always', { null: 'ignore' }],
        },
    },
    {
        files: ['**/*.vue'],
        languageOptions: {
            parser: vueParser,
            parserOptions: {
                parser: tsParser,
                ecmaVersion: 2020,
                sourceType: 'module',
            },
        },
        plugins: {
            '@typescript-eslint': tsPlugin,
        },
        rules: {
            // TypeScript
            '@typescript-eslint/no-unused-vars': ['error', { argsIgnorePattern: '^_' }],
            '@typescript-eslint/no-explicit-any': 'error',
            '@typescript-eslint/explicit-function-return-type': 'off',

            // Vue
            'vue/multi-word-component-names': 'off',
            'vue/no-v-html': 'error',
            'vue/component-name-in-template-casing': ['error', 'PascalCase'],
            'vue/html-self-closing': ['error', { html: { void: 'always' } }],

            // General
            'no-console': ['warn', { allow: ['warn', 'error'] }],
            'no-debugger': 'error',
            'prefer-const': 'error',
            'eqeqeq': ['error', 'always', { null: 'ignore' }],
        },
    },
    // Prettier must be last — disables all formatting rules that conflict
    prettier,
    {
        ignores: ['dist/', 'node_modules/', 'src/test/', '**/*.test.ts', '**/*.spec.ts'],
    },
]

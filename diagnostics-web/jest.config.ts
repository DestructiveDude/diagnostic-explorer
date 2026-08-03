import type {Config} from 'jest';

const config: Config = {
    preset: 'jest-preset-angular',
    setupFilesAfterEnv: ['<rootDir>/setup-jest.ts'],
    testEnvironment: 'jsdom',
    // The worktree lives under a `.claude/` dot-directory; the glob-based
    // testMatch `**` skips dot-directory segments, so match by regex instead.
    roots: ['<rootDir>/src'],
    testRegex: '\\.spec\\.ts$',
    transform: {
        '^.+\\.(ts|mjs|js|html)$': [
            'jest-preset-angular',
            {
                tsconfig: '<rootDir>/tsconfig.spec.json',
                stringifyContentPathRegex: '\\.(html|svg)$',
            },
        ],
    },
    transformIgnorePatterns: ['node_modules/(?!.*\\.mjs$|@noble/)'],
    collectCoverageFrom: [
        'src/**/*.ts',
        '!src/**/*.spec.ts',
        '!src/main.ts',
        '!src/test.ts',
        '!src/environments/**',
    ],
    coverageDirectory: '<rootDir>/coverage/jest',
    moduleFileExtensions: ['ts', 'html', 'js', 'json', 'mjs'],
};

export default config;

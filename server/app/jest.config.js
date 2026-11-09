/** Jest configuration: unit tests plus the client-flow e2e suite. */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  roots: ['<rootDir>/test'],
  testMatch: ['**/*.spec.ts'],
  moduleFileExtensions: ['ts', 'js', 'json'],
  // The e2e suite talks to the docker PostgreSQL and runs migrations.
  testTimeout: 60000,
  clearMocks: true,
};

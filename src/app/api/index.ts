/**
 * API layer.
 *
 * Everything under `./generated` is produced by ng-openapi-gen from `openapi/AzureAdmin.API.json`,
 * which the backend writes on every build (see `AzureAdmin.API.csproj`). Do not edit generated files;
 * change the controllers or contracts, rebuild the backend, and commit the regenerated output.
 *
 * Components inject the generated `*Api` services (one per controller) and use the generated model
 * types. Hand-written code in this folder is limited to helpers such as error formatting.
 */
export * from './generated/models';
// ng-openapi-gen keeps the enum value arrays out of its models barrel; expose them for parsing user input.
export { RELEASE_LIFECYCLE_STATUS } from './generated/models/release-lifecycle-status-array';
export { RELEASE_PR_PHASE } from './generated/models/release-pr-phase-array';
export { RELEASE_PULL_REQUEST_STATUS } from './generated/models/release-pull-request-status-array';
export { WEBHOOK_DESTINATION_KIND } from './generated/models/webhook-destination-kind-array';
export * from './generated/services';
export { ApiConfiguration, provideApiConfiguration } from './generated/api-configuration';
export { apiErrorMessage, apiErrorStatus } from './api-error';

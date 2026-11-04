// The envelope model lives in application/ (the error mappers need it); infrastructure re-exports it.
export {
  parseResponseError,
  renderValidationFailures,
  type ResponseError,
  type ValidationFailure,
} from '../../application/errors/response-error.js';

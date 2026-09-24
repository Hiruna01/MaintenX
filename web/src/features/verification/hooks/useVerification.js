import useFetch from '../../../hooks/useFetch';
import { buildVerificationPath } from '../services/verificationApi';

/** One check from GET /api/verifications/{id}. */
export function useVerification(id) {
  return useFetch(buildVerificationPath(id));
}

export default useVerification;

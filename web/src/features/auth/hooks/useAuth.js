import { createContext, useContext } from 'react';

/**
 * App-wide auth/session state. Context — not Redux, not Zustand, not TanStack Query;
 * that is a locked ADR decision. Local UI state stays in useState.
 *
 * The context object lives here rather than in AuthProvider.jsx so that this hook and the
 * provider component can both import it without a circular import.
 */
export const AuthContext = createContext(null);

export function useAuth() {
  const context = useContext(AuthContext);

  if (context === null) {
    throw new Error('useAuth must be used inside <AuthProvider>.');
  }

  return context;
}

export default useAuth;

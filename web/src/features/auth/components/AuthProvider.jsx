import { useCallback, useEffect, useMemo, useState } from 'react';

import { getToken, subscribe } from '../../../services/tokenStore';
import { AuthContext } from '../hooks/useAuth';
import * as authService from '../services/authService';

/**
 * Owns the signed-in user for the whole app.
 *
 * The token itself lives in tokenStore (memory + localStorage) because useFetch and the
 * feature services need it and cannot call a hook. This provider subscribes to that store,
 * so when a 401 expires the token mid-request the user is cleared here too and the route
 * guards redirect — no component has to remember to handle it.
 */
export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  // True until the "is there a token in localStorage, and is it still good?" check is done.
  // Route guards wait for this; otherwise a reload would bounce a signed-in user to /login.
  const [isRestoring, setIsRestoring] = useState(getToken() !== null);
  const [sessionExpired, setSessionExpired] = useState(false);

  useEffect(() => {
    // No token means nothing to restore, and `isRestoring` already started false.
    if (getToken() === null) {
      return undefined;
    }

    let active = true;

    authService
      .fetchCurrentUser()
      .then((me) => {
        if (!active) return;
        setUser(me);
      })
      .catch(() => {
        // A 401 has already cleared the token via the store; anything else leaves the user
        // signed out too, because we could not confirm who they are.
        if (!active) return;
        setUser(null);
      })
      .finally(() => {
        if (!active) return;
        setIsRestoring(false);
      });

    return () => {
      active = false;
    };
  }, []);

  useEffect(
    () =>
      subscribe((token) => {
        if (token === null) {
          setUser((current) => {
            // Only an *unexpected* loss of the token is a session expiry. A deliberate
            // logout clears `user` itself, so `current` is already null by then.
            if (current !== null) setSessionExpired(true);
            return null;
          });
        }
      }),
    [],
  );

  const login = useCallback(async (email, password) => {
    const signedIn = await authService.login(email, password);
    setSessionExpired(false);
    setUser(signedIn);
    return signedIn;
  }, []);

  const logout = useCallback(() => {
    setUser(null);
    setSessionExpired(false);
    authService.logout();
  }, []);

  const clearSessionExpired = useCallback(() => setSessionExpired(false), []);

  const value = useMemo(
    () => ({
      user,
      role: user?.role ?? null,
      isAuthenticated: user !== null,
      isRestoring,
      sessionExpired,
      login,
      logout,
      clearSessionExpired,
    }),
    [user, isRestoring, sessionExpired, login, logout, clearSessionExpired],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export default AuthProvider;

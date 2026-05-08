import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from 'react';
import { api, auth } from '../api';

type AuthState =
  | { status: 'loading' }
  | { status: 'unauthenticated' }
  | { status: 'authenticated'; username: string };

interface AuthContextValue {
  state: AuthState;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<AuthState>({ status: 'loading' });

  const handleUnauthorized = useCallback(() => {
    setState({ status: 'unauthenticated' });
  }, []);

  useEffect(() => {
    auth.onUnauthorized(handleUnauthorized);
    return () => auth.onUnauthorized(null);
  }, [handleUnauthorized]);

  useEffect(() => {
    const token = auth.getToken();
    if (!token) {
      setState({ status: 'unauthenticated' });
      return;
    }
    api.me()
      .then((res) => setState({ status: 'authenticated', username: res.username }))
      .catch(() => {
        auth.setToken(null);
        setState({ status: 'unauthenticated' });
      });
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.login(username, password);
    auth.setToken(res.token);
    setState({ status: 'authenticated', username: res.username });
  }, []);

  const logout = useCallback(async () => {
    try { await api.logout(); } catch { /* ignore */ }
    auth.setToken(null);
    setState({ status: 'unauthenticated' });
  }, []);

  return (
    <AuthContext.Provider value={{ state, login, logout }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

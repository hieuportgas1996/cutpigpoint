import { createContext, ReactNode, useCallback, useContext, useEffect, useState } from 'react';
import { api, auth } from '../api';

type AuthState =
  | { status: 'loading' }
  | { status: 'unauthenticated' }
  | { status: 'authenticated'; userId: string; username: string; displayName: string; isAdmin: boolean; hasAvatar: boolean; avatarVersion: number };

interface AuthContextValue {
  state: AuthState;
  login: (username: string, password: string) => Promise<void>;
  logout: () => Promise<void>;
  refreshAvatar: (hasAvatar: boolean) => void;
  updateDisplayName: (displayName: string) => void;
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
      .then((res) => setState({
        status: 'authenticated',
        userId: res.userId,
        username: res.username,
        displayName: res.displayName,
        isAdmin: res.isAdmin,
        hasAvatar: res.hasAvatar,
        avatarVersion: Date.now()
      }))
      .catch(() => {
        auth.setToken(null);
        setState({ status: 'unauthenticated' });
      });
  }, []);

  const login = useCallback(async (username: string, password: string) => {
    const res = await api.login(username, password);
    auth.setToken(res.token);
    setState({
      status: 'authenticated',
      userId: res.userId,
      username: res.username,
      displayName: res.displayName,
      isAdmin: res.isAdmin,
      hasAvatar: res.hasAvatar,
      avatarVersion: Date.now()
    });
  }, []);

  const refreshAvatar = useCallback((hasAvatar: boolean) => {
    setState(prev => prev.status === 'authenticated'
      ? { ...prev, hasAvatar, avatarVersion: Date.now() }
      : prev);
  }, []);

  const updateDisplayName = useCallback((displayName: string) => {
    setState(prev => prev.status === 'authenticated'
      ? { ...prev, displayName }
      : prev);
  }, []);

  const logout = useCallback(async () => {
    try { await api.logout(); } catch { /* ignore */ }
    auth.setToken(null);
    setState({ status: 'unauthenticated' });
  }, []);

  return (
    <AuthContext.Provider value={{ state, login, logout, refreshAvatar, updateDisplayName }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth must be used within AuthProvider');
  return ctx;
}

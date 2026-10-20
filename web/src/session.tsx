import { createContext, useCallback, useContext, useEffect, useMemo, useRef, useState, type ReactNode } from 'react';

import { api, ApiError, onSessionEnded, type SessionUser } from './api';
import { navigate } from './router';

export type SetupStatus = 'required' | 'complete' | 'unknown';

/** Why the sign-in page shows a note: the session ended on its own, or the person signed out. */
export type SignInNotice = 'expired' | 'signed-out';

interface SessionValue {
  loading: boolean;
  failure: ApiError | null;
  setup: SetupStatus;
  user: SessionUser | null;
  notice: SignInNotice | null;
  reload: () => Promise<void>;
  /** After a sign-in, setup or link flow finished: the server has started a session. */
  signedIn: (user: SessionUser) => void;
  signOut: () => Promise<void>;
  clearNotice: () => void;
}

const SessionContext = createContext<SessionValue | null>(null);

export function useSession(): SessionValue {
  const value = useContext(SessionContext);
  if (!value) {
    throw new Error('useSession outside SessionProvider');
  }
  return value;
}

export function isAdmin(user: SessionUser | null): boolean {
  return user?.roles.includes('Admin') ?? false;
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const [loading, setLoading] = useState(true);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [setup, setSetup] = useState<SetupStatus>('unknown');
  const [user, setUser] = useState<SessionUser | null>(null);
  const [notice, setNotice] = useState<SignInNotice | null>(null);

  const reload = useCallback(async () => {
    setLoading(true);
    try {
      const { data } = await api<{ setup: SetupStatus; user: SessionUser | null }>(
        'GET',
        '/api/v1/auth/session',
        undefined,
        {
          signedIn: false,
        },
      );
      setSetup(data.setup);
      setUser(data.user);
      setFailure(null);
    } catch (error) {
      setFailure(error instanceof ApiError ? error : new ApiError(0, 'network', 'Couldn’t reach Goldmark.'));
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  // A signed-in request answered 401: the session ended (30 minutes idle, 12 hours, or revoked).
  const signedInRef = useRef(false);
  useEffect(() => {
    signedInRef.current = user !== null;
  }, [user]);
  useEffect(
    () =>
      onSessionEnded(() => {
        if (!signedInRef.current) {
          return;
        }
        signedInRef.current = false;
        setUser(null);
        setNotice('expired');
        const here = window.location.pathname + window.location.search;
        navigate(`/sign-in?next=${encodeURIComponent(here)}`, { replace: true });
      }),
    [],
  );

  const value = useMemo<SessionValue>(
    () => ({
      loading,
      failure,
      setup,
      user,
      notice,
      reload,
      signedIn: (next) => {
        setUser(next);
        setSetup('complete');
        setNotice(null);
      },
      signOut: async () => {
        try {
          await api('POST', '/api/v1/auth/sign-out', {}, { signedIn: false });
        } finally {
          setUser(null);
          setNotice('signed-out');
          navigate('/sign-in', { replace: true });
        }
      },
      clearNotice: () => setNotice(null),
    }),
    [loading, failure, setup, user, notice, reload],
  );

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>;
}

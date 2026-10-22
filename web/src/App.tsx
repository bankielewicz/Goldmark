import { Spinner, makeStyles, tokens } from '@fluentui/react-components';
import { useEffect } from 'react';

import { AuditPage } from './pages/AuditPage';
import { EnrolmentPage } from './pages/EnrolmentPage';
import { DashboardPage, ForbiddenPage, NotFoundPage } from './pages/SimplePages';
import { SignInPage } from './pages/SignInPage';
import { TwoFactorPage } from './pages/TwoFactorPage';
import { UsersPage } from './pages/UsersPage';
import { navigate, safeReturnPath, useLocation } from './router';
import { canReadAudit, isAdmin, useSession } from './session';
import { AppShell } from './shell/AppShell';
import { ErrorBar, UnauthenticatedLayout } from './ui/common';

const useStyles = makeStyles({
  loading: {
    minHeight: '100vh',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    backgroundColor: tokens.colorNeutralBackground3,
  },
});

function Redirect({ to }: { to: string }) {
  useEffect(() => navigate(to, { replace: true }), [to]);
  return null;
}

/**
 * The routes (DEC-040: an in-repo router). Unauthenticated: /sign-in, /setup, /invite, /reset and
 * /password-reset (their token in the URL fragment). Signed in, inside the app shell: /, /users (Admin only), /audit
 * (Admin and Auditor) and /account/two-factor. Anything else signed in is "not found"; signed out, it leads to sign-in and
 * back afterwards.
 */
export function App() {
  const styles = useStyles();
  const session = useSession();
  const { pathname, search } = useLocation();

  if (pathname === '/setup') return <EnrolmentPage route="setup" />;
  if (pathname === '/invite' || pathname === '/reset' || pathname === '/password-reset') {
    return <EnrolmentPage route="link" />;
  }

  if (session.loading) {
    return (
      <div className={styles.loading}>
        <Spinner label="Loading Goldmark…" />
      </div>
    );
  }

  if (session.failure && !session.user) {
    return (
      <UnauthenticatedLayout>
        <ErrorBar error={session.failure} onRetry={() => void session.reload()} />
      </UnauthenticatedLayout>
    );
  }

  if (pathname === '/sign-in') {
    return session.user ? <Redirect to={safeReturnPath(search.get('next'))} /> : <SignInPage />;
  }

  if (!session.user) {
    const next =
      pathname === '/'
        ? ''
        : `?next=${encodeURIComponent(pathname + (search.size > 0 ? `?${search.toString()}` : ''))}`;
    return <Redirect to={`/sign-in${next}`} />;
  }

  let page;
  if (pathname === '/') {
    page = <DashboardPage />;
  } else if (pathname === '/users') {
    page = isAdmin(session.user) ? (
      <UsersPage />
    ) : (
      <ForbiddenPage title="Users & roles" crumbs={[{ label: 'Administration' }, { label: 'Users & roles' }]} />
    );
  } else if (pathname === '/audit') {
    page = canReadAudit(session.user) ? (
      <AuditPage />
    ) : (
      <ForbiddenPage title="the audit log" crumbs={[{ label: 'Administration' }, { label: 'Audit log' }]} />
    );
  } else if (pathname === '/account/two-factor') {
    page = <TwoFactorPage />;
  } else {
    page = <NotFoundPage />;
  }
  return <AppShell>{page}</AppShell>;
}

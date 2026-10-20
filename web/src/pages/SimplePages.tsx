import { Card, Link, Text, makeStyles, tokens } from '@fluentui/react-components';

import { followLink } from '../router';
import { isAdmin, useSession } from '../session';
import { PageHeader } from '../shell/PageHeader';

const useStyles = makeStyles({
  card: { maxWidth: '720px', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
});

/** The dashboard is screen 3 (later slices); slice 3 shows where to go next. */
export function DashboardPage() {
  const styles = useStyles();
  const { user } = useSession();
  return (
    <>
      <PageHeader crumbs={[{ label: 'Overview' }, { label: 'Dashboard' }]} title="Dashboard" />
      <Card appearance="outline" className={styles.card}>
        <Text weight="semibold" size={400}>
          Welcome, {user?.displayName}
        </Text>
        <Text>Goldmark is running. Configuration items, sources and impact analysis arrive in the next releases.</Text>
        {isAdmin(user) ? (
          <Text>
            Add the people who need access on{' '}
            <Link href="/users" onClick={(event) => followLink(event, '/users')}>
              Users &amp; roles
            </Link>
            .
          </Text>
        ) : null}
      </Card>
    </>
  );
}

/** The 403 state: a signed-in person without the role a page needs. */
export function ForbiddenPage({ title, crumbs }: { title: string; crumbs: { label: string }[] }) {
  const styles = useStyles();
  const { user } = useSession();
  return (
    <>
      <PageHeader crumbs={crumbs} title="You don’t have access to this page" />
      <Card appearance="outline" className={styles.card}>
        <Text>
          Your {user && user.roles.length === 1 ? 'role' : 'roles'}, {user?.roles.join(', ') || 'none'}, can’t open{' '}
          {title}. An administrator can change your role.
        </Text>
        <Link href="/" onClick={(event) => followLink(event, '/')}>
          Go to the dashboard
        </Link>
      </Card>
    </>
  );
}

export function NotFoundPage() {
  const styles = useStyles();
  return (
    <>
      <PageHeader crumbs={[{ label: 'Goldmark' }]} title="Page not found" />
      <Card appearance="outline" className={styles.card}>
        <Text>There’s no page at this address.</Text>
        <Link href="/" onClick={(event) => followLink(event, '/')}>
          Go to the dashboard
        </Link>
      </Card>
    </>
  );
}

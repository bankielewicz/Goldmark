import {
  Avatar,
  Button,
  CounterBadge,
  Menu,
  MenuDivider,
  MenuGroup,
  MenuGroupHeader,
  MenuItem,
  MenuItemCheckbox,
  MenuList,
  MenuPopover,
  MenuTrigger,
  NavDrawer,
  NavDrawerBody,
  NavItem,
  NavSectionHeader,
  Popover,
  PopoverSurface,
  PopoverTrigger,
  SearchBox,
  Text,
  Toast,
  ToastTitle,
  Toaster,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
  useId,
  useToastController,
} from '@fluentui/react-components';
import {
  BoardRegular,
  ChevronDownRegular,
  DocumentBulletListClockRegular,
  FlowchartRegular,
  KeyRegular,
  NavigationRegular,
  PeopleRegular,
  SearchRegular,
  ShapesRegular,
  ShieldCheckmarkRegular,
  SignOutRegular,
  WeatherMoonRegular,
  WeatherSunnyRegular,
} from '@fluentui/react-icons';
import { useEffect, useState, type MouseEvent, type ReactElement, type ReactNode } from 'react';

import { api, ApiError } from '../api';
import { usePreferences } from '../preferences';
import { followLink, navigate, useLocation } from '../router';
import { canReadAudit, isAdmin, useSession } from '../session';
import { ErrorBar } from '../ui/common';
import { ChangePasswordDialog } from './ChangePasswordDialog';

const wideQuery = '(min-width: 1100px)';

const useStyles = makeStyles({
  root: {
    minHeight: '100vh',
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
  },
  skip: {
    position: 'absolute',
    left: tokens.spacingHorizontalS,
    top: '-48px',
    zIndex: 10,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground1,
    borderRadius: tokens.borderRadiusMedium,
    boxShadow: tokens.shadow8,
    ':focus': { top: tokens.spacingVerticalS },
  },
  header: {
    height: '48px',
    flexShrink: 0,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `0 ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground1,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, marginRight: 'auto' },
  mark: {
    width: '28px',
    height: '28px',
    borderRadius: tokens.borderRadiusMedium,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontWeight: tokens.fontWeightBold,
  },
  body: { flexGrow: 1, display: 'flex', minHeight: 0 },
  rail: {
    width: '48px',
    flexShrink: 0,
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalXS}`,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRight: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
  },
  railItem: { width: '40px', height: '40px', minWidth: '40px' },
  // The count of requests to approve (DEC-042 item 9): after the label, or on the rail's icon.
  navContent: { flexGrow: 1, display: 'flex', alignItems: 'center', justifyContent: 'space-between' },
  railIcon: { position: 'relative', display: 'inline-flex' },
  railCount: { position: 'absolute', top: '-6px', right: '-10px' },
  railCurrent: { backgroundColor: tokens.colorBrandBackground2, color: tokens.colorBrandForeground2 },
  main: {
    flexGrow: 1,
    minWidth: 0,
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalXL} ${tokens.spacingHorizontalXXL}`,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    ':focus': { outlineStyle: 'none' },
    '@media (max-width: 767px)': { padding: `${tokens.spacingVerticalL} ${tokens.spacingHorizontalL}` },
  },
  topSearch: { width: '300px' },
  popover: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, width: '300px' },
  menuHeader: {
    display: 'flex',
    flexDirection: 'column',
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
  },
});

interface NavEntry {
  value: string;
  label: string;
  group: string;
  path: string;
  icon: ReactElement;
  /** Requests waiting for this Admin's approval (Users & roles only). */
  count?: number;
}

/** Sent by Users & roles after it changes or reloads the list, so the nav count follows at once. */
export const approvalsChanged = 'goldmark:approvals-changed';

/**
 * The requests this Admin could approve (DEC-042 item 9; Goldmark sends no email): read when a page
 * opens and after Users & roles changes something. Never on a timer: every request counts as activity,
 * so polling would keep an idle Admin's session from ending after Goldmark:Auth:SessionIdleMinutes (a
 * side agent's note). A failed read leaves the last count.
 */
function useApprovals(enabled: boolean, pathname: string): number {
  const [waiting, setWaiting] = useState(0);
  useEffect(() => {
    if (!enabled) return undefined;
    let cancelled = false;
    const refresh = () => {
      api<{ waiting: number }>('GET', '/api/v1/users/approvals')
        .then(({ data }) => {
          if (!cancelled) setWaiting(data.waiting);
        })
        .catch(() => undefined);
    };
    // Users & roles reads its list and then sends approvalsChanged itself: one read there, not two.
    if (pathname !== '/users') refresh();
    window.addEventListener(approvalsChanged, refresh);
    return () => {
      cancelled = true;
      window.removeEventListener(approvalsChanged, refresh);
    };
  }, [enabled, pathname]);
  return enabled ? waiting : 0;
}

function countLabel(label: string, count: number): string {
  return `${label}, ${count} ${count === 1 ? 'request needs' : 'requests need'} your approval`;
}

/** True at 1100 px and wider: the inline navigation and the wide page layouts (DEC-039). */
export function useWide(): boolean {
  const [wide, setWide] = useState(() => window.matchMedia(wideQuery).matches);
  useEffect(() => {
    const query = window.matchMedia(wideQuery);
    const listener = () => setWide(query.matches);
    query.addEventListener('change', listener);
    return () => query.removeEventListener('change', listener);
  }, []);
  return wide;
}

/**
 * The app shell for signed-in screens (DEC-039; slice 3's sparse nav: only the screens that exist).
 * NavDrawer inline at 1100 px and wider, an icon rail with an overlay drawer below; top bar with the
 * CI search (slice 5: a box, or below 1100 px a button opening it in a Popover; not on /search, which has
 * its own), the theme toggle and the account menu; a skip link to the main region. The As-of control
 * (slice 7) arrives with its slice.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const styles = useStyles();
  const session = useSession();
  const preferences = usePreferences();
  const { pathname } = useLocation();
  const wide = useWide();
  const [overlayOpen, setOverlayOpen] = useState(false);
  const [signOutFailure, setSignOutFailure] = useState<ApiError | null>(null);
  const [changingPassword, setChangingPassword] = useState(false);
  const accountButtonId = useId('account-menu');
  const toasterId = useId('shell-toaster');
  const { dispatchToast } = useToastController(toasterId);
  const user = session.user;
  const approvals = useApprovals(isAdmin(user), pathname);

  const closeChangePassword = () => {
    setChangingPassword(false);
    // Focus goes back where the dialog was opened from.
    document.getElementById(accountButtonId)?.focus();
  };

  const signOut = () => {
    setSignOutFailure(null);
    session
      .signOut()
      .catch((error: unknown) =>
        setSignOutFailure(error instanceof ApiError ? error : new ApiError(0, 'network', 'Couldn’t reach Goldmark.')),
      );
  };

  const entries: NavEntry[] = [
    { value: 'dashboard', label: 'Dashboard', group: 'Overview', path: '/', icon: <BoardRegular /> },
    { value: 'search', label: 'Search', group: 'Inventory', path: '/search', icon: <SearchRegular /> },
    // Slice 6 (screen 6): for everyone signed in, after Search.
    { value: 'graph', label: 'Graph explorer', group: 'Inventory', path: '/graph', icon: <FlowchartRegular /> },
    ...(isAdmin(user)
      ? [
          // Slice 5b (screen 14): first in the group, Admin only.
          {
            value: 'types',
            label: 'Types & attributes',
            group: 'Administration',
            path: '/types',
            icon: <ShapesRegular />,
          },
          {
            value: 'users',
            label: 'Users & roles',
            group: 'Administration',
            path: '/users',
            icon: <PeopleRegular />,
            count: approvals,
          },
        ]
      : []),
    ...(canReadAudit(user)
      ? [
          {
            value: 'audit',
            label: 'Audit log',
            group: 'Administration',
            path: '/audit',
            icon: <DocumentBulletListClockRegular />,
          },
        ]
      : []),
  ];
  // A CI's page sits under Search (its breadcrumb starts there).
  const selected = pathname.startsWith('/ci/')
    ? 'search'
    : (entries.find((entry) => (entry.path === '/' ? pathname === '/' : pathname.startsWith(entry.path)))?.value ?? '');
  const otherTheme = preferences.theme === 'dark' ? 'light' : 'dark';
  const initials = (user?.displayName ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');
  // The visible initials start the name, so voice control can say them (WCAG 2.5.3, Label in Name).
  const accountLabel = `${initials ? `${initials}, ` : ''}Account: ${user?.displayName ?? ''}`;

  const go = (event: MouseEvent<HTMLElement>, path: string) => {
    setOverlayOpen(false);
    followLink(event, path);
  };

  const drawer = (type: 'inline' | 'overlay') => (
    <NavDrawer
      type={type}
      open={type === 'inline' || overlayOpen}
      selectedValue={selected}
      onOpenChange={(_, data) => setOverlayOpen(data.open)}
      aria-label="Main"
      size="small"
    >
      <NavDrawerBody>
        {['Overview', 'Inventory', 'Administration'].map((group) => {
          const items = entries.filter((entry) => entry.group === group);
          return items.length === 0 ? null : (
            <div key={group}>
              <NavSectionHeader>{group}</NavSectionHeader>
              {items.map((entry) => (
                <NavItem
                  key={entry.value}
                  value={entry.value}
                  href={entry.path}
                  icon={entry.icon}
                  onClick={(event) => go(event, entry.path)}
                  {...(entry.count ? { 'aria-label': countLabel(entry.label, entry.count) } : {})}
                >
                  <span className={styles.navContent}>
                    {entry.label}
                    {/* The space makes the visible text "Users & roles 2", not "roles2", so it's in the name (Label in Name). */}
                    {entry.count ? (
                      <>
                        {' '}
                        <CounterBadge aria-hidden count={entry.count} color="brand" size="medium" />
                      </>
                    ) : null}
                  </span>
                </NavItem>
              ))}
            </div>
          );
        })}
      </NavDrawerBody>
    </NavDrawer>
  );

  return (
    <div className={styles.root}>
      <a className={styles.skip} href="#main">
        Skip to content
      </a>
      <header className={styles.header}>
        {wide ? null : (
          <Button
            appearance="subtle"
            icon={<NavigationRegular />}
            aria-label="Open navigation"
            onClick={() => setOverlayOpen(true)}
          />
        )}
        <div className={styles.brand}>
          <span aria-hidden="true" className={styles.mark}>
            G
          </span>
          <Text weight="semibold" size={400}>
            Goldmark
          </Text>
        </div>
        {pathname === '/search' ? null : <TopSearch wide={wide} />}
        <Tooltip content={`Switch to ${otherTheme} theme`} relationship="label">
          <Button
            appearance="subtle"
            icon={preferences.theme === 'dark' ? <WeatherSunnyRegular /> : <WeatherMoonRegular />}
            onClick={() => preferences.setTheme(otherTheme)}
          />
        </Tooltip>
        <Menu
          checkedValues={{ density: preferences.compact ? ['compact'] : [] }}
          onCheckedValueChange={(_, data) => preferences.setCompact(data.checkedItems.includes('compact'))}
        >
          <MenuTrigger disableButtonEnhancement>
            <Button
              id={accountButtonId}
              appearance="subtle"
              aria-label={accountLabel}
              icon={<ChevronDownRegular />}
              iconPosition="after"
            >
              <Avatar size={24} name={user?.displayName ?? ''} initials={initials} aria-hidden />
            </Button>
          </MenuTrigger>
          <MenuPopover>
            <MenuList>
              <MenuGroup>
                <MenuGroupHeader className={styles.menuHeader}>
                  <Text weight="semibold">{user?.displayName}</Text>
                  <Text size={200}>{user?.userName}</Text>
                </MenuGroupHeader>
              </MenuGroup>
              <MenuItem icon={<KeyRegular />} aria-haspopup="dialog" onClick={() => setChangingPassword(true)}>
                Change password…
              </MenuItem>
              <MenuItem icon={<ShieldCheckmarkRegular />} onClick={() => navigate('/account/two-factor')}>
                Two-factor authentication
              </MenuItem>
              <MenuItem
                icon={preferences.theme === 'dark' ? <WeatherSunnyRegular /> : <WeatherMoonRegular />}
                onClick={() => preferences.setTheme(otherTheme)}
              >
                {`Switch to ${otherTheme} theme`}
              </MenuItem>
              <MenuItemCheckbox name="density" value="compact">
                Compact density
              </MenuItemCheckbox>
              <MenuDivider />
              <MenuItem icon={<SignOutRegular />} onClick={signOut}>
                Sign out
              </MenuItem>
            </MenuList>
          </MenuPopover>
        </Menu>
      </header>
      <div className={styles.body}>
        {wide ? (
          drawer('inline')
        ) : (
          <>
            <nav className={styles.rail} aria-label="Main">
              {entries.map((entry) => (
                <Tooltip
                  key={entry.value}
                  content={entry.count ? countLabel(entry.label, entry.count) : entry.label}
                  relationship="label"
                  positioning="after"
                >
                  <Button
                    as="a"
                    href={entry.path}
                    appearance="subtle"
                    icon={
                      entry.count ? (
                        <span className={styles.railIcon}>
                          {entry.icon}
                          <CounterBadge
                            aria-hidden
                            className={styles.railCount}
                            count={entry.count}
                            color="brand"
                            size="small"
                          />
                        </span>
                      ) : (
                        entry.icon
                      )
                    }
                    aria-current={selected === entry.value ? 'page' : undefined}
                    className={mergeClasses(styles.railItem, selected === entry.value && styles.railCurrent)}
                    onClick={(event) => go(event, entry.path)}
                  />
                </Tooltip>
              ))}
            </nav>
            {drawer('overlay')}
          </>
        )}
        <main id="main" tabIndex={-1} className={styles.main}>
          {signOutFailure ? (
            <ErrorBar
              error={signOutFailure}
              message="You’re still signed in: Goldmark couldn’t end your session. Try again before you leave this computer."
              onRetry={signOut}
            />
          ) : null}
          {children}
        </main>
      </div>
      {changingPassword ? (
        <ChangePasswordDialog
          onClose={closeChangePassword}
          onChanged={() => {
            closeChangePassword();
            dispatchToast(
              <Toast>
                <ToastTitle>Password changed. You’re signed out on your other devices.</ToastTitle>
              </Toast>,
              { intent: 'success' },
            );
          }}
        />
      ) : null}
      <Toaster toasterId={toasterId} position="bottom-end" />
    </div>
  );
}

/** The top bar's CI search: Enter opens /search?q=… (US-53). */
function TopSearch({ wide }: { wide: boolean }) {
  const styles = useStyles();
  const [q, setQ] = useState('');
  const [open, setOpen] = useState(false);
  const go = () => {
    const text = q.trim();
    setOpen(false);
    setQ('');
    navigate(`/search${text ? `?q=${encodeURIComponent(text)}` : ''}`);
  };
  const box = (
    <SearchBox
      className={styles.topSearch}
      input={{ 'aria-label': 'Search CIs' }}
      placeholder="Search CIs by name"
      value={q}
      onChange={(_, data) => setQ(data.value)}
      onKeyDown={(event) => {
        if (event.key === 'Enter') go();
      }}
    />
  );
  if (wide) {
    return <div role="search">{box}</div>;
  }
  return (
    <Popover open={open} onOpenChange={(_, data) => setOpen(data.open)} trapFocus positioning="below-end">
      <PopoverTrigger disableButtonEnhancement>
        <Tooltip content="Search CIs" relationship="label">
          <Button appearance="subtle" icon={<SearchRegular />} />
        </Tooltip>
      </PopoverTrigger>
      <PopoverSurface aria-label="Search CIs">
        <div role="search" className={styles.popover}>
          {box}
          <Text size={200}>Press Enter to see the results on Search.</Text>
        </div>
      </PopoverSurface>
    </Popover>
  );
}

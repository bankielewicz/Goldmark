import {
  Avatar,
  Button,
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
  Text,
  Tooltip,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import {
  BoardRegular,
  ChevronDownRegular,
  NavigationRegular,
  PeopleRegular,
  ShieldCheckmarkRegular,
  SignOutRegular,
  WeatherMoonRegular,
  WeatherSunnyRegular,
} from '@fluentui/react-icons';
import { useEffect, useState, type MouseEvent, type ReactElement, type ReactNode } from 'react';

import { usePreferences } from '../preferences';
import { followLink, navigate, useLocation } from '../router';
import { isAdmin, useSession } from '../session';

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
}

function useWide(): boolean {
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
 * theme toggle and the account menu; a skip link to the main region. Search (slice 5) and the
 * As-of control (slice 7) arrive with their slices.
 */
export function AppShell({ children }: { children: ReactNode }) {
  const styles = useStyles();
  const session = useSession();
  const preferences = usePreferences();
  const { pathname } = useLocation();
  const wide = useWide();
  const [overlayOpen, setOverlayOpen] = useState(false);
  const user = session.user;

  const entries: NavEntry[] = [
    { value: 'dashboard', label: 'Dashboard', group: 'Overview', path: '/', icon: <BoardRegular /> },
    ...(isAdmin(user)
      ? [{ value: 'users', label: 'Users & roles', group: 'Administration', path: '/users', icon: <PeopleRegular /> }]
      : []),
  ];
  const selected =
    entries.find((entry) => (entry.path === '/' ? pathname === '/' : pathname.startsWith(entry.path)))?.value ?? '';
  const otherTheme = preferences.theme === 'dark' ? 'light' : 'dark';
  const initials = (user?.displayName ?? '')
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join('');

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
        {['Overview', 'Administration'].map((group) => {
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
                >
                  {entry.label}
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
              appearance="subtle"
              aria-label={`Account: ${user?.displayName ?? ''}`}
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
              <MenuItem icon={<SignOutRegular />} onClick={() => void session.signOut()}>
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
                <Tooltip key={entry.value} content={entry.label} relationship="label" positioning="after">
                  <Button
                    as="a"
                    href={entry.path}
                    appearance="subtle"
                    icon={entry.icon}
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
          {children}
        </main>
      </div>
    </div>
  );
}

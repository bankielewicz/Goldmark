import {
  Avatar,
  Badge,
  Button,
  Checkbox,
  DataGrid,
  DataGridBody,
  DataGridCell,
  DataGridHeader,
  DataGridHeaderCell,
  DataGridRow,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DrawerBody,
  DrawerFooter,
  DrawerHeader,
  DrawerHeaderTitle,
  Dropdown,
  Field,
  InlineDrawer,
  Input,
  Menu,
  MenuDivider,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarBody,
  Option,
  OverlayDrawer,
  SearchBox,
  Skeleton,
  SkeletonItem,
  Spinner,
  Text,
  Toast,
  ToastTitle,
  Toaster,
  Tooltip,
  createTableColumn,
  makeStyles,
  tokens,
  useId,
  useToastController,
  type TableColumnDefinition,
} from '@fluentui/react-components';
import {
  ArrowExportRegular,
  CheckmarkCircleRegular,
  DismissRegular,
  LockClosedRegular,
  MoreHorizontalRegular,
  PersonAddRegular,
  ShieldCheckmarkRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useMemo, useState, type FormEvent, type ReactNode } from 'react';

import { api, ApiError } from '../api';
import { formatRemaining, formatUtc, formatUtcTime, initialsOf } from '../format';
import { usePreferences } from '../preferences';
import { useLocation } from '../router';
import { useSession } from '../session';
import { useWide } from '../shell/AppShell';
import { PageHeader } from '../shell/PageHeader';
import { CopyButton, ErrorBar } from '../ui/common';
import { describeFieldError } from './EnrolmentPage';

const useStyles = makeStyles({
  filters: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', alignItems: 'center' },
  search: { minWidth: '260px' },
  layout: { display: 'flex', gap: tokens.spacingHorizontalL, alignItems: 'flex-start', minHeight: 0 },
  region: {
    flexGrow: 1,
    minWidth: 0,
    overflowX: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  grid: { minWidth: '960px' },
  name: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  you: { color: tokens.colorNeutralForeground3 },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  badges: { display: 'flex', gap: tokens.spacingHorizontalXS, flexWrap: 'wrap' },
  footer: {
    display: 'flex',
    justifyContent: 'space-between',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
  },
  empty: {
    padding: tokens.spacingHorizontalXXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
    alignItems: 'flex-start',
  },
  panel: { width: '400px' },
  panelBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  roleOption: { display: 'flex', flexDirection: 'column' },
  roleHint: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  section: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS },
  h3: { margin: 0, fontSize: tokens.fontSizeBase300, fontWeight: tokens.fontWeightSemibold },
  secretBox: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  link: {
    flexGrow: 1,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase200,
    overflowWrap: 'anywhere',
  },
  bigCode: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeHero700,
    lineHeight: tokens.lineHeightHero700,
    letterSpacing: '0.12em',
    fontWeight: tokens.fontWeightSemibold,
  },
  fieldset: {
    border: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
  },
  legend: { fontWeight: tokens.fontWeightSemibold, marginBottom: tokens.spacingVerticalXS },
  srOnly: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    margin: '-1px',
    overflow: 'hidden',
    clipPath: 'inset(50%)',
    whiteSpace: 'nowrap',
  },
});

const roleText: Record<string, string> = {
  Admin: 'Everything, including users and settings.',
  Editor: 'Create, edit and retire CIs and relationships.',
  Viewer: 'Read only.',
  Auditor: 'Read only, plus the audit log.',
};

interface OpenLink {
  purpose: 'invitation' | 'two-factor-reset';
  expiresAt: string;
  expired: boolean;
}

interface UserRow {
  id: string;
  userName: string;
  displayName: string;
  roles: string[];
  twoFactorEnrolled: boolean;
  account: {
    state: 'active' | 'locked' | 'disabled' | 'invitation-pending' | 'invitation-expired';
    lockedUntil: string | null;
    link: OpenLink | null;
  };
  lastSignInAt: string | null;
  createdAt: string;
  eTag: string;
}

interface IssuedLink {
  token: string;
  code: string;
  expiresAt: string;
  /** The configured limits (Goldmark:Auth:LinkExpiryHours, LinkCodeAttempts). */
  expiryHours: number;
  codeAttempts: number;
}

type Panel =
  | { kind: 'add' }
  | { kind: 'roles'; user: UserRow }
  | { kind: 'secret'; purpose: 'invitation' | 'two-factor-reset'; user: UserRow; link: IssuedLink };

type Confirm = { action: 'lock' | 'unlock' | 'disable' | 'enable' | 'reset' | 'revoke' | 'reissue'; user: UserRow };

const accountFilters = ['All', 'Active', 'Locked', 'Disabled', 'Invitation pending', 'Invitation expired'] as const;
const stateOfFilter: Record<string, UserRow['account']['state']> = {
  Active: 'active',
  Locked: 'locked',
  Disabled: 'disabled',
  'Invitation pending': 'invitation-pending',
  'Invitation expired': 'invitation-expired',
};

function isPending(user: UserRow) {
  return user.account.state === 'invitation-pending' || user.account.state === 'invitation-expired';
}

/** Screen 17 (US-04): every person, their roles and account state; invitations; the admin's actions. */
export function UsersPage() {
  const styles = useStyles();
  const session = useSession();
  const toasterId = useId('users-toaster');
  const { dispatchToast } = useToastController(toasterId);
  const [users, setUsers] = useState<UserRow[] | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const { search } = useLocation();
  // "Show this person in Users & roles" on the audit log opens the page filtered (?q=username).
  const [filter, setFilter] = useState(() => search.get('q') ?? '');
  const [role, setRole] = useState('All');
  const [account, setAccount] = useState<string>('All');
  const [panel, setPanel] = useState<Panel | null>(null);
  const [confirm, setConfirm] = useState<Confirm | null>(null);
  const wide = useWide();
  const { compact } = usePreferences();
  const me = session.user?.id;

  const load = useCallback(async () => {
    setFailure(null);
    try {
      setUsers((await api<{ users: UserRow[] }>('GET', '/api/v1/users')).data.users);
    } catch (error) {
      if (error instanceof ApiError) setFailure(error);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const toast = useCallback(
    (text: string) =>
      dispatchToast(
        <Toast>
          <ToastTitle media={<CheckmarkCircleRegular />}>{text}</ToastTitle>
        </Toast>,
        { intent: 'success', politeness: 'polite' },
      ),
    [dispatchToast],
  );

  const enabledAdmins = (users ?? []).filter(
    (u) => u.roles.includes('Admin') && u.account.state !== 'disabled' && !isPending(u),
  ).length;

  const visible = useMemo(() => {
    const text = filter.trim().toLowerCase();
    return (users ?? []).filter(
      (u) =>
        (text === '' || u.displayName.toLowerCase().includes(text) || u.userName.toLowerCase().includes(text)) &&
        (role === 'All' || u.roles.includes(role)) &&
        (account === 'All' || u.account.state === stateOfFilter[account]),
    );
  }, [users, filter, role, account]);

  const columns: TableColumnDefinition<UserRow>[] = useMemo(
    () => [
      createTableColumn<UserRow>({
        columnId: 'name',
        compare: (a, b) => a.displayName.localeCompare(b.displayName),
        renderHeaderCell: () => 'Name',
        renderCell: (u) => (
          <span className={styles.name}>
            <Avatar size={24} name={u.displayName} initials={initialsOf(u.displayName)} aria-hidden />
            {u.displayName}
            {u.id === me ? <span className={styles.you}>(you)</span> : null}
          </span>
        ),
      }),
      createTableColumn<UserRow>({
        columnId: 'userName',
        renderHeaderCell: () => 'Username',
        renderCell: (u) => <span className={styles.mono}>{u.userName}</span>,
      }),
      createTableColumn<UserRow>({
        columnId: 'roles',
        renderHeaderCell: () => 'Roles',
        renderCell: (u) => (
          <span className={styles.badges}>
            {u.roles.map((r) => (
              <Badge key={r} appearance="tint" color="subtle">
                {r}
              </Badge>
            ))}
          </span>
        ),
      }),
      createTableColumn<UserRow>({
        columnId: 'twoFactor',
        renderHeaderCell: () => 'Two-factor',
        renderCell: (u) =>
          u.twoFactorEnrolled ? (
            <Badge appearance="tint" color="success" icon={<ShieldCheckmarkRegular />}>
              Enrolled
            </Badge>
          ) : (
            <Badge appearance="tint" color="informative">
              Not enrolled
            </Badge>
          ),
      }),
      createTableColumn<UserRow>({
        columnId: 'account',
        renderHeaderCell: () => 'Account',
        renderCell: (u) => <AccountState user={u} />,
      }),
      createTableColumn<UserRow>({
        columnId: 'lastSignIn',
        compare: (a, b) => (a.lastSignInAt ?? '').localeCompare(b.lastSignInAt ?? ''),
        renderHeaderCell: () => 'Last sign-in (UTC)',
        renderCell: (u) => formatUtc(u.lastSignInAt),
      }),
      createTableColumn<UserRow>({
        columnId: 'actions',
        renderHeaderCell: () => <span className={styles.srOnly}>Actions</span>,
        renderCell: (u) => (
          <RowActions
            user={u}
            self={u.id === me}
            lastAdmin={u.roles.includes('Admin') && enabledAdmins <= 1}
            onEditRoles={() => setPanel({ kind: 'roles', user: u })}
            onConfirm={(action) => setConfirm({ action, user: u })}
          />
        ),
      }),
    ],
    [styles, me, enabledAdmins],
  );

  const body: ReactNode =
    failure && !users ? (
      <ErrorBar error={failure} onRetry={() => void load()} message="Couldn’t load users." />
    ) : !users ? (
      <Skeleton aria-label="Loading users">
        {[1, 2, 3, 4, 5, 6].map((n) => (
          <SkeletonItem key={n} size={32} />
        ))}
      </Skeleton>
    ) : users.length === 1 && users[0]?.id === me ? (
      <div className={styles.empty}>
        <Text>You’re the only user. Add someone to give them access.</Text>
        <Button appearance="primary" icon={<PersonAddRegular />} onClick={() => setPanel({ kind: 'add' })}>
          Add user
        </Button>
      </div>
    ) : (
      <DataGrid
        className={styles.grid}
        size={compact ? 'small' : 'medium'}
        items={visible}
        columns={columns}
        sortable
        defaultSortState={{ sortColumn: 'name', sortDirection: 'ascending' }}
        getRowId={(u: UserRow) => u.id}
        aria-label="Users"
      >
        <DataGridHeader>
          <DataGridRow>
            {({ renderHeaderCell }) => <DataGridHeaderCell>{renderHeaderCell()}</DataGridHeaderCell>}
          </DataGridRow>
        </DataGridHeader>
        <DataGridBody<UserRow>>
          {({ item, rowId }) => (
            <DataGridRow<UserRow> key={rowId}>
              {({ renderCell }) => <DataGridCell>{renderCell(item)}</DataGridCell>}
            </DataGridRow>
          )}
        </DataGridBody>
      </DataGrid>
    );

  const closePanel = () => setPanel(null);
  const panelContent = panel ? (
    <UserPanel
      panel={panel}
      onClose={closePanel}
      onCreated={(user, link) => {
        void load();
        setPanel({ kind: 'secret', purpose: 'invitation', user, link });
      }}
      onSaved={(user) => {
        void load();
        setPanel(null);
        toast(`${user.displayName} now has ${joinRoles(user.roles)}`);
      }}
      onDone={() => {
        setPanel(null);
        void load();
      }}
    />
  ) : null;

  return (
    <>
      <Toaster toasterId={toasterId} position="bottom-end" />
      <PageHeader
        crumbs={[{ label: 'Administration' }, { label: 'Users & roles' }]}
        title="Users & roles"
        actions={
          <>
            <Button as="a" href="/api/v1/users/export" download icon={<ArrowExportRegular />}>
              Export CSV
            </Button>
            <Button appearance="primary" icon={<PersonAddRegular />} onClick={() => setPanel({ kind: 'add' })}>
              Add user
            </Button>
          </>
        }
      />
      <div role="toolbar" aria-label="Filters" className={styles.filters}>
        <SearchBox
          className={styles.search}
          placeholder="Filter by name or username"
          aria-label="Filter by name or username"
          value={filter}
          onChange={(_, d) => setFilter(d.value)}
        />
        <Dropdown
          aria-label="Role"
          value={`Role: ${role}`}
          selectedOptions={[role]}
          onOptionSelect={(_, d) => setRole(d.optionValue ?? 'All')}
        >
          {['All', 'Admin', 'Editor', 'Viewer', 'Auditor'].map((r) => (
            <Option key={r} value={r}>
              {r}
            </Option>
          ))}
        </Dropdown>
        <Dropdown
          aria-label="Account"
          value={`Account: ${account}`}
          selectedOptions={[account]}
          onOptionSelect={(_, d) => setAccount(d.optionValue ?? 'All')}
        >
          {accountFilters.map((a) => (
            <Option key={a} value={a}>
              {a}
            </Option>
          ))}
        </Dropdown>
      </div>
      {failure && users ? <ErrorBar error={failure} onRetry={() => void load()} /> : null}
      <div className={styles.layout}>
        <div className={styles.region}>{body}</div>
        {wide && panel ? (
          <InlineDrawer open position="end" separator className={styles.panel} aria-labelledby="panel-title">
            {panelContent}
          </InlineDrawer>
        ) : null}
      </div>
      {users ? (
        <div className={styles.footer}>
          <span aria-live="polite">
            {visible.length} {visible.length === 1 ? 'person' : 'people'}
          </span>
        </div>
      ) : null}
      {!wide ? (
        <OverlayDrawer
          open={panel !== null}
          position="end"
          onOpenChange={(_, d) => (d.open ? undefined : closePanel())}
          aria-labelledby="panel-title"
        >
          {panelContent}
        </OverlayDrawer>
      ) : null}
      {confirm ? (
        <ConfirmDialog
          confirm={confirm}
          onCancel={() => setConfirm(null)}
          onDone={(result) => {
            setConfirm(null);
            void load();
            if (result.link) {
              setPanel({
                kind: 'secret',
                purpose: confirm.action === 'reset' ? 'two-factor-reset' : 'invitation',
                user: confirm.user,
                link: result.link,
              });
            } else if (result.message) {
              toast(result.message);
            }
          }}
        />
      ) : null}
    </>
  );
}

function joinRoles(roles: string[]): string {
  return roles.length <= 1
    ? (roles[0] ?? 'no role')
    : `${roles.slice(0, -1).join(', ')} and ${roles[roles.length - 1]}`;
}

function AccountState({ user }: { user: UserRow }) {
  const { state, lockedUntil, link } = user.account;
  switch (state) {
    case 'active':
      return (
        <Text>
          {link?.purpose === 'two-factor-reset' && !link.expired
            ? `Active · reset link expires in ${formatRemaining(link.expiresAt)}`
            : 'Active'}
        </Text>
      );
    case 'locked':
      return (
        <Badge appearance="tint" color="severe" icon={<LockClosedRegular />}>
          {lockedUntil ? `Locked until ${formatUtcTime(lockedUntil)}` : 'Locked'}
        </Badge>
      );
    case 'disabled':
      return (
        <Badge appearance="tint" color="informative">
          Disabled
        </Badge>
      );
    case 'invitation-pending':
      return (
        <Badge appearance="tint" color="informative">
          {link ? `Invitation pending · expires in ${formatRemaining(link.expiresAt)}` : 'Invitation pending'}
        </Badge>
      );
    default:
      return (
        <Badge appearance="tint" color="informative">
          Invitation expired
        </Badge>
      );
  }
}

/** A menu item that stays focusable when unavailable and says why (aria-disabled + Tooltip). */
function GuardedItem({
  reason,
  onClick,
  children,
}: {
  reason: string | null;
  onClick: () => void;
  children: ReactNode;
}) {
  if (!reason) {
    return <MenuItem onClick={onClick}>{children}</MenuItem>;
  }
  return (
    <Tooltip content={reason} relationship="description" positioning="after">
      <MenuItem disabled>{children}</MenuItem>
    </Tooltip>
  );
}

function RowActions({
  user,
  self,
  lastAdmin,
  onEditRoles,
  onConfirm,
}: {
  user: UserRow;
  self: boolean;
  lastAdmin: boolean;
  onEditRoles: () => void;
  onConfirm: (action: Confirm['action']) => void;
}) {
  const pending = isPending(user);
  const locked = user.account.state === 'locked';
  const disabled = user.account.state === 'disabled';
  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <Button appearance="subtle" icon={<MoreHorizontalRegular />} aria-label={`Actions for ${user.displayName}`} />
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {pending ? (
            <>
              <MenuItem onClick={() => onConfirm('reissue')}>Copy new link…</MenuItem>
              <MenuItem onClick={() => onConfirm('revoke')}>Revoke invitation…</MenuItem>
            </>
          ) : (
            <>
              <MenuItem onClick={onEditRoles}>Edit roles…</MenuItem>
              {locked ? (
                <MenuItem onClick={() => onConfirm('unlock')}>Unlock…</MenuItem>
              ) : (
                <GuardedItem
                  reason={self ? 'You can’t lock your own account.' : null}
                  onClick={() => onConfirm('lock')}
                >
                  Lock…
                </GuardedItem>
              )}
              {disabled ? (
                <MenuItem onClick={() => onConfirm('enable')}>Enable…</MenuItem>
              ) : (
                <GuardedItem
                  reason={
                    self
                      ? 'You can’t disable your own account.'
                      : lastAdmin
                        ? 'Goldmark needs at least one enabled Admin.'
                        : null
                  }
                  onClick={() => onConfirm('disable')}
                >
                  Disable…
                </GuardedItem>
              )}
              <MenuDivider />
              <GuardedItem
                reason={self ? 'You can’t reset your own two-factor here. Use your two-factor page.' : null}
                onClick={() => onConfirm('reset')}
              >
                Reset two-factor…
              </GuardedItem>
            </>
          )}
        </MenuList>
      </MenuPopover>
    </Menu>
  );
}

function confirmText(confirm: Confirm): { title: string; body: string; button: string; destructive: boolean } {
  const name = confirm.user.displayName;
  const first = name.split(' ')[0] ?? name;
  switch (confirm.action) {
    case 'lock':
      return {
        title: `Lock ${name}?`,
        body: `${first} is signed out and can’t sign in until you unlock the account. Unlock is on the same menu.`,
        button: 'Lock account',
        destructive: false,
      };
    case 'unlock':
      return {
        title: `Unlock ${name}?`,
        body: `${first} can sign in again.`,
        button: 'Unlock account',
        destructive: false,
      };
    case 'disable':
      return {
        title: `Disable ${name}?`,
        body: `${first} can no longer sign in and is signed out now. Their history is kept; you can enable the account again.`,
        button: 'Disable account',
        destructive: true,
      };
    case 'enable':
      return {
        title: `Enable ${name}?`,
        body: `${first} can sign in again with their password and two-factor.`,
        button: 'Enable account',
        destructive: false,
      };
    case 'reset':
      return {
        title: `Reset two-factor for ${name}?`,
        body: `This signs ${first} out everywhere and removes their authenticator and recovery codes. Their password and roles stay. Next you get a link and a code. ${first} needs both to set up two-factor again before the link expires.`,
        button: 'Reset two-factor',
        destructive: true,
      };
    case 'revoke':
      return {
        title: `Revoke the invitation for ${name}?`,
        body: 'The link and code stop working at once.',
        button: 'Revoke',
        destructive: true,
      };
    default:
      return {
        title: `New link for ${name}?`,
        body: 'This issues a new link and code; the old ones stop working.',
        button: 'Create new link',
        destructive: false,
      };
  }
}

function ConfirmDialog({
  confirm,
  onCancel,
  onDone,
}: {
  confirm: Confirm;
  onCancel: () => void;
  onDone: (result: { message?: string; link?: IssuedLink }) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ApiError | null>(null);
  const text = confirmText(confirm);
  const id = confirm.user.id;

  async function act() {
    setBusy(true);
    setProblem(null);
    try {
      switch (confirm.action) {
        case 'reset':
        case 'reissue': {
          const path =
            confirm.action === 'reset' ? `/api/v1/users/${id}/two-factor-reset` : `/api/v1/users/${id}/invitation`;
          const { data } = await api<{ link: IssuedLink }>('POST', path, {});
          onDone({ link: data.link });
          return;
        }
        case 'revoke':
          await api('DELETE', `/api/v1/users/${id}/invitation`);
          onDone({ message: 'Invitation revoked' });
          return;
        default:
          await api('POST', `/api/v1/users/${id}/${confirm.action}`, {});
          onDone({
            message: {
              lock: 'Account locked',
              unlock: 'Account unlocked',
              disable: 'Account disabled',
              enable: 'Account enabled',
            }[confirm.action],
          });
      }
    } catch (error) {
      if (error instanceof ApiError) setProblem(error);
      else throw error;
    } finally {
      setBusy(false);
    }
  }

  return (
    <Dialog open modalType="alert" onOpenChange={(_, d) => (d.open ? undefined : onCancel())}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{text.title}</DialogTitle>
          <DialogContent>
            {problem ? (
              problem.isUnavailable ? (
                <ErrorBar error={problem} onRetry={() => void act()} />
              ) : (
                <MessageBar intent="error">
                  <MessageBarBody>{problem.message}</MessageBarBody>
                </MessageBar>
              )
            ) : null}
            {text.destructive ? (
              <MessageBar intent="warning">
                <MessageBarBody>{text.body}</MessageBarBody>
              </MessageBar>
            ) : (
              <Text as="p">{text.body}</Text>
            )}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onCancel}>
              Cancel
            </Button>
            <Button
              appearance="primary"
              disabled={busy}
              onClick={() => void act()}
              {...(busy ? { icon: <Spinner size="tiny" /> } : {})}
            >
              {text.button}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function UserPanel({
  panel,
  onClose,
  onCreated,
  onSaved,
  onDone,
}: {
  panel: Panel;
  onClose: () => void;
  onCreated: (user: UserRow, link: IssuedLink) => void;
  onSaved: (user: UserRow) => void;
  onDone: () => void;
}) {
  const title =
    panel.kind === 'add'
      ? 'Add user'
      : panel.kind === 'roles'
        ? `Roles for ${panel.user.displayName}`
        : panel.purpose === 'invitation'
          ? `Invitation for ${panel.user.displayName}`
          : `New set-up link for ${panel.user.displayName}`;
  return (
    <>
      <DrawerHeader>
        <DrawerHeaderTitle
          heading={{ id: 'panel-title' }}
          action={<Button appearance="subtle" aria-label="Close" icon={<DismissRegular />} onClick={onClose} />}
        >
          {title}
        </DrawerHeaderTitle>
      </DrawerHeader>
      {panel.kind === 'add' ? <AddUser onClose={onClose} onCreated={onCreated} /> : null}
      {panel.kind === 'roles' ? <EditRoles user={panel.user} onClose={onClose} onSaved={onSaved} /> : null}
      {panel.kind === 'secret' ? <ShownOnce panel={panel} onDone={onDone} /> : null}
    </>
  );
}

function AddUser({
  onClose,
  onCreated,
}: {
  onClose: () => void;
  onCreated: (user: UserRow, link: IssuedLink) => void;
}) {
  const styles = useStyles();
  const [displayName, setDisplayName] = useState('');
  const [userName, setUserName] = useState('');
  const [roles, setRoles] = useState<string[]>(['Viewer']);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [problem, setProblem] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setProblem(null);
    setErrors({});
    try {
      const { data } = await api<{ user: UserRow; link: IssuedLink }>('POST', '/api/v1/users', {
        displayName,
        userName,
        roles,
      });
      onCreated(data.user, data.link);
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      if (error.code === 'validation') {
        setErrors(
          Object.fromEntries(
            Object.entries(error.fieldErrors).map(([field, codes]) => [
              field,
              field === 'roles' ? 'Choose at least one role.' : describeFieldError(codes[0] ?? ''),
            ]),
          ),
        );
      } else {
        setProblem(error);
      }
    } finally {
      setBusy(false);
    }
  }

  return (
    <form onSubmit={(event) => void onSubmit(event)} noValidate>
      <DrawerBody className={styles.panelBody}>
        {problem ? <ErrorBar error={problem} onRetry={() => setProblem(null)} /> : null}
        <Field label="Display name" required {...validation(errors.displayName)}>
          <Input value={displayName} onChange={(_, d) => setDisplayName(d.value)} />
        </Field>
        <Field
          label="Username"
          required
          hint="They sign in with this. It can’t be changed later."
          {...validation(errors.userName)}
        >
          <Input
            className={styles.mono}
            autoComplete="off"
            value={userName}
            onChange={(_, d) => setUserName(d.value)}
          />
        </Field>
        <fieldset className={styles.fieldset}>
          <legend className={styles.legend}>Roles</legend>
          {['Editor', 'Viewer', 'Auditor'].map((r) => (
            <RoleCheckbox
              key={r}
              role={r}
              checked={roles.includes(r)}
              onChange={(checked) => setRoles((rs) => (checked ? [...rs, r] : rs.filter((x) => x !== r)))}
            />
          ))}
          {errors.roles ? <Text className={styles.roleHint}>{errors.roles}</Text> : null}
          <Text className={styles.roleHint}>
            Admin is added after their first sign-in, so a leaked invitation can never create an Admin.
          </Text>
        </fieldset>
      </DrawerBody>
      <DrawerFooter>
        <Button type="submit" appearance="primary" disabled={busy} {...(busy ? { icon: <Spinner size="tiny" /> } : {})}>
          Create invitation
        </Button>
        <Button onClick={onClose}>Cancel</Button>
      </DrawerFooter>
    </form>
  );
}

function EditRoles({
  user,
  onClose,
  onSaved,
}: {
  user: UserRow;
  onClose: () => void;
  onSaved: (user: UserRow) => void;
}) {
  const styles = useStyles();
  const session = useSession();
  const [current, setCurrent] = useState(user);
  const [roles, setRoles] = useState<string[]>(user.roles);
  const [problem, setProblem] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);
  const self = user.id === session.user?.id;
  const neverSignedIn = current.lastSignInAt === null;

  async function save(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setProblem(null);
    try {
      const { data } = await api<UserRow>(
        'PUT',
        `/api/v1/users/${user.id}/roles`,
        { roles },
        { ifMatch: `"${current.eTag}"` },
      );
      onSaved(data);
    } catch (error) {
      if (error instanceof ApiError) setProblem(error);
      else throw error;
    } finally {
      setBusy(false);
    }
  }

  async function reload() {
    const { data, etag } = await api<UserRow>('GET', `/api/v1/users/${user.id}`);
    setCurrent({ ...data, eTag: etag?.replace(/^W\//, '').replace(/"/g, '') ?? data.eTag });
    setRoles(data.roles);
    setProblem(null);
  }

  return (
    <form onSubmit={(event) => void save(event)} noValidate>
      <DrawerBody className={styles.panelBody}>
        <Text>
          {current.displayName} · <span className={styles.mono}>{current.userName}</span> ·{' '}
          {current.lastSignInAt ? `last signed in ${formatUtc(current.lastSignInAt).slice(0, 10)}` : 'never signed in'}
        </Text>
        {problem?.status === 412 ? (
          <MessageBar intent="warning">
            <MessageBarBody>
              {current.displayName}’s roles changed while you were editing. Your change isn’t saved yet.{' '}
              <Button size="small" onClick={() => void reload()}>
                Review their changes
              </Button>
            </MessageBarBody>
          </MessageBar>
        ) : problem?.code === 'last-admin' ? (
          <MessageBar intent="error">
            <MessageBarBody>
              {current.displayName} is now the only Admin, so Admin can’t be removed. Give someone else Admin first.
            </MessageBarBody>
          </MessageBar>
        ) : problem ? (
          problem.isUnavailable ? (
            <ErrorBar error={problem} onRetry={() => setProblem(null)} />
          ) : (
            <MessageBar intent="error">
              <MessageBarBody>
                {problem.code === 'validation' ? 'Choose at least one role.' : problem.message}
              </MessageBarBody>
            </MessageBar>
          )
        ) : null}
        <fieldset className={styles.fieldset}>
          <legend className={styles.legend}>Roles</legend>
          {['Admin', 'Editor', 'Viewer', 'Auditor'].map((r) => {
            const lockedAdmin =
              r === 'Admin' && ((self && roles.includes('Admin')) || (neverSignedIn && !roles.includes('Admin')));
            const reason =
              r !== 'Admin'
                ? null
                : self
                  ? 'You can’t remove Admin from yourself.'
                  : neverSignedIn
                    ? 'Add Admin after their first sign-in.'
                    : null;
            const box = (
              <RoleCheckbox
                key={r}
                role={r}
                checked={roles.includes(r)}
                disabled={lockedAdmin}
                onChange={(checked) => setRoles((rs) => (checked ? [...rs, r] : rs.filter((x) => x !== r)))}
              />
            );
            return lockedAdmin && reason ? (
              <Tooltip key={r} content={reason} relationship="description">
                <div>{box}</div>
              </Tooltip>
            ) : (
              box
            );
          })}
        </fieldset>
        <Text className={styles.roleHint}>
          Collector is for service accounts with API tokens and is never given to a person.
        </Text>
      </DrawerBody>
      <DrawerFooter>
        <Button
          type="submit"
          appearance="primary"
          disabled={busy || roles.length === 0}
          {...(busy ? { icon: <Spinner size="tiny" /> } : {})}
        >
          Save
        </Button>
        <Button onClick={onClose}>Cancel</Button>
      </DrawerFooter>
    </form>
  );
}

function RoleCheckbox({
  role,
  checked,
  disabled = false,
  onChange,
}: {
  role: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}) {
  const styles = useStyles();
  return (
    <Checkbox
      checked={checked}
      disabled={disabled}
      onChange={(_, d) => onChange(d.checked === true)}
      label={
        <span className={styles.roleOption}>
          <b>{role}</b>
          <span className={styles.roleHint}>{roleText[role]}</span>
        </span>
      }
    />
  );
}

/** The shown-once link and code (DEC-038 items 2 and 8): the code has no copy button on purpose. */
function ShownOnce({ panel, onDone }: { panel: Extract<Panel, { kind: 'secret' }>; onDone: () => void }) {
  const styles = useStyles();
  const [sent, setSent] = useState(false);
  const path = panel.purpose === 'invitation' ? 'invite' : 'reset';
  const url = `${window.location.origin}/${path}#t=${panel.link.token}`;
  const first = panel.user.displayName.split(' ')[0] ?? panel.user.displayName;
  const expires = formatUtc(panel.link.expiresAt);
  return (
    <>
      <DrawerBody className={styles.panelBody}>
        <MessageBar intent="warning">
          <MessageBarBody>
            <b>Shown only once.</b>{' '}
            {panel.purpose === 'invitation'
              ? 'Goldmark keeps only a hash of the link and the code.'
              : `${first}’s password and roles are kept; they use these to set up two-factor again.`}
          </MessageBarBody>
        </MessageBar>
        <section className={styles.section} aria-labelledby="link-heading">
          <h3 id="link-heading" className={styles.h3}>
            1 · Send this link by chat or email
          </h3>
          <div className={styles.secretBox}>
            <span className={styles.link}>{url}</span>
            <CopyButton value={url} label="Copy link" />
          </div>
        </section>
        <section className={styles.section} aria-labelledby="code-heading">
          <h3 id="code-heading" className={styles.h3}>
            2 · Tell them this code by phone or in person
          </h3>
          <div className={styles.secretBox}>
            <span className={styles.bigCode} aria-label={panel.link.code.replace('-', '').split('').join(', ')}>
              {panel.link.code}
            </span>
          </div>
          <Text className={styles.roleHint}>
            No copy button on purpose: the link doesn’t work without this code, so never send them together. Codes avoid
            look-alike characters (0/O, 1/I/L).
          </Text>
        </section>
        <Text>
          Expires in {panel.link.expiryHours} {panel.link.expiryHours === 1 ? 'hour' : 'hours'}, at {expires} UTC.{' '}
          {panel.link.codeAttempts} wrong {panel.link.codeAttempts === 1 ? 'code ends' : 'codes end'} the link.
        </Text>
        <Checkbox
          label="I sent the link and told them the code by phone or in person"
          checked={sent}
          onChange={(_, d) => setSent(d.checked === true)}
        />
      </DrawerBody>
      <DrawerFooter>
        <Button appearance="primary" disabled={!sent} onClick={onDone}>
          Done
        </Button>
      </DrawerFooter>
    </>
  );
}

function validation(error: string | undefined) {
  return error ? { validationState: 'error' as const, validationMessage: error } : {};
}

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
  MessageBarActions,
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
import { approvalsChanged, useWide } from '../shell/AppShell';
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
  dialogBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  paragraph: { margin: 0 },
  rolesLine: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    alignItems: 'center',
    flexWrap: 'wrap',
    color: tokens.colorNeutralForeground2,
  },
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

type LinkPurpose = 'invitation' | 'two-factor-reset' | 'password-reset' | 'combined-reset';

interface OpenLink {
  purpose: LinkPurpose;
  /** When the link, or a request still awaiting approval, ends. */
  expiresAt: string;
  expired: boolean;
  /** Under dual control: no code yet until another Admin approves (DEC-042). */
  awaitingApproval: boolean;
  requestedBy: { id: string; displayName: string; askedAt: string } | null;
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
    /** An administrator reset the password, and the person hasn't chosen a new one yet. */
    passwordResetPending: boolean;
  };
  lastSignInAt: string | null;
  createdAt: string;
  eTag: string;
}

interface IssuedLink {
  token: string;
  /** Null while the request awaits another Admin's approval: the approving Admin gets the code (DEC-042). */
  code: string | null;
  expiresAt: string;
  /** The configured limits (Goldmark:Auth:LinkExpiryHours, LinkCodeAttempts). */
  expiryHours: number;
  codeAttempts: number;
  awaitingApproval: boolean;
  /** No other Admin could approve, so the one who asked got both values. */
  onlyAdmin: boolean;
}

/** The code an approving Admin gets, shown once (DEC-042). */
interface ApprovedCode {
  code: string;
  expiresAt: string;
  expiryHours: number;
  codeAttempts: number;
}

type Panel =
  | { kind: 'add' }
  | { kind: 'roles'; user: UserRow }
  | { kind: 'secret'; purpose: LinkPurpose; user: UserRow; link: IssuedLink }
  | { kind: 'code'; user: UserRow; code: ApprovedCode; askedBy: string };

type Confirm = {
  action:
    | 'lock'
    | 'unlock'
    | 'disable'
    | 'enable'
    | 'reset'
    | 'revoke'
    | 'reissue'
    | 'password-reset'
    | 'approve'
    | 'decline'
    | 'cancel-request'
    | 'ask-again';
  user: UserRow;
  /** Admins other than you and the person who could approve a request about them. */
  approvers: number;
};

const accountFilters = [
  'All',
  'Active',
  'Locked',
  'Disabled',
  'Invitation pending',
  'Invitation expired',
  'Awaiting approval',
] as const;
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

/** Your own request about this person that ended without approval: you can ask again (DEC-042 item 9). */
function endedRequest(user: UserRow, me: string | undefined): OpenLink | null {
  const link = user.account.link;
  return link?.awaitingApproval && link.expired && link.requestedBy?.id === me ? link : null;
}

/** A request about this person that waits for another Admin's approval and hasn't ended (DEC-042). */
function waitingRequest(user: UserRow): OpenLink | null {
  const link = user.account.link;
  return link?.awaitingApproval && !link.expired ? link : null;
}

function firstName(name: string): string {
  return name.split(' ')[0] ?? name;
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
      // The nav's approvals count follows what this page now shows (DEC-042 item 9).
      window.dispatchEvent(new Event(approvalsChanged));
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

  const admins = useMemo(
    () => (users ?? []).filter((u) => u.roles.includes('Admin') && u.account.state !== 'disabled' && !isPending(u)),
    [users],
  );
  const enabledAdmins = admins.length;
  /** Who could approve a request you make about this person: another enabled Admin, not them (DEC-042). */
  const approversFor = useCallback(
    (target: string | null) => admins.filter((a) => a.id !== me && a.id !== target).length,
    [admins, me],
  );
  // Requests you could approve: not yours, not about you, not for a disabled account.
  const approvals = (users ?? []).filter((u) => {
    const request = waitingRequest(u);
    return request !== null && u.id !== me && u.account.state !== 'disabled' && request.requestedBy?.id !== me;
  }).length;

  const visible = useMemo(() => {
    const text = filter.trim().toLowerCase();
    return (users ?? []).filter(
      (u) =>
        (text === '' || u.displayName.toLowerCase().includes(text) || u.userName.toLowerCase().includes(text)) &&
        (role === 'All' || u.roles.includes(role)) &&
        (account === 'All' ||
          (account === 'Awaiting approval' ? waitingRequest(u) !== null : u.account.state === stateOfFilter[account])),
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
        renderCell: (u) => <AccountState user={u} me={me} />,
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
            ownRequest={waitingRequest(u)?.requestedBy?.id === me}
            ended={u.id !== me && u.account.state !== 'disabled' && endedRequest(u, me) !== null}
            lastAdmin={u.roles.includes('Admin') && enabledAdmins <= 1}
            onEditRoles={() => setPanel({ kind: 'roles', user: u })}
            onConfirm={(action) => setConfirm({ action, user: u, approvers: approversFor(u.id) })}
          />
        ),
      }),
    ],
    [styles, me, enabledAdmins, approversFor],
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
      dualControl={approversFor(null) > 0}
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
      {approvals > 0 ? (
        // Goldmark sends no email, so every other Admin sees waiting requests here (DEC-042 item 9).
        <MessageBar intent="warning">
          <MessageBarBody>
            {approvals === 1
              ? '1 request needs another Admin’s approval.'
              : `${approvals} requests need another Admin’s approval.`}
          </MessageBarBody>
          <MessageBarActions>
            <Button size="small" onClick={() => setAccount('Awaiting approval')}>
              Show them
            </Button>
          </MessageBarActions>
        </MessageBar>
      ) : null}
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
      {confirm?.action === 'password-reset' || confirm?.action === 'ask-again' ? (
        <ResetPasswordDialog
          user={confirm.user}
          approvers={confirm.approvers}
          initialAlsoTwoFactor={confirm.action === 'ask-again'}
          onCancel={() => setConfirm(null)}
          onDone={(link, alsoTwoFactor) => {
            setConfirm(null);
            void load();
            setPanel({
              kind: 'secret',
              purpose: alsoTwoFactor ? 'combined-reset' : 'password-reset',
              user: confirm.user,
              link,
            });
          }}
        />
      ) : confirm ? (
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
            } else if (result.code) {
              setPanel({
                kind: 'code',
                user: confirm.user,
                code: result.code,
                askedBy: confirm.user.account.link?.requestedBy?.displayName ?? 'The Admin who asked',
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

function AccountState({ user, me }: { user: UserRow; me: string | undefined }) {
  const { state, lockedUntil, link, passwordResetPending } = user.account;
  const request = waitingRequest(user) ?? endedRequest(user, me);
  if (request && state !== 'disabled') {
    // Nothing changed on the account yet: only the request shows (DEC-042); the one who asked sees
    // its time left, then that it ended (item 9), everyone else who asked.
    const kind = request.purpose === 'invitation' ? 'Invitation' : 'Password reset';
    const asker = request.requestedBy;
    return (
      <Badge appearance="tint" color="informative">
        {request.expired
          ? `${kind} approval request ended`
          : asker?.id === me
            ? `${kind} awaiting approval · ends in ${formatRemaining(request.expiresAt)}`
            : `${kind} awaiting approval${asker ? ` · asked by ${asker.displayName}` : ''}`}
      </Badge>
    );
  }
  switch (state) {
    case 'active':
      return <Text>{activeText(link, passwordResetPending)}</Text>;
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

function activeText(link: OpenLink | null, passwordResetPending: boolean): string {
  if (link && !link.awaitingApproval && (link.purpose === 'password-reset' || link.purpose === 'combined-reset')) {
    return link.expired
      ? 'Active · password reset link expired'
      : `Active · password reset link expires in ${formatRemaining(link.expiresAt)}`;
  }
  if (passwordResetPending) {
    // The link expired or stopped working: the person can't sign in until an Admin resets again.
    return 'Active · password reset link expired';
  }
  return link?.purpose === 'two-factor-reset' && !link.expired
    ? `Active · two-factor reset link expires in ${formatRemaining(link.expiresAt)}`
    : 'Active';
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
  ownRequest,
  ended,
  lastAdmin,
  onEditRoles,
  onConfirm,
}: {
  user: UserRow;
  self: boolean;
  ownRequest: boolean;
  /** Your own request about them ended without approval: "Ask again…" comes first (DEC-042 item 9). */
  ended: boolean;
  lastAdmin: boolean;
  onEditRoles: () => void;
  onConfirm: (action: Confirm['action']) => void;
}) {
  const pending = isPending(user);
  const locked = user.account.state === 'locked';
  const disabled = user.account.state === 'disabled';
  const request = self ? null : waitingRequest(user);
  const waiting = request ? 'A request for this person is waiting for another Admin’s approval.' : null;
  // Under dual control (DEC-042): another Admin approves or declines; the one who asked can cancel.
  const requestItems = request ? (
    <>
      <GuardedItem
        reason={ownRequest ? 'Another Admin approves requests you make.' : null}
        onClick={() => onConfirm('approve')}
      >
        Approve…
      </GuardedItem>
      {ownRequest ? (
        <MenuItem onClick={() => onConfirm('cancel-request')}>Cancel request…</MenuItem>
      ) : (
        <MenuItem onClick={() => onConfirm('decline')}>Decline…</MenuItem>
      )}
    </>
  ) : null;
  return (
    <Menu>
      <MenuTrigger disableButtonEnhancement>
        <Button appearance="subtle" icon={<MoreHorizontalRegular />} aria-label={`Actions for ${user.displayName}`} />
      </MenuTrigger>
      <MenuPopover>
        <MenuList>
          {pending && requestItems ? (
            requestItems
          ) : pending && ended ? (
            <>
              <MenuItem onClick={() => onConfirm('reissue')}>Ask again…</MenuItem>
              <MenuItem onClick={() => onConfirm('revoke')}>Revoke invitation…</MenuItem>
            </>
          ) : pending ? (
            <>
              <MenuItem onClick={() => onConfirm('reissue')}>Copy new link…</MenuItem>
              <MenuItem onClick={() => onConfirm('revoke')}>Revoke invitation…</MenuItem>
            </>
          ) : (
            <>
              {requestItems ? (
                <>
                  {requestItems}
                  <MenuDivider />
                </>
              ) : ended ? (
                <>
                  <MenuItem onClick={() => onConfirm('ask-again')}>Ask again…</MenuItem>
                  <MenuDivider />
                </>
              ) : null}
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
              {ended ? null : (
                // After an ended request, "Ask again…" above takes its place (the addendum board).
                <GuardedItem
                  reason={
                    self
                      ? 'You can’t reset your own password here. Use Change password in your account menu.'
                      : disabled
                        ? 'Enable the account first. A disabled account can’t use the link.'
                        : user.roles.includes('Admin') && !user.twoFactorEnrolled
                          ? 'This Admin has no authenticator to confirm a new password with. Remove Admin first, or let them finish setting up two-factor.'
                          : waiting
                  }
                  onClick={() => onConfirm('password-reset')}
                >
                  Reset password…
                </GuardedItem>
              )}
              <GuardedItem
                reason={
                  self
                    ? 'You can’t reset your own two-factor here. Use your two-factor page.'
                    : user.account.passwordResetPending
                      ? 'Their password was reset. Use Reset password with “Also reset two-factor” instead.'
                      : waiting
                }
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

interface ConfirmText {
  title: string;
  body: string;
  /** A second paragraph. */
  more?: string;
  /** "Roles they get", for approving an invitation. */
  roles?: string[];
  button: string;
  /** The dismiss button's label, when "Cancel" would be ambiguous. */
  dismiss?: string;
  destructive: boolean;
}

function confirmText(confirm: Confirm): ConfirmText {
  const name = confirm.user.displayName;
  const first = firstName(name);
  const request = confirm.user.account.link;
  const invitation = request?.purpose === 'invitation';
  const asker = request?.requestedBy?.displayName ?? 'The Admin who asked';
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
    case 'approve': {
      const at = request?.requestedBy ? ` at ${formatUtcTime(request.requestedBy.askedAt)} UTC` : '';
      return {
        title: invitation
          ? `Approve the invitation for ${name}?`
          : `Approve the password and two-factor reset for ${name}?`,
        body: `${asker} asked for this${at} and has the link. You get the code and tell ${first} by phone or in person.`,
        more: invitation
          ? `Check that you’re talking to ${first} before you do.`
          : `When you approve, ${first} is signed out everywhere, their current password stops working, and their authenticator and recovery codes are removed. Check that you’re talking to ${first} before you do.`,
        ...(invitation ? { roles: confirm.user.roles } : {}),
        button: 'Approve and show the code',
        destructive: false,
      };
    }
    case 'decline':
      return {
        title: invitation
          ? `Decline the invitation for ${name}?`
          : `Decline the password and two-factor reset for ${name}?`,
        body: `The link stops working and ${asker} sees that you declined.`,
        button: 'Decline',
        destructive: false,
      };
    case 'cancel-request':
      return {
        title: invitation
          ? `Cancel the request for ${name}’s invitation?`
          : `Cancel the password and two-factor reset for ${name}?`,
        body: invitation
          ? 'The link stops working and the invitation is removed.'
          : `The link stops working. Nothing changed on ${first}’s account.`,
        button: 'Cancel request',
        dismiss: 'Keep request',
        destructive: false,
      };
    default:
      return confirm.approvers > 0
        ? {
            title: `New link for ${name}?`,
            body: 'This makes a new link, and the old one stops working. Another Admin approves it and gets the code.',
            button: 'Ask for approval',
            destructive: false,
          }
        : {
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
  onDone: (result: { message?: string; link?: IssuedLink; code?: ApprovedCode }) => void;
}) {
  const styles = useStyles();
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
        case 'approve': {
          const { data } = await api<{ code: ApprovedCode }>('POST', `/api/v1/users/${id}/request/approve`, {});
          onDone({ code: data.code });
          return;
        }
        case 'decline':
          await api('POST', `/api/v1/users/${id}/request/decline`, {});
          onDone({ message: 'Request declined' });
          return;
        case 'cancel-request':
          await api('POST', `/api/v1/users/${id}/request/cancel`, {});
          onDone({ message: 'Request cancelled' });
          return;
        case 'password-reset':
        case 'ask-again':
          // ResetPasswordDialog handles these.
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
          <DialogContent className={styles.dialogBody}>
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
            {text.more ? (
              <Text as="p" className={styles.paragraph}>
                {text.more}
              </Text>
            ) : null}
            {text.roles ? (
              <div className={styles.rolesLine}>
                <Text>Roles they get:</Text>
                {text.roles.map((r) => (
                  <Badge key={r} appearance="tint" color="subtle">
                    {r}
                  </Badge>
                ))}
              </div>
            ) : null}
          </DialogContent>
          <DialogActions>
            <Button appearance="secondary" onClick={onCancel}>
              {text.dismiss ?? 'Cancel'}
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

/**
 * "Reset password…" (DEC-042): password only, or with "Also reset two-factor" for a person who lost
 * their authenticator too (never for an Admin). With two-factor and another Admin able to approve,
 * nothing changes until they do.
 */
function ResetPasswordDialog({
  user,
  approvers,
  initialAlsoTwoFactor,
  onCancel,
  onDone,
}: {
  user: UserRow;
  approvers: number;
  /** "Ask again…" after an ended request starts with "Also reset two-factor" ticked. */
  initialAlsoTwoFactor: boolean;
  onCancel: () => void;
  onDone: (link: IssuedLink, alsoTwoFactor: boolean) => void;
}) {
  const styles = useStyles();
  // Without an authenticator the password-only reset could never be confirmed (DEC-042 item 3), so the
  // reset includes two-factor; an Admin without one doesn't reach this dialog (the menu says why).
  const noAuthenticator = !user.twoFactorEnrolled && !user.roles.includes('Admin');
  const [alsoTwoFactor, setAlsoTwoFactor] = useState(
    (initialAlsoTwoFactor || noAuthenticator) && !user.roles.includes('Admin'),
  );
  const [busy, setBusy] = useState(false);
  const [problem, setProblem] = useState<ApiError | null>(null);
  const first = firstName(user.displayName);
  const admin = user.roles.includes('Admin');
  const dual = alsoTwoFactor && approvers > 0;
  const paragraphs = !alsoTwoFactor
    ? [
        `This signs ${first} out everywhere, and their current password stops working now.`,
        `Next you get a link and a code. ${first} needs both, and their authenticator app, to choose a new password before the link expires. Their two-factor and roles stay.`,
      ]
    : dual
      ? [
          `Another Admin must approve this. When they do, ${first} is signed out everywhere, their current password stops working, and their authenticator and recovery codes are removed.`,
          `You get the link now; the approving Admin gets the code. ${first} needs both to choose a new password and set up two-factor again. Their roles stay.`,
        ]
      : [
          `This signs ${first} out everywhere, their current password stops working now, and their authenticator and recovery codes are removed.`,
          `Next you get a link and a code. ${first} needs both to choose a new password and set up two-factor again before the link expires. Their roles stay.`,
        ];

  async function act() {
    setBusy(true);
    setProblem(null);
    try {
      const { data } = await api<{ link: IssuedLink }>('POST', `/api/v1/users/${user.id}/password-reset`, {
        alsoTwoFactor,
      });
      onDone(data.link, alsoTwoFactor);
    } catch (error) {
      if (error instanceof ApiError) setProblem(error);
      else throw error;
    } finally {
      setBusy(false);
    }
  }

  const box = (
    <Checkbox
      label={`Also reset two-factor (${first} lost their authenticator)`}
      checked={alsoTwoFactor}
      disabled={admin || noAuthenticator || busy}
      onChange={(_, d) => setAlsoTwoFactor(d.checked === true)}
    />
  );
  return (
    <Dialog open modalType="alert" onOpenChange={(_, d) => (d.open ? undefined : onCancel())}>
      <DialogSurface>
        <DialogBody>
          <DialogTitle>{`Reset the password for ${user.displayName}?`}</DialogTitle>
          <DialogContent className={styles.dialogBody}>
            {problem ? (
              problem.isUnavailable ? (
                <ErrorBar error={problem} onRetry={() => void act()} />
              ) : (
                <MessageBar intent="error">
                  <MessageBarBody>{problem.message}</MessageBarBody>
                </MessageBar>
              )
            ) : null}
            {paragraphs.map((text) => (
              <Text as="p" key={text} className={styles.paragraph}>
                {text}
              </Text>
            ))}
            {admin || noAuthenticator ? (
              // A link and code alone never yield an Admin session (DEC-042 item 7).
              <Tooltip
                content={
                  admin
                    ? 'Remove Admin first; add it again after their first sign-in.'
                    : `${first} has no authenticator, so the reset includes two-factor.`
                }
                relationship="description"
              >
                <div>{box}</div>
              </Tooltip>
            ) : (
              box
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
              {dual ? 'Ask for approval' : 'Reset password'}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function UserPanel({
  panel,
  dualControl,
  onClose,
  onCreated,
  onSaved,
  onDone,
}: {
  panel: Panel;
  /** Another Admin could approve an invitation you create (DEC-042). */
  dualControl: boolean;
  onClose: () => void;
  onCreated: (user: UserRow, link: IssuedLink) => void;
  onSaved: (user: UserRow) => void;
  onDone: () => void;
}) {
  const name = panel.kind === 'add' ? '' : panel.user.displayName;
  const title =
    panel.kind === 'add'
      ? 'Add user'
      : panel.kind === 'roles'
        ? `Roles for ${name}`
        : panel.kind === 'code'
          ? `Code for ${name}`
          : {
              invitation: `Invitation for ${name}`,
              'two-factor-reset': `New set-up link for ${name}`,
              'password-reset': `Password reset link for ${name}`,
              'combined-reset': `Set-up link for ${name}`,
            }[panel.purpose];
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
      {panel.kind === 'add' ? <AddUser dualControl={dualControl} onClose={onClose} onCreated={onCreated} /> : null}
      {panel.kind === 'roles' ? <EditRoles user={panel.user} onClose={onClose} onSaved={onSaved} /> : null}
      {panel.kind === 'secret' ? <ShownOnce panel={panel} onDone={onDone} /> : null}
      {panel.kind === 'code' ? <ApprovedCodePanel panel={panel} onDone={onDone} /> : null}
    </>
  );
}

function AddUser({
  dualControl,
  onClose,
  onCreated,
}: {
  dualControl: boolean;
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
        {problem ? (
          problem.isUnavailable ? (
            <ErrorBar error={problem} onRetry={() => setProblem(null)} />
          ) : (
            <MessageBar intent="error">
              <MessageBarBody>{problem.message}</MessageBarBody>
            </MessageBar>
          )
        ) : null}
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
          {dualControl ? 'Create and ask for approval' : 'Create invitation'}
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
  // Its link may need none of their factors, and an Admin is never reset that way (DEC-042 item 7).
  const resetPending = current.account.passwordResetPending;

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
              r === 'Admin' &&
              ((self && roles.includes('Admin')) || ((neverSignedIn || resetPending) && !roles.includes('Admin')));
            const reason =
              r !== 'Admin'
                ? null
                : self
                  ? 'You can’t remove Admin from yourself.'
                  : neverSignedIn
                    ? 'Add Admin after their first sign-in.'
                    : resetPending
                      ? 'Add Admin after they finish their password reset.'
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

/**
 * The shown-once link and code (DEC-038 items 2 and 8): the code has no copy button on purpose. Under
 * dual control the one who asks gets the link only, and the approving Admin the code (DEC-042).
 */
function ShownOnce({ panel, onDone }: { panel: Extract<Panel, { kind: 'secret' }>; onDone: () => void }) {
  const styles = useStyles();
  const [sent, setSent] = useState(false);
  const { purpose, link } = panel;
  const path = purpose === 'invitation' ? 'invite' : purpose === 'two-factor-reset' ? 'reset' : 'password-reset';
  const url = `${window.location.origin}/${path}#t=${link.token}`;
  const first = firstName(panel.user.displayName);
  const expires = formatUtc(link.expiresAt);
  const hours = `${link.expiryHours} ${link.expiryHours === 1 ? 'hour' : 'hours'}`;
  const waiting = link.code === null;
  const intro = waiting
    ? `Send this link now. Another Admin approves the ${purpose === 'invitation' ? 'invitation' : 'reset'} and tells ${first} the code by phone or in person. The link works only after that.`
    : {
        invitation: 'Goldmark keeps only a hash of the link and the code.',
        'two-factor-reset': `${first}’s password and roles are kept; they use these to set up two-factor again.`,
        'password-reset': `${first}’s two-factor and roles are kept; they use these to choose a new password.`,
        'combined-reset': `${first}’s roles are kept; they use these to choose a new password and set up two-factor again.`,
      }[purpose];
  return (
    <>
      <DrawerBody className={styles.panelBody}>
        <MessageBar intent="warning">
          <MessageBarBody>
            <b>Shown only once.</b> {intro}
          </MessageBarBody>
        </MessageBar>
        {link.onlyAdmin ? (
          <MessageBar intent="info">
            <MessageBarBody>
              You’re the only Admin, so you get both the link and the code. With a second Admin, another Admin approves
              and tells them the code.
            </MessageBarBody>
          </MessageBar>
        ) : null}
        <section className={styles.section} aria-labelledby="link-heading">
          <h3 id="link-heading" className={styles.h3}>
            {waiting ? 'Send this link by chat or email' : '1 · Send this link by chat or email'}
          </h3>
          <div className={styles.secretBox}>
            <span className={styles.link}>{url}</span>
            <CopyButton value={url} label="Copy link" />
          </div>
        </section>
        {link.code ? (
          <section className={styles.section} aria-labelledby="code-heading">
            <h3 id="code-heading" className={styles.h3}>
              2 · Tell them this code by phone or in person
            </h3>
            <SpokenCode code={link.code} />
            <Text className={styles.roleHint}>
              No copy button on purpose: the link doesn’t work without this code, so never send them together. Codes
              avoid look-alike characters (0/O, 1/I/L).
            </Text>
          </section>
        ) : null}
        <Text>
          {waiting
            ? purpose === 'invitation'
              ? `Waiting for approval. The request ends if nobody approves it within ${hours}.`
              : `Waiting for approval. Nothing changes on ${first}’s account until then; the request ends if nobody approves it within ${hours}.`
            : `Expires in ${hours}, at ${expires} UTC. ${link.codeAttempts} wrong ${link.codeAttempts === 1 ? 'code ends' : 'codes end'} the link.`}
        </Text>
        <Checkbox
          label={waiting ? 'I sent the link' : 'I sent the link and told them the code by phone or in person'}
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

/** The approving Admin's code, shown once; the one who asked has the link (DEC-042). */
function ApprovedCodePanel({ panel, onDone }: { panel: Extract<Panel, { kind: 'code' }>; onDone: () => void }) {
  const styles = useStyles();
  const [told, setTold] = useState(false);
  const first = firstName(panel.user.displayName);
  const { code } = panel;
  return (
    <>
      <DrawerBody className={styles.panelBody}>
        <MessageBar intent="warning">
          <MessageBarBody>
            <b>Shown only once.</b> Tell {first} this code by phone or in person. {panel.askedBy} has sent the link.
          </MessageBarBody>
        </MessageBar>
        <section className={styles.section} aria-labelledby="code-heading">
          <h3 id="code-heading" className={styles.h3}>
            Tell them this code by phone or in person
          </h3>
          <SpokenCode code={code.code} />
          <Text className={styles.roleHint}>
            No copy button on purpose: say it aloud. Codes avoid look-alike characters (0/O, 1/I/L).
          </Text>
        </section>
        <Text>
          The link works until {formatUtc(code.expiresAt)} UTC. {code.codeAttempts} wrong{' '}
          {code.codeAttempts === 1 ? 'code ends' : 'codes end'} it.
        </Text>
        <Checkbox
          label="I told them the code by phone or in person"
          checked={told}
          onChange={(_, d) => setTold(d.checked === true)}
        />
      </DrawerBody>
      <DrawerFooter>
        <Button appearance="primary" disabled={!told} onClick={onDone}>
          Done
        </Button>
      </DrawerFooter>
    </>
  );
}

function SpokenCode({ code }: { code: string }) {
  const styles = useStyles();
  return (
    <div className={styles.secretBox}>
      <span className={styles.bigCode} aria-label={code.replace('-', '').split('').join(', ')}>
        {code}
      </span>
    </div>
  );
}

function validation(error: string | undefined) {
  return error ? { validationState: 'error' as const, validationMessage: error } : {};
}

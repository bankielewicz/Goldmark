import {
  Button,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Link,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  SearchBox,
  Skeleton,
  SkeletonItem,
  Spinner,
  Tab,
  TabList,
  Text,
  Toast,
  ToastTitle,
  Toaster,
  Tooltip,
  makeStyles,
  tokens,
  useId,
  useToastController,
} from '@fluentui/react-components';
import {
  ArchiveRegular,
  ArrowCounterclockwiseRegular,
  CheckmarkCircleRegular,
  EditRegular,
  MoreHorizontalRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useState } from 'react';

import { api, ApiError } from '../api';
import { formatAgo, formatUtc, formatUtcTime } from '../format';
import { followLink, navigate, useLocation } from '../router';
import { useSession } from '../session';
import { useWide } from '../shell/AppShell';
import { PageHeader } from '../shell/PageHeader';
import { CiPanel } from '../ui/CiPanel';
import { CopyButton, ErrorBar } from '../ui/common';
import {
  asApiError,
  canEditCis,
  isCiId,
  LifecycleBadge,
  takePendingToast,
  TypeBadge,
  TypeIcon,
  useCiTypes,
  valueText,
  type AttributeDef,
  type CiDetail,
  type CiType,
} from '../ui/inventory';

const useStyles = makeStyles({
  changed: { color: tokens.colorNeutralForeground3 },
  retiredTitle: { color: tokens.colorNeutralForeground2 },
  panel: {
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    padding: tokens.spacingHorizontalXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXL,
  },
  group: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  groupTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  grid: {
    margin: 0,
    display: 'grid',
    gridTemplateColumns: '180px minmax(0, 1fr) 180px minmax(0, 1fr)',
    gap: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    alignItems: 'center',
    '@media (max-width: 1099px)': { gridTemplateColumns: '180px minmax(0, 1fr)' },
    '@media (max-width: 599px)': { gridTemplateColumns: 'minmax(0, 1fr)', rowGap: tokens.spacingVerticalXXS },
  },
  term: {
    color: tokens.colorNeutralForeground3,
    fontWeight: tokens.fontWeightSemibold,
    '@media (max-width: 599px)': { marginTop: tokens.spacingVerticalS },
  },
  value: {
    margin: 0,
    minWidth: 0,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    flexWrap: 'wrap',
  },
  notSet: { color: tokens.colorNeutralForeground3 },
  contents: { display: 'contents' },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200, overflowWrap: 'anywhere' },
  link: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    fontWeight: tokens.fontWeightSemibold,
  },
  linkType: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  noAttributes: {
    paddingTop: tokens.spacingVerticalL,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    fontWeight: tokens.fontWeightSemibold,
  },
  notFound: {
    maxWidth: '720px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    padding: tokens.spacingHorizontalXL,
  },
  search: { maxWidth: '480px' },
});

type Change = 'retire' | 'restore';

/**
 * Screen 5, CI detail, Attributes tab (US-11, US-53 `/ci/{id}`, `/ci/{id}?tab=attributes`; DEC-044):
 * the CI's general fields, its type's attributes and its custom attributes; Edit, Retire… (confirmed)
 * and Restore for Admins and Editors. Later slices add tabs (relationships, history, identifiers,
 * impact). An unknown ID, or one that isn't a CI ID, gets the friendly not-found page.
 */
export function CiPage({ id }: { id: string }) {
  if (!isCiId(id)) return <CiNotFound id={id} malformed />;
  return <CiView id={id} />;
}

function CiView({ id }: { id: string }) {
  const styles = useStyles();
  const session = useSession();
  const canEdit = canEditCis(session.user);
  const wide = useWide();
  const { search } = useLocation();
  const { types } = useCiTypes();
  const toasterId = useId('ci-toaster');
  const { dispatchToast } = useToastController(toasterId);
  const [ci, setCi] = useState<CiDetail | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [missing, setMissing] = useState(false);
  const [editing, setEditing] = useState(false);
  const [confirmRetire, setConfirmRetire] = useState(false);
  const [busy, setBusy] = useState<Change | null>(null);
  const [changeFailure, setChangeFailure] = useState<{ change: Change; error: ApiError } | null>(null);
  const [changedMeanwhile, setChangedMeanwhile] = useState<CiDetail | null>(null);

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

  const load = useCallback(async () => {
    setFailure(null);
    setMissing(false);
    try {
      const { data } = await api<CiDetail>('GET', `/api/v1/cis/${id}`);
      setCi(data);
    } catch (error) {
      const problem = asApiError(error);
      if (problem.status === 404) setMissing(true);
      else setFailure(problem);
    }
  }, [id]);

  useEffect(() => {
    setCi(null);
    void load();
  }, [load]);

  useEffect(() => {
    const pending = takePendingToast();
    if (pending) toast(pending);
  }, [toast]);

  // Only the Attributes tab exists in slice 5: any other ?tab= shows it and the address is corrected.
  const tab = search.get('tab');
  useEffect(() => {
    if (tab !== null && tab !== 'attributes') navigate(`/ci/${id}`, { replace: true });
  }, [tab, id]);

  const change = async (kind: Change) => {
    if (!ci) return;
    setConfirmRetire(false);
    setBusy(kind);
    setChangeFailure(null);
    try {
      const { data } = await api<CiDetail>('POST', `/api/v1/cis/${ci.id}/${kind}`, {}, { ifMatch: `"${ci.eTag}"` });
      setCi(data);
      toast(`${data.name} ${kind === 'retire' ? 'retired' : 'restored'}`);
    } catch (error) {
      const problem = asApiError(error);
      if (problem.status === 412 || problem.status === 409) {
        const { data } = await api<CiDetail>('GET', `/api/v1/cis/${ci.id}`).catch(() => ({ data: null }));
        if (data) setChangedMeanwhile(data);
        else setChangeFailure({ change: kind, error: problem });
      } else {
        setChangeFailure({ change: kind, error: problem });
      }
    } finally {
      setBusy(null);
    }
  };

  if (missing) return <CiNotFound id={id} malformed={false} />;
  if (failure && !ci) {
    return (
      <>
        <PageHeader crumbs={[{ label: 'Search', path: '/search' }, { label: 'CI' }]} title="CI" />
        <ErrorBar
          error={failure}
          message={
            failure.status === 0
              ? 'Couldn’t load this CI. Check your connection, then try again.'
              : 'Couldn’t load this CI: something went wrong on the server.'
          }
          onRetry={() => void load()}
        />
      </>
    );
  }
  if (!ci) return <CiSkeleton />;

  const retired = ci.lifecycleState === 'Retired';
  const type = types?.find((t) => t.name === ci.type);
  const who = ci.changedBy?.displayName ?? (ci.changedBy ? 'a removed account' : 'System');
  const actions = canEdit
    ? wide
      ? retired
        ? [
            <Button
              key="restore"
              icon={<ArrowCounterclockwiseRegular />}
              disabled={busy !== null}
              onClick={() => void change('restore')}
            >
              {busy === 'restore' ? <Spinner size="tiny" aria-label="Restoring" /> : null}
              Restore
            </Button>,
          ]
        : [
            <Button
              key="edit"
              icon={<EditRegular />}
              aria-haspopup="dialog"
              disabled={!types}
              onClick={() => setEditing(true)}
            >
              Edit
            </Button>,
            <Button
              key="retire"
              icon={<ArchiveRegular />}
              aria-haspopup="dialog"
              disabled={busy !== null}
              onClick={() => setConfirmRetire(true)}
            >
              {busy === 'retire' ? <Spinner size="tiny" aria-label="Retiring" /> : null}
              Retire…
            </Button>,
          ]
      : [
          <Menu key="more">
            <MenuTrigger disableButtonEnhancement>
              <Tooltip content="More actions" relationship="label">
                <Button icon={<MoreHorizontalRegular />} disabled={busy !== null} />
              </Tooltip>
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {retired ? (
                  <MenuItem icon={<ArrowCounterclockwiseRegular />} onClick={() => void change('restore')}>
                    Restore
                  </MenuItem>
                ) : (
                  <>
                    <MenuItem icon={<EditRegular />} disabled={!types} onClick={() => setEditing(true)}>
                      Edit
                    </MenuItem>
                    <MenuItem icon={<ArchiveRegular />} onClick={() => setConfirmRetire(true)}>
                      Retire…
                    </MenuItem>
                  </>
                )}
              </MenuList>
            </MenuPopover>
          </Menu>,
        ]
    : [];

  return (
    <>
      <PageHeader
        crumbs={[
          { label: 'Search', path: '/search' },
          { label: ci.type, path: `/search?type=${encodeURIComponent(ci.type)}` },
          { label: ci.name },
        ]}
        title={ci.name}
        {...(retired ? { titleClassName: styles.retiredTitle } : {})}
        badges={
          <>
            <TypeBadge type={ci.type} types={types} />
            <LifecycleBadge state={ci.lifecycleState} />
          </>
        }
        {...(actions.length > 0 ? { actions: <>{actions}</> } : {})}
      />
      <Tooltip content={`${formatUtc(ci.changedAt)} UTC`} relationship="description">
        <Text className={styles.changed}>
          Changed {formatAgo(ci.changedAt)} by {who}
        </Text>
      </Tooltip>

      {retired ? (
        <MessageBar intent="info">
          <MessageBarBody>
            Retired on {formatUtc(ci.changedAt).slice(0, 10)} by {who}. It’s hidden from search unless Show retired is
            on.
          </MessageBarBody>
          {canEdit ? (
            <MessageBarActions>
              <Button size="small" disabled={busy !== null} onClick={() => void change('restore')}>
                Restore
              </Button>
            </MessageBarActions>
          ) : null}
        </MessageBar>
      ) : null}
      {changeFailure ? (
        <ErrorBar error={changeFailure.error} onRetry={() => void change(changeFailure.change)} />
      ) : null}

      <TabList selectedValue="attributes" aria-label="CI sections">
        <Tab value="attributes" id="tab-attributes" aria-controls="panel-attributes">
          Attributes
        </Tab>
      </TabList>
      <section id="panel-attributes" role="tabpanel" aria-labelledby="tab-attributes" className={styles.panel}>
        <Attributes ci={ci} type={type} types={types} />
      </section>

      {editing && types ? (
        <CiPanel
          mode={{ kind: 'edit', ci }}
          types={types}
          onClose={() => setEditing(false)}
          onSaved={(saved, changed) => {
            setCi(saved);
            if (changed) toast('Changes saved');
          }}
        />
      ) : null}

      <Dialog open={confirmRetire} modalType="alert" onOpenChange={(_, data) => setConfirmRetire(data.open)}>
        <DialogSurface aria-describedby="retire-body">
          <DialogBody>
            <DialogTitle>Retire {ci.name}?</DialogTitle>
            <DialogContent id="retire-body">
              It’s hidden from search unless Show retired is on. Its details are kept, and you can restore it.
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setConfirmRetire(false)}>Cancel</Button>
              <Button appearance="primary" onClick={() => void change('retire')}>
                Retire
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={changedMeanwhile !== null} modalType="alert">
        <DialogSurface aria-describedby="changed-body">
          <DialogBody>
            <DialogTitle>This CI changed meanwhile</DialogTitle>
            <DialogContent id="changed-body">
              {changedMeanwhile
                ? `${changedMeanwhile.changedBy?.displayName ?? 'Someone else'} saved changes at ${formatUtcTime(changedMeanwhile.changedAt)} UTC. Nothing was changed by you yet.`
                : null}
            </DialogContent>
            <DialogActions>
              <Button
                appearance="primary"
                onClick={() => {
                  if (changedMeanwhile) setCi(changedMeanwhile);
                  setChangedMeanwhile(null);
                }}
              >
                Review their changes
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
      <Toaster toasterId={toasterId} position="bottom-end" />
    </>
  );
}

function Attributes({ ci, type, types }: { ci: CiDetail; type: CiType | undefined; types: CiType[] | null }) {
  const styles = useStyles();
  const builtIn = type?.attributes.filter((a) => a.builtIn) ?? [];
  const custom = type?.attributes.filter((a) => !a.builtIn) ?? [];
  return (
    <>
      <div className={styles.group}>
        <h2 className={styles.groupTitle}>General</h2>
        <dl className={styles.grid}>
          <dt className={styles.term}>Name</dt>
          <dd className={styles.value}>{ci.name}</dd>
          <dt className={styles.term}>Type</dt>
          <dd className={styles.value}>{ci.type}</dd>
          <dt className={styles.term}>Lifecycle</dt>
          <dd className={styles.value}>
            <LifecycleBadge state={ci.lifecycleState} />
          </dd>
          <dt className={styles.term}>ID</dt>
          <dd className={styles.value}>
            <span className={styles.mono}>{ci.id}</span>
            <CopyButton value={ci.id} label="Copy ID" appearance="transparent" />
          </dd>
        </dl>
      </div>
      {builtIn.length > 0 ? (
        <AttributeGroup title={`${ci.type} attributes`} attributes={builtIn} ci={ci} types={types} />
      ) : null}
      {custom.length > 0 ? (
        <AttributeGroup title="Custom attributes" attributes={custom} ci={ci} types={types} />
      ) : null}
      {type && type.attributes.length === 0 ? (
        <div className={styles.noAttributes}>This type has no attributes yet.</div>
      ) : null}
    </>
  );
}

function AttributeGroup({
  title,
  attributes,
  ci,
  types,
}: {
  title: string;
  attributes: AttributeDef[];
  ci: CiDetail;
  types: CiType[] | null;
}) {
  const styles = useStyles();
  return (
    <div className={styles.group}>
      <h2 className={styles.groupTitle}>{title}</h2>
      <dl className={styles.grid}>
        {attributes.map((attribute) => {
          const value = ci.attributes[attribute.name];
          const text = valueText(attribute, value, ci.references);
          const reference =
            attribute.dataType === 'ciRef' && typeof value === 'string' ? ci.references[value] : undefined;
          return (
            <div key={attribute.name} className={styles.contents}>
              <dt className={styles.term}>{attribute.label}</dt>
              <dd className={styles.value}>
                {text === null ? (
                  <span className={styles.notSet}>Not set</span>
                ) : reference ? (
                  <>
                    <Link
                      href={`/ci/${reference.id}`}
                      className={styles.link}
                      onClick={(event) => followLink(event, `/ci/${reference.id}`)}
                    >
                      <TypeIcon type={reference.type} types={types} />
                      {reference.name}
                    </Link>
                    <span className={styles.linkType}>
                      {reference.type}
                      {reference.lifecycleState === 'Retired' ? ' · Retired' : ''}
                    </span>
                  </>
                ) : (
                  text
                )}
              </dd>
            </div>
          );
        })}
      </dl>
    </div>
  );
}

function CiSkeleton() {
  const styles = useStyles();
  return (
    <div aria-busy="true">
      <Skeleton aria-label="Loading the CI">
        <SkeletonItem size={28} />
      </Skeleton>
      <div className={styles.panel}>
        <Skeleton aria-label="Loading the attributes">
          {Array.from({ length: 6 }, (_, i) => (
            <SkeletonItem key={i} size={16} />
          ))}
        </Skeleton>
      </div>
    </div>
  );
}

/** US-53: an unknown CI ID, or one that isn't a CI ID, gets this page, never an error. Merged CIs redirect in slice 12. */
export function CiNotFound({ id, malformed }: { id: string; malformed: boolean }) {
  const styles = useStyles();
  const [q, setQ] = useState('');
  return (
    <>
      <PageHeader crumbs={[{ label: 'Inventory' }]} title="We can’t find that CI" />
      <Card appearance="outline" className={styles.notFound}>
        {malformed ? (
          <Text>
            That isn’t a CI ID. A CI ID looks like{' '}
            <span className={styles.mono}>0192f3a4-7c1e-7d40-9a51-2b7e0c9d1f20</span>.
          </Text>
        ) : (
          <Text>
            There’s no CI with the ID <span className={styles.mono}>{id}</span>. The link may be out of date, or the ID
            mistyped.
          </Text>
        )}
        <SearchBox
          className={styles.search}
          input={{ 'aria-label': 'Search CIs by name' }}
          placeholder="Search CIs by name"
          value={q}
          onChange={(_, data) => setQ(data.value)}
          onKeyDown={(event) => {
            if (event.key === 'Enter') navigate(`/search${q.trim() ? `?q=${encodeURIComponent(q.trim())}` : ''}`);
          }}
        />
        <Link href="/" onClick={(event) => followLink(event, '/')}>
          Go to the dashboard
        </Link>
      </Card>
    </>
  );
}

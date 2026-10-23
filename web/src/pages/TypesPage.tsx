import {
  Badge,
  Button,
  Combobox,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  Link,
  Menu,
  MenuItem,
  MenuList,
  MenuPopover,
  MenuTrigger,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Option,
  OptionGroup,
  SearchBox,
  Skeleton,
  SkeletonItem,
  Spinner,
  Tab,
  TabList,
  Table,
  TableBody,
  TableCell,
  TableHeader,
  TableHeaderCell,
  TableRow,
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
  AddRegular,
  CheckmarkCircleRegular,
  DeleteRegular,
  EditRegular,
  EyeOffRegular,
  EyeRegular,
  InfoRegular,
  LockClosedRegular,
  MoreHorizontalRegular,
} from '@fluentui/react-icons';
import { useCallback, useEffect, useMemo, useRef, useState, type KeyboardEvent } from 'react';

import { api, ApiError } from '../api';
import { followLink, navigate, useLocation } from '../router';
import { useWide } from '../shell/AppShell';
import { PageHeader } from '../shell/PageHeader';
import { ErrorBar, Notice } from '../ui/common';
import {
  asApiError,
  categories,
  kindLabels,
  TypeIcon,
  type AttributeDef,
  type CiType,
  type RelationshipType,
  type TypesUsage,
} from '../ui/inventory';
import {
  AttributePanel,
  NewTypePanel,
  RelationshipTypePanel,
  type AttributeMode,
  type RelationshipMode,
} from '../ui/TypePanels';

const useStyles = makeStyles({
  tabs: { borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}` },
  columns: { display: 'flex', gap: tokens.spacingHorizontalXXL, minHeight: 0, alignItems: 'flex-start' },
  side: { width: '280px', flex: 'none', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  sideButton: { alignSelf: 'flex-start' },
  listbox: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    maxHeight: 'calc(100vh - 300px)',
    overflowY: 'auto',
    borderRadius: tokens.borderRadiusMedium,
    ':focus-visible': { outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`, outlineOffset: '2px' },
  },
  group: { display: 'flex', flexDirection: 'column', gap: '1px' },
  groupName: {
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS}`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground3,
  },
  item: {
    position: 'relative',
    height: '30px',
    boxSizing: 'border-box',
    padding: `0 ${tokens.spacingHorizontalS}`,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    borderRadius: tokens.borderRadiusMedium,
    cursor: 'pointer',
    ':hover': { backgroundColor: tokens.colorNeutralBackground1Hover },
  },
  itemSelected: {
    backgroundColor: tokens.colorBrandBackground2,
    fontWeight: tokens.fontWeightSemibold,
    ':hover': { backgroundColor: tokens.colorBrandBackground2Hover },
    '::before': {
      content: '""',
      position: 'absolute',
      left: 0,
      top: '7px',
      bottom: '7px',
      width: '3px',
      borderRadius: '2px',
      backgroundColor: tokens.colorBrandForeground1,
    },
  },
  itemActive: { outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`, outlineOffset: '-2px' },
  itemName: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  itemCount: {
    minWidth: '40px',
    textAlign: 'right',
    fontSize: tokens.fontSizeBase200,
    color: tokens.colorNeutralForeground3,
  },
  noMatch: {
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'flex-start',
    gap: tokens.spacingVerticalS,
    padding: tokens.spacingVerticalL,
  },
  details: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  narrowPicker: { display: 'flex', alignItems: 'flex-end', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  narrowField: { flex: 1, minWidth: '240px' },
  header: { display: 'flex', alignItems: 'flex-start', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  headerText: { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  titleRow: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalM,
    flexWrap: 'wrap',
    fontSize: '24px',
  },
  title: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
  },
  subtitle: { color: tokens.colorNeutralForeground3 },
  reason: {
    margin: 0,
    maxWidth: '280px',
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalS} ${tokens.spacingVerticalS} 32px`,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
  },
  sectionHeader: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  sectionTitle: {
    margin: 0,
    flex: 1,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  intro: { margin: 0, flex: 1, minWidth: '320px', color: tokens.colorNeutralForeground2 },
  tableWrap: {
    overflowX: 'auto',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  table: { minWidth: '900px' },
  relTable: { minWidth: '1040px' },
  sticky: { position: 'sticky', left: 0, zIndex: 1, backgroundColor: tokens.colorNeutralBackground1 },
  empty: {
    padding: `${tokens.spacingVerticalXXXL} ${tokens.spacingHorizontalXXL}`,
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    textAlign: 'center',
  },
  mono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase300 },
  monoStrong: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase300,
    fontWeight: tokens.fontWeightSemibold,
  },
  strong: { fontWeight: tokens.fontWeightSemibold },
  muted: { color: tokens.colorNeutralForeground3 },
  number: { textAlign: 'right', fontVariantNumeric: 'tabular-nums' },
  inline: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalXS, whiteSpace: 'nowrap' },
  description: { whiteSpace: 'normal', minWidth: '280px' },
  actions: { display: 'flex', justifyContent: 'flex-end', gap: '2px' },
  actionsCell: { width: '88px' },
  skeleton: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  danger: {
    backgroundColor: tokens.colorStatusDangerBackground3,
    color: tokens.colorNeutralForegroundOnBrand,
    ':hover': {
      backgroundColor: tokens.colorStatusDangerBackground3Hover,
      color: tokens.colorNeutralForegroundOnBrand,
    },
    ':hover:active': {
      backgroundColor: tokens.colorStatusDangerBackground3Pressed,
      color: tokens.colorNeutralForegroundOnBrand,
    },
  },
  dialogBody: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
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

interface Catalog {
  types: CiType[];
  relationshipTypes: RelationshipType[];
}

type PanelState =
  { kind: 'type' } | { kind: 'attribute'; mode: AttributeMode } | { kind: 'relationship'; mode: RelationshipMode };

type DialogState =
  | { kind: 'deleteAttribute'; type: string; attribute: string }
  | { kind: 'deleteType'; type: string }
  | { kind: 'hideType'; type: string }
  | { kind: 'deleteRelationship'; name: string };

const builtInAttributeReason = 'Built-in attributes can’t be changed: Goldmark and its collectors rely on them.';
const builtInRelationshipReason =
  'Built-in relationship types can’t be changed: Goldmark and its collectors rely on them.';

const count = (n: number) => n.toLocaleString('en-US');
const plural = (n: number, one: string, many: string) => `${count(n)} ${n === 1 ? one : many}`;

/** The type list's groups: PRD §9.3's, then types without one, then hidden types; each A–Z. */
function listGroups(types: CiType[], filter: string): { name: string; types: CiType[] }[] {
  const text = filter.trim().toLowerCase();
  const shown = types.filter((t) => t.name.toLowerCase().includes(text));
  const byName = (a: CiType, b: CiType) => a.name.localeCompare(b.name, 'en');
  const visible = shown.filter((t) => !t.hidden);
  return [
    ...[...categories, null].map((category) => ({
      name: category ?? 'Other',
      types: visible
        .filter((t) => (category === null ? !categories.includes(t.category ?? '') : t.category === category))
        .sort(byName),
    })),
    { name: 'Hidden', types: shown.filter((t) => t.hidden).sort(byName) },
  ].filter((g) => g.types.length > 0);
}

/**
 * Screen 14, Types & attributes (slice 5b; US-10, FR-CM-1, FR-IM-1; DEC-045; boards `docs/design/screens/slice-5b/`),
 * Admin only (`/types`, `/types?type=VirtualMachine`, `/types?tab=relationships`). The CI types tab lists the types in
 * §9.3's groups beside the selected type's attributes; custom types are added, hidden and deleted when unused, custom
 * attributes added, changed and deleted (with a typed confirmation when CIs hold values). The relationship types tab
 * lists them all, with custom ones added, changed and deleted when unused. Everything here is also `/api/v1`.
 */
export function TypesPage() {
  const styles = useStyles();
  const wide = useWide();
  const { search } = useLocation();
  const tab = search.get('tab') === 'relationships' ? 'relationships' : 'types';
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [usage, setUsage] = useState<TypesUsage | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [filter, setFilter] = useState('');
  const [panel, setPanel] = useState<PanelState | null>(null);
  const [dialog, setDialog] = useState<DialogState | null>(null);
  const [busy, setBusy] = useState(false);
  const toasterId = useId('types-toaster');
  const { dispatchToast } = useToastController(toasterId);

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
    try {
      const [{ data: types }, { data: counts }] = await Promise.all([
        api<Catalog>('GET', '/api/v1/types'),
        api<TypesUsage>('GET', '/api/v1/types/usage'),
      ]);
      setCatalog(types);
      setUsage(counts);
    } catch (error) {
      setFailure(asApiError(error));
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const types = useMemo(() => catalog?.types ?? [], [catalog]);
  const groups = useMemo(() => listGroups(types, filter), [types, filter]);
  const wanted = search.get('type');
  const selected =
    types.find((t) => t.name.toLowerCase() === wanted?.toLowerCase()) ?? listGroups(types, '')[0]?.types[0] ?? null;

  // A click adds to the history; moving with the arrow keys replaces the entry, so Back doesn't step through every type passed.
  const select = (name: string, replace = false) => navigate(`/types?type=${encodeURIComponent(name)}`, { replace });
  const setTab = (value: string) =>
    navigate(
      value === 'relationships'
        ? '/types?tab=relationships'
        : `/types${selected ? `?type=${encodeURIComponent(selected.name)}` : ''}`,
    );

  const typeUsage = (name: string) => usage?.types.find((t) => t.name === name);

  return (
    <>
      <PageHeader crumbs={[{ label: 'Administration' }, { label: 'Types & attributes' }]} title="Types & attributes" />
      <TabList className={styles.tabs} selectedValue={tab} onTabSelect={(_, data) => setTab(String(data.value))}>
        <Tab value="types">CI types</Tab>
        <Tab value="relationships">Relationship types</Tab>
      </TabList>

      {failure ? (
        <ErrorBar
          error={failure}
          message={`Couldn’t load the ${tab === 'types' ? 'CI types' : 'relationship types'}. Check your connection, then try again.`}
          onRetry={() => void load()}
        />
      ) : null}

      {tab === 'types' && !failure ? (
        <div className={styles.columns} aria-busy={catalog === null}>
          {wide ? (
            <div className={styles.side}>
              <Button
                className={styles.sideButton}
                appearance="primary"
                icon={<AddRegular />}
                onClick={() => setPanel({ kind: 'type' })}
              >
                New CI type
              </Button>
              <SearchBox
                placeholder="Filter types"
                aria-label="Filter types"
                value={filter}
                onChange={(_, data) => setFilter(data.value)}
              />
              {catalog === null ? (
                <ListSkeleton />
              ) : groups.length === 0 ? (
                <div role="status" className={styles.noMatch}>
                  <span>No types match ‘{filter.trim()}’.</span>
                  <Button size="small" onClick={() => setFilter('')}>
                    Clear filter
                  </Button>
                </div>
              ) : (
                <TypeList
                  groups={groups}
                  selected={selected?.name ?? null}
                  counts={(name) => typeUsage(name)?.cis ?? null}
                  onSelect={select}
                />
              )}
            </div>
          ) : null}

          <section aria-labelledby="type-title" className={styles.details}>
            {!wide ? (
              <div className={styles.narrowPicker}>
                <TypeCombobox types={types} selected={selected?.name ?? null} onSelect={select} />
                <Button appearance="primary" icon={<AddRegular />} onClick={() => setPanel({ kind: 'type' })}>
                  New CI type
                </Button>
              </div>
            ) : null}
            {catalog === null || selected === null ? (
              <DetailSkeleton />
            ) : (
              <TypeDetails
                type={selected}
                catalog={catalog}
                usage={usage}
                onShow={async () => {
                  setBusy(true);
                  try {
                    await api('POST', `/api/v1/types/${encodeURIComponent(selected.name)}/show`);
                    toast(`${selected.name} shown again`);
                  } catch (error) {
                    setFailure(asApiError(error));
                  } finally {
                    setBusy(false);
                    void load();
                  }
                }}
                busy={busy}
                onHide={() => setDialog({ kind: 'hideType', type: selected.name })}
                onDelete={() => setDialog({ kind: 'deleteType', type: selected.name })}
                onAddAttribute={() => setPanel({ kind: 'attribute', mode: { kind: 'add', type: selected } })}
                onEditAttribute={(attribute) =>
                  setPanel({ kind: 'attribute', mode: { kind: 'edit', type: selected, attribute } })
                }
                onDeleteAttribute={(attribute) =>
                  setDialog({ kind: 'deleteAttribute', type: selected.name, attribute: attribute.name })
                }
              />
            )}
          </section>
        </div>
      ) : null}

      {tab === 'relationships' && !failure ? (
        <RelationshipTypes
          catalog={catalog}
          usage={usage}
          onAdd={() => setPanel({ kind: 'relationship', mode: { kind: 'new' } })}
          onEdit={(relationshipType) => setPanel({ kind: 'relationship', mode: { kind: 'edit', relationshipType } })}
          onDelete={(relationshipType) => setDialog({ kind: 'deleteRelationship', name: relationshipType.name })}
        />
      ) : null}

      {panel?.kind === 'type' ? (
        <NewTypePanel
          types={types}
          onClose={() => setPanel(null)}
          onSaved={(type) => {
            setPanel(null);
            toast(`${type.name} added`);
            select(type.name);
            void load();
          }}
        />
      ) : null}
      {panel?.kind === 'attribute' ? (
        <AttributePanel
          mode={panel.mode}
          types={types}
          usage={usage}
          onClose={() => setPanel(null)}
          onSaved={(attribute, changed) => {
            setPanel(null);
            if (changed) toast(panel.mode.kind === 'add' ? `${attribute.label} added` : 'Changes saved');
            void load();
          }}
        />
      ) : null}
      {panel?.kind === 'relationship' ? (
        <RelationshipTypePanel
          mode={panel.mode}
          types={types}
          onClose={() => setPanel(null)}
          onSaved={(relationshipType, changed) => {
            setPanel(null);
            if (changed) toast(panel.mode.kind === 'new' ? `${relationshipType.name} added` : 'Changes saved');
            void load();
          }}
        />
      ) : null}

      {dialog && catalog ? (
        <ConfirmDialogs
          dialog={dialog}
          catalog={catalog}
          usage={usage}
          onClose={() => setDialog(null)}
          onDone={(text, deletedType) => {
            setDialog(null);
            toast(text);
            if (deletedType) navigate('/types');
            void load();
          }}
          onReload={() => void load()}
        />
      ) : null}
      <Toaster toasterId={toasterId} position="bottom-end" />
    </>
  );
}

function ListSkeleton() {
  const styles = useStyles();
  return (
    <Skeleton aria-label="Loading the CI types" className={styles.skeleton}>
      {[60, 190, 150, 170, 60, 200, 160, 180, 140, 60].map((width, i) => (
        <SkeletonItem key={i} size={12} style={{ width: `${width}px` }} />
      ))}
    </Skeleton>
  );
}

function DetailSkeleton() {
  const styles = useStyles();
  return (
    <Skeleton aria-label="Loading the type" className={styles.skeleton}>
      <SkeletonItem size={28} style={{ width: '260px' }} />
      <SkeletonItem size={16} style={{ width: '200px' }} />
      {[90, 80, 85, 70, 88, 76].map((width, i) => (
        <SkeletonItem key={i} size={16} style={{ width: `${width}%` }} />
      ))}
    </Skeleton>
  );
}

/**
 * The type list (wide layout): one single-select listbox with §9.3's groups. The selection follows the arrow keys,
 * Home and End; Tab moves on to the details.
 */
function TypeList({
  groups,
  selected,
  counts,
  onSelect,
}: {
  groups: { name: string; types: CiType[] }[];
  selected: string | null;
  counts: (name: string) => number | null;
  onSelect: (name: string, replace?: boolean) => void;
}) {
  const styles = useStyles();
  const listId = useId('type-list');
  const flat = groups.flatMap((g) => g.types.map((t) => t.name));
  const [focused, setFocused] = useState(false);
  const container = useRef<HTMLDivElement>(null);
  const optionId = (name: string) => `${listId}-${name}`;

  useEffect(() => {
    if (selected) document.getElementById(optionId(selected))?.scrollIntoView({ block: 'nearest' });
  });

  const onKeyDown = (event: KeyboardEvent) => {
    const index = selected ? flat.indexOf(selected) : -1;
    const next =
      event.key === 'ArrowDown'
        ? Math.min(flat.length - 1, index + 1)
        : event.key === 'ArrowUp'
          ? Math.max(0, index - 1)
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? flat.length - 1
              : null;
    if (next === null) return;
    event.preventDefault();
    const name = flat[next];
    if (name && name !== selected) onSelect(name, true);
  };

  return (
    <div
      ref={container}
      role="listbox"
      aria-label="CI types"
      tabIndex={0}
      className={styles.listbox}
      {...(selected && flat.includes(selected) ? { 'aria-activedescendant': optionId(selected) } : {})}
      onKeyDown={onKeyDown}
      onFocus={() => setFocused(true)}
      onBlur={() => setFocused(false)}
    >
      {groups.map((g) => (
        <div key={g.name} role="group" aria-labelledby={`${listId}-group-${g.name}`} className={styles.group}>
          <div id={`${listId}-group-${g.name}`} className={styles.groupName}>
            {g.name}
          </div>
          {g.types.map((t) => {
            const isSelected = t.name === selected;
            const n = counts(t.name);
            return (
              <div
                key={t.name}
                id={optionId(t.name)}
                role="option"
                aria-selected={isSelected}
                className={mergeClasses(
                  styles.item,
                  isSelected && styles.itemSelected,
                  isSelected && focused && styles.itemActive,
                )}
                onClick={() => {
                  container.current?.focus();
                  onSelect(t.name);
                }}
              >
                <TypeIcon type={t.name} types={g.types} />
                <span className={styles.itemName}>{t.name}</span>
                {t.builtIn ? null : (
                  <Badge appearance="tint" color="brand" size="small">
                    Custom
                  </Badge>
                )}
                <span className={styles.itemCount}>{n === null ? '' : count(n)}</span>
              </div>
            );
          })}
        </div>
      ))}
    </div>
  );
}

/** Below 1100 px the list becomes a Combobox above the details, with the same groups and icons. */
function TypeCombobox({
  types,
  selected,
  onSelect,
}: {
  types: CiType[];
  selected: string | null;
  onSelect: (name: string) => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const groups = listGroups(types, typed ?? '');
  return (
    <Field label="CI type" className={styles.narrowField}>
      <Combobox
        value={typed ?? selected ?? ''}
        selectedOptions={selected ? [selected] : []}
        onChange={(event) => setTyped(event.target.value)}
        onOpenChange={(_, data) => {
          if (!data.open) setTyped(null);
        }}
        onOptionSelect={(_, data) => {
          setTyped(null);
          if (data.optionValue) onSelect(data.optionValue);
        }}
      >
        {groups.map((g) => (
          <OptionGroup key={g.name} label={g.name}>
            {g.types.map((t) => (
              <Option key={t.name} value={t.name} text={t.name}>
                <span className={styles.inline}>
                  <TypeIcon type={t.name} types={types} />
                  {t.name}
                </span>
              </Option>
            ))}
          </OptionGroup>
        ))}
      </Combobox>
    </Field>
  );
}

function BuiltInBadge() {
  return (
    <Badge appearance="tint" color="subtle" icon={<LockClosedRegular />}>
      Built in
    </Badge>
  );
}

function CustomBadge() {
  return (
    <Badge appearance="tint" color="brand">
      Custom
    </Badge>
  );
}

/** Why a custom type can't be deleted: its CIs, or another definition that names it; null when it can be. */
function deleteReason(type: CiType, catalog: Catalog, cis: number): string | null {
  if (cis > 0) {
    return `${type.name} has ${plural(cis, 'CI', 'CIs')} (retired ones included), so it can’t be deleted.${
      type.hidden ? '' : ' Hide it instead to take it out of the pickers.'
    }`;
  }
  for (const other of catalog.types) {
    const link = other.name === type.name ? undefined : other.attributes.find((a) => a.refType === type.name);
    if (link) return `${link.label} (an attribute of ${other.name}) links to ${type.name}.`;
  }
  const relationship = catalog.relationshipTypes.find((r) => r.from === type.name || r.to === type.name);
  return relationship ? `The relationship type ${relationship.name} names ${type.name}.` : null;
}

function TypeDetails({
  type,
  catalog,
  usage,
  busy,
  onShow,
  onHide,
  onDelete,
  onAddAttribute,
  onEditAttribute,
  onDeleteAttribute,
}: {
  type: CiType;
  catalog: Catalog;
  usage: TypesUsage | null;
  busy: boolean;
  onShow: () => Promise<void>;
  onHide: () => void;
  onDelete: () => void;
  onAddAttribute: () => void;
  onEditAttribute: (attribute: AttributeDef) => void;
  onDeleteAttribute: (attribute: AttributeDef) => void;
}) {
  const styles = useStyles();
  const reasonId = useId('delete-type-reason');
  const [descending, setDescending] = useState(false);
  const typeUsage = usage?.types.find((t) => t.name === type.name);
  const cis = typeUsage?.cis ?? 0;
  const reason = deleteReason(type, catalog, cis);
  const values = (name: string) => typeUsage?.attributes.find((a) => a.name === name)?.values ?? 0;
  const attributes = [...type.attributes].sort((a, b) => a.label.localeCompare(b.label, 'en') * (descending ? -1 : 1));
  const searchPath = `/search?type=${encodeURIComponent(type.name)}&retired=true`;

  return (
    <>
      <div className={styles.header}>
        <div className={styles.headerText}>
          <div className={styles.titleRow}>
            <TypeIcon type={type.name} types={catalog.types} />
            <h2 id="type-title" className={styles.title}>
              {type.name}
            </h2>
            {type.builtIn ? <BuiltInBadge /> : <CustomBadge />}
            {type.hidden ? (
              <Badge appearance="tint" color="subtle" icon={<EyeOffRegular />}>
                Hidden
              </Badge>
            ) : null}
          </div>
          <span className={styles.subtitle}>
            {type.category ?? 'No group'} ·{' '}
            {cis > 0 ? (
              <Link href={searchPath} onClick={(event) => followLink(event, searchPath)}>
                {plural(cis, 'CI', 'CIs')}
              </Link>
            ) : (
              'No CIs'
            )}
          </span>
        </div>
        {type.builtIn ? null : (
          <Menu>
            <MenuTrigger disableButtonEnhancement>
              <Button
                appearance="subtle"
                icon={<MoreHorizontalRegular />}
                aria-label={`More actions for ${type.name}`}
              />
            </MenuTrigger>
            <MenuPopover>
              <MenuList>
                {type.hidden ? (
                  <MenuItem icon={<EyeRegular />} disabled={busy} onClick={() => void onShow()}>
                    Show again
                  </MenuItem>
                ) : (
                  <MenuItem icon={<EyeOffRegular />} onClick={onHide}>
                    Hide type…
                  </MenuItem>
                )}
                <MenuItem
                  icon={<DeleteRegular />}
                  disabled={reason !== null}
                  {...(reason ? { 'aria-describedby': reasonId } : {})}
                  onClick={onDelete}
                >
                  Delete type…
                </MenuItem>
              </MenuList>
              {reason ? (
                <p id={reasonId} className={styles.reason}>
                  {reason}
                </p>
              ) : null}
            </MenuPopover>
          </Menu>
        )}
      </div>

      {type.hidden ? (
        <MessageBar intent="info">
          <MessageBarBody>
            <strong>Hidden.</strong> {type.name} isn’t offered for new CIs or in Search’s type filter.{' '}
            {cis > 0 ? `Its ${plural(cis, 'CI keeps', 'CIs keep')} working.` : ''}
          </MessageBarBody>
          <MessageBarActions>
            <Button size="small" disabled={busy} onClick={() => void onShow()}>
              Show again
            </Button>
          </MessageBarActions>
        </MessageBar>
      ) : null}

      <div className={styles.sectionHeader}>
        <h3 className={styles.sectionTitle}>Attributes</h3>
        <Button icon={<AddRegular />} onClick={onAddAttribute}>
          Add attribute
        </Button>
      </div>

      {type.attributes.length === 0 ? (
        <div className={styles.empty}>
          <span>This type has no attributes yet.</span>
          <Button onClick={onAddAttribute}>Add attribute</Button>
        </div>
      ) : (
        <div className={styles.tableWrap}>
          <Table aria-label={`Attributes of ${type.name}`} sortable className={styles.table}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell
                  className={styles.sticky}
                  sortDirection={descending ? 'descending' : 'ascending'}
                  onClick={() => setDescending((d) => !d)}
                >
                  Label
                </TableHeaderCell>
                <TableHeaderCell>Name</TableHeaderCell>
                <TableHeaderCell>Kind</TableHeaderCell>
                <TableHeaderCell>Required</TableHeaderCell>
                <TableHeaderCell>Details</TableHeaderCell>
                <TableHeaderCell className={styles.number}>CIs with a value</TableHeaderCell>
                <TableHeaderCell>
                  <span className={styles.srOnly}>Source</span>
                </TableHeaderCell>
                <TableHeaderCell className={styles.actionsCell}>
                  <span className={styles.srOnly}>Actions</span>
                </TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {attributes.map((a) => (
                <TableRow key={a.name}>
                  <TableCell className={mergeClasses(styles.sticky, styles.strong)}>{a.label}</TableCell>
                  <TableCell>
                    <code className={styles.mono}>{a.name}</code>
                  </TableCell>
                  <TableCell>{kindLabels[a.dataType]}</TableCell>
                  <TableCell>{a.required ? 'Yes' : 'No'}</TableCell>
                  <TableCell>
                    <AttributeDetails attribute={a} types={catalog.types} />
                  </TableCell>
                  <TableCell className={styles.number}>{count(values(a.name))}</TableCell>
                  <TableCell>{a.builtIn ? <BuiltInBadge /> : <CustomBadge />}</TableCell>
                  <TableCell>
                    <RowActions
                      label={a.label}
                      lockedReason={a.builtIn ? builtInAttributeReason : null}
                      onEdit={() => onEditAttribute(a)}
                      onDelete={() => onDeleteAttribute(a)}
                    />
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </>
  );
}

function AttributeDetails({ attribute, types }: { attribute: AttributeDef; types: CiType[] }) {
  const styles = useStyles();
  if (attribute.dataType === 'enum') return <>{(attribute.choices ?? []).join(', ')}</>;
  if (attribute.dataType === 'ciRef') {
    return attribute.refType ? (
      <span className={styles.inline}>
        <TypeIcon type={attribute.refType} types={types} />
        {attribute.refType}
      </span>
    ) : (
      <>Any type</>
    );
  }
  return <span className={styles.muted}>None</span>;
}

/** Edit and Delete for a row; a locked row shows both disabled, with the reason in a Tooltip. */
function RowActions({
  label,
  lockedReason,
  deleteReason: deleteBlocked,
  onEdit,
  onDelete,
}: {
  label: string;
  lockedReason: string | null;
  deleteReason?: string | null;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const styles = useStyles();
  const editButton = (
    <Button
      appearance="subtle"
      icon={<EditRegular />}
      aria-label={`Edit ${label}`}
      disabledFocusable={lockedReason !== null}
      onClick={onEdit}
    />
  );
  const deleteWhy = lockedReason ?? deleteBlocked ?? null;
  const deleteButton = (
    <Button
      appearance="subtle"
      icon={<DeleteRegular />}
      aria-label={`Delete ${label}`}
      disabledFocusable={deleteWhy !== null}
      onClick={onDelete}
    />
  );
  return (
    <div className={styles.actions}>
      {lockedReason ? (
        <Tooltip content={lockedReason} relationship="description">
          {editButton}
        </Tooltip>
      ) : (
        editButton
      )}
      {deleteWhy ? (
        <Tooltip content={deleteWhy} relationship="description">
          {deleteButton}
        </Tooltip>
      ) : (
        deleteButton
      )}
    </div>
  );
}

/** owned_by's dependency is a Team or a Person, which one column can't say (0006): the list says so. */
function endName(relationshipType: RelationshipType, end: 'from' | 'to'): string {
  const name = relationshipType[end];
  if (name) return name;
  return end === 'to' && relationshipType.builtIn && relationshipType.name === 'owned_by'
    ? 'Team or Person'
    : 'Any type';
}

function RelationshipTypes({
  catalog,
  usage,
  onAdd,
  onEdit,
  onDelete,
}: {
  catalog: Catalog | null;
  usage: TypesUsage | null;
  onAdd: () => void;
  onEdit: (relationshipType: RelationshipType) => void;
  onDelete: (relationshipType: RelationshipType) => void;
}) {
  const styles = useStyles();
  const rows = [...(catalog?.relationshipTypes ?? [])].sort(
    (a, b) => Number(b.builtIn) - Number(a.builtIn) || a.name.localeCompare(b.name, 'en'),
  );
  const used = (name: string) => usage?.relationshipTypes.find((r) => r.name === name)?.relationships ?? 0;
  const end = (r: RelationshipType, which: 'from' | 'to') => {
    const name = endName(r, which);
    return (
      <span className={styles.inline}>
        <TypeIcon
          type={name === 'Team or Person' ? 'Team' : name === 'Any type' ? 'Any' : name}
          types={catalog?.types ?? null}
        />
        {name}
      </span>
    );
  };
  return (
    <>
      <div className={styles.sectionHeader}>
        <p className={styles.intro}>
          The direction is always dependent → dependency: “A runs_on B” means A depends on B. Impact passes from B to A
          only when the type’s impact is Down.
        </p>
        <Button icon={<AddRegular />} onClick={onAdd}>
          New relationship type
        </Button>
      </div>
      {catalog === null ? (
        <DetailSkeleton />
      ) : (
        <div className={styles.tableWrap}>
          <Table aria-label="Relationship types" className={styles.relTable}>
            <TableHeader>
              <TableRow>
                <TableHeaderCell className={styles.sticky}>Name</TableHeaderCell>
                <TableHeaderCell>From</TableHeaderCell>
                <TableHeaderCell>To</TableHeaderCell>
                <TableHeaderCell>
                  <span className={styles.inline}>
                    Impact
                    <Tooltip
                      content="Down: when the CI it points to is down, this CI is impacted. None: no impact passes along this relationship."
                      relationship="description"
                    >
                      <Button
                        size="small"
                        appearance="transparent"
                        icon={<InfoRegular />}
                        aria-label="What impact means"
                      />
                    </Tooltip>
                  </span>
                </TableHeaderCell>
                <TableHeaderCell>Description</TableHeaderCell>
                <TableHeaderCell>
                  <span className={styles.srOnly}>Source</span>
                </TableHeaderCell>
                <TableHeaderCell className={styles.actionsCell}>
                  <span className={styles.srOnly}>Actions</span>
                </TableHeaderCell>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => {
                const n = used(r.name);
                return (
                  <TableRow key={r.name}>
                    <TableCell className={styles.sticky}>
                      <code className={styles.monoStrong}>{r.name}</code>
                    </TableCell>
                    <TableCell>{end(r, 'from')}</TableCell>
                    <TableCell>{end(r, 'to')}</TableCell>
                    <TableCell>
                      {r.impact === 'Down' ? (
                        <Badge appearance="tint" color="brand">
                          Down
                        </Badge>
                      ) : (
                        <Badge appearance="tint" color="subtle">
                          None
                        </Badge>
                      )}
                    </TableCell>
                    <TableCell className={styles.description}>{r.description ?? ''}</TableCell>
                    <TableCell>{r.builtIn ? <BuiltInBadge /> : <CustomBadge />}</TableCell>
                    <TableCell>
                      <RowActions
                        label={r.name}
                        lockedReason={r.builtIn ? builtInRelationshipReason : null}
                        deleteReason={n > 0 ? `${plural(n, 'relationship uses', 'relationships use')} it.` : null}
                        onEdit={() => onEdit(r)}
                        onDelete={() => onDelete(r)}
                      />
                    </TableCell>
                  </TableRow>
                );
              })}
            </TableBody>
          </Table>
        </div>
      )}
    </>
  );
}

/**
 * The confirmations (boards `DeleteUnused`, `DeleteTyped*`, `DeleteType`, `HideType`, `DeleteRel`): deleting values
 * that exist needs the attribute's name typed (DEC-045 items 2 and 6), anything else a plain confirmation. Each reads
 * the item from the current catalog, so a reload behind it (after "changed meanwhile") shows the counts now.
 */
function ConfirmDialogs({
  dialog,
  catalog,
  usage,
  onClose,
  onDone,
  onReload,
}: {
  dialog: DialogState;
  catalog: Catalog;
  usage: TypesUsage | null;
  onClose: () => void;
  onDone: (toast: string, deletedType?: boolean) => void;
  onReload: () => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState('');
  const [working, setWorking] = useState(false);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  const typeName = dialog.kind === 'deleteRelationship' ? null : dialog.type;
  const type = typeName ? catalog.types.find((t) => t.name === typeName) : undefined;
  const cis = usage?.types.find((t) => t.name === typeName)?.cis ?? 0;

  let title: string;
  let body: string;
  let primary = 'Delete';
  let danger = true;
  let confirmName: string | null = null;
  let run: (() => Promise<string>) | null = null;

  if (dialog.kind === 'deleteAttribute') {
    const attribute = type?.attributes.find((a) => a.name === dialog.attribute);
    const values =
      usage?.types.find((t) => t.name === dialog.type)?.attributes.find((a) => a.name === dialog.attribute)?.values ??
      0;
    title = `Delete ${attribute?.label ?? dialog.attribute}?`;
    if (values > 0) {
      body = `${plural(values, 'CI', 'CIs')} of type ${dialog.type} ${values === 1 ? 'has' : 'have'} a value for ${attribute?.label ?? dialog.attribute} (retired ones included). Deleting it removes those values from the CIs; each CI’s history keeps them. Each of those CIs will show as changed by you. This can’t be undone.`;
      primary = 'Delete attribute';
      confirmName = dialog.attribute;
    } else {
      body = 'No CI has a value for it.';
    }
    run = attribute
      ? async () => {
          const { data } = await api<{ valuesRemoved: number }>(
            'DELETE',
            `/api/v1/types/${encodeURIComponent(dialog.type)}/attributes/${encodeURIComponent(attribute.name)}`,
            undefined,
            { ifMatch: `"${attribute.eTag}"` },
          );
          return data.valuesRemoved > 0
            ? `${attribute.label} deleted (${plural(data.valuesRemoved, 'value', 'values')} removed)`
            : `${attribute.label} deleted`;
        }
      : null;
  } else if (dialog.kind === 'deleteType') {
    const n = type?.attributes.length ?? 0;
    title = `Delete ${dialog.type}?`;
    body = `No CIs have this type. ${n === 0 ? 'It has no attributes.' : `Its ${plural(n, 'attribute is', 'attributes are')} deleted with it.`}`;
    run = async () => {
      await api('DELETE', `/api/v1/types/${encodeURIComponent(dialog.type)}`);
      return `${dialog.type} deleted`;
    };
  } else if (dialog.kind === 'hideType') {
    title = `Hide ${dialog.type}?`;
    body = `It’s no longer offered for new CIs or in Search’s type filter. ${
      cis > 0 ? `Its ${plural(cis, 'CI keeps', 'CIs keep')} working, and you` : 'You'
    } can show the type again.`;
    primary = 'Hide type';
    danger = false;
    run = async () => {
      await api('POST', `/api/v1/types/${encodeURIComponent(dialog.type)}/hide`);
      return `${dialog.type} hidden`;
    };
  } else {
    const relationshipType = catalog.relationshipTypes.find((r) => r.name === dialog.name);
    title = `Delete ${dialog.name}?`;
    body = 'No relationships use it.';
    run = relationshipType
      ? async () => {
          await api('DELETE', `/api/v1/relationship-types/${encodeURIComponent(dialog.name)}`, undefined, {
            ifMatch: `"${relationshipType.eTag}"`,
          });
          return `${dialog.name} deleted`;
        }
      : null;
  }

  const confirm = async () => {
    if (!run) return;
    setWorking(true);
    setFailure(null);
    setProblem(null);
    try {
      onDone(await run(), dialog.kind === 'deleteType');
    } catch (error) {
      const answer = asApiError(error);
      if (answer.status === 412 || answer.status === 409 || answer.status === 404) {
        // Changed meanwhile, now in use, or gone: say so, and show the counts as they are now.
        setProblem(
          answer.status === 412
            ? 'This changed meanwhile. The page now shows it as it is; check and try again.'
            : answer.status === 404
              ? 'This was deleted meanwhile.'
              : answer.message,
        );
        onReload();
      } else {
        setFailure(answer);
      }
    } finally {
      setWorking(false);
    }
  };

  const blocked = run === null || (confirmName !== null && typed !== confirmName);
  return (
    <Dialog open modalType="alert" onOpenChange={(_, data) => (data.open ? undefined : onClose())}>
      <DialogSurface aria-describedby="types-confirm-body">
        <DialogBody>
          <DialogTitle>{title}</DialogTitle>
          <DialogContent className={styles.dialogBody}>
            <p id="types-confirm-body" style={{ margin: 0 }}>
              {body}
            </p>
            {problem ? <Notice intent="warning">{problem}</Notice> : null}
            {failure ? <ErrorBar error={failure} onRetry={() => void confirm()} /> : null}
            {confirmName ? (
              <Field
                label={{
                  children: (
                    <>
                      Type <code className={styles.mono}>{confirmName}</code> to confirm
                    </>
                  ),
                }}
              >
                <Input value={typed} input={{ className: styles.mono }} onChange={(_, data) => setTyped(data.value)} />
              </Field>
            ) : null}
          </DialogContent>
          <DialogActions>
            <Button onClick={onClose}>Cancel</Button>
            <Button
              appearance="primary"
              className={danger && !blocked && !working ? styles.danger : undefined}
              disabled={blocked || working}
              onClick={() => void confirm()}
            >
              {working ? <Spinner size="tiny" aria-label="Working" /> : null}
              {primary}
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

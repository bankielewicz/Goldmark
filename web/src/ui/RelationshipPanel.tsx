import {
  Button,
  Combobox,
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
  Field,
  Input,
  Option,
  OptionGroup,
  OverlayDrawer,
  Radio,
  RadioGroup,
  Spinner,
  Tag,
  TagPicker,
  TagPickerControl,
  TagPickerGroup,
  TagPickerInput,
  TagPickerList,
  TagPickerOption,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import { useEffect, useMemo, useRef, useState } from 'react';

import { api, type ApiError } from '../api';
import { formatUtcTime } from '../format';
import { ErrorBar, Notice } from './common';
import {
  asApiError,
  TypeIcon,
  type CiDetail,
  type CiPageData,
  type CiSummary,
  type CiType,
  type RelationshipType,
} from './inventory';
import {
  endsText,
  groupFits,
  names,
  otherTypes,
  relationshipLabel,
  relationshipMessage,
  sidesFor,
  type RedundancyGroupUse,
  type Relationship,
} from './relationships';

const useStyles = makeStyles({
  drawer: { width: '480px', maxWidth: '100vw' },
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalXL,
  },
  typeBox: { position: 'relative' },
  typeCombobox: { width: '100%', boxSizing: 'border-box' },
  typeInput: { paddingRight: '52%' },
  ends: {
    position: 'absolute',
    right: '36px',
    top: '6px',
    maxWidth: '48%',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    whiteSpace: 'nowrap',
    pointerEvents: 'none',
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase300,
  },
  option: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, width: '100%' },
  optionMono: { fontFamily: tokens.fontFamilyMonospace, fontSize: tokens.fontSizeBase200 },
  optionAfter: { marginLeft: 'auto', color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  side: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXXS },
  sideLabel: { fontWeight: tokens.fontWeightSemibold },
  sideValue: { minHeight: '24px', display: 'flex', alignItems: 'center' },
  help: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  badTag: {
    border: `${tokens.strokeWidthThin} solid ${tokens.colorPaletteRedBorder2}`,
    backgroundColor: tokens.colorPaletteRedBackground1,
  },
  readOnly: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalL },
  preview: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    padding: tokens.spacingHorizontalM,
    backgroundColor: tokens.colorNeutralBackground2,
    borderRadius: tokens.borderRadiusMedium,
  },
  previewLabel: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    fontWeight: tokens.fontWeightSemibold,
  },
  yours: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  srOnly: { position: 'absolute', width: '1px', height: '1px', overflow: 'hidden', clip: 'rect(0 0 0 0)' },
});

/** Add (from the tab) or Edit (a row's Edit button). */
export type RelationshipPanelMode = { kind: 'add' } | { kind: 'edit'; relationship: Relationship };

type Side = 'dependent' | 'dependency';

/** The panel's field messages, in the boards' words. */
type FieldErrors = { type?: string | undefined; others?: string | undefined; group?: string | undefined };

/** A CI chosen in the picker: its ID, name and type, and why its add failed, if it did. */
interface Chosen {
  id: string;
  name: string;
  type: string;
  failed: string | null;
}

/**
 * The Add relationship and Edit relationship panel (slice 6 boards `AddRel*`, `EditRel*`; DEC-046 items 2 and 6): an
 * overlay drawer, 480 px. Add takes a relationship type the CI's type can have, the CI's side when it can be either,
 * one or more CIs of the other end's type (never retired), and an optional redundancy group, suggested from the
 * dependent's relationships; each CI is one audited request, in order, and a failure keeps the panel open with
 * the CIs that failed (Cancel then reads Close). Edit changes the redundancy group only, with the version the tab
 * read: a 412 opens the conflict dialog. Closing with unsaved changes asks first.
 */
export function RelationshipPanel({
  mode,
  ci,
  types,
  relationshipTypes,
  onClose,
  onAdded,
  onSaved,
}: {
  mode: RelationshipPanelMode;
  ci: CiDetail;
  types: CiType[] | null;
  relationshipTypes: RelationshipType[];
  onClose: () => void;
  /** Some relationships were added (the tab reloads); `done` when the panel closed with every one added. */
  onAdded: (count: number, done: boolean) => void;
  onSaved: (changed: boolean) => void;
}) {
  const styles = useStyles();
  const editing = mode.kind === 'edit' ? mode.relationship : null;
  const allowed = useMemo(
    () => relationshipTypes.filter((t) => sidesFor(t, ci.type).dependent || sidesFor(t, ci.type).dependency),
    [relationshipTypes, ci.type],
  );
  const [typeName, setTypeName] = useState(editing?.type ?? '');
  const [typedType, setTypedType] = useState<string | null>(null);
  const type = relationshipTypes.find((t) => t.name === typeName) ?? null;
  const sides = type ? sidesFor(type, ci.type) : { dependent: true, dependency: false };
  const [chosenSide, setChosenSide] = useState<Side>(
    editing ? (editing.from.id === ci.id ? 'dependent' : 'dependency') : 'dependent',
  );
  const side: Side = sides.dependent && sides.dependency ? chosenSide : sides.dependency ? 'dependency' : 'dependent';
  const others = type ? otherTypes(type, side) : [];
  const [chosen, setChosen] = useState<Chosen[]>([]);
  const [baseGroup, setBaseGroup] = useState(editing?.redundancyGroup ?? '');
  const [group, setGroup] = useState(baseGroup);
  const [etag, setEtag] = useState(editing?.eTag ?? '');
  const [errors, setErrors] = useState<FieldErrors>({});
  const [partial, setPartial] = useState<{ added: string[]; failed: { name: string; message: string }[] } | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const [deleted, setDeleted] = useState(false);
  const [conflict, setConflict] = useState<Relationship | null>(null);
  const [reviewed, setReviewed] = useState<{ theirs: Relationship; mine: string } | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const firstField = useRef<HTMLInputElement>(null);

  useEffect(() => {
    firstField.current?.focus();
  }, []);

  const dirty = editing
    ? group.trim() !== baseGroup.trim()
    : typeName !== '' || chosen.length > 0 || group.trim() !== '';

  // The dependent whose groups the panel suggests: this CI, or the one other CI chosen as the dependent.
  const dependentId = editing
    ? editing.from.id
    : side === 'dependent'
      ? ci.id
      : chosen.length === 1
        ? (chosen[0]?.id ?? null)
        : null;
  const dependentName = editing ? editing.from.name : side === 'dependent' ? ci.name : (chosen[0]?.name ?? '');
  const suggestions = useGroups(dependentId);

  const requestClose = () => {
    if (saving) return;
    if (dirty && !partial) setConfirmDiscard(true);
    else onClose();
  };

  const add = async () => {
    const local: FieldErrors = {};
    if (!type) local.type = 'Choose a relationship type.';
    if (chosen.length === 0) local.others = 'Choose a CI.';
    if (!groupFits(group)) local.group = relationshipMessage('TooLong', []);
    setErrors(local);
    setFailure(null);
    if (Object.keys(local).length > 0 || !type) return;

    setSaving(true);
    setPartial(null);
    const added: Chosen[] = [];
    const failed: Chosen[] = [];
    let stop = false;
    for (const other of chosen) {
      if (stop) {
        failed.push(other);
        continue;
      }
      const [from, to] = side === 'dependent' ? [ci, other] : [other, ci];
      try {
        await api('POST', '/api/v1/relationships', {
          type: type.name,
          from: from.id,
          to: to.id,
          redundancyGroup: group,
        });
        added.push(other);
      } catch (error) {
        const problem = asApiError(error);
        if (problem.status === 409 && problem.code === 'exists') {
          failed.push({ ...other, failed: 'this relationship already exists' });
        } else if (problem.status === 400 && (problem.fieldErrors.type || problem.fieldErrors.redundancyGroup)) {
          // The type or the group: they apply to every CI, so the rest aren't sent.
          setErrors({
            ...(problem.fieldErrors.type ? { type: relationshipMessage(problem.fieldErrors.type[0] ?? '', []) } : {}),
            ...(problem.fieldErrors.redundancyGroup ? { group: relationshipMessage('TooLong', []) } : {}),
          });
          failed.push(other);
          stop = true;
        } else if (problem.status === 400) {
          const code =
            (problem.fieldErrors[side === 'dependent' ? 'to' : 'from'] ??
              problem.fieldErrors.to ??
              problem.fieldErrors.from ?? [''])[0] ?? '';
          failed.push({ ...other, failed: lowerFirst(relationshipMessage(code, others)) });
        } else {
          setFailure(problem);
          failed.push(other);
          stop = true;
        }
      }
    }
    setSaving(false);

    if (added.length > 0 && failed.length === 0) {
      onAdded(added.length, true);
      onClose();
      return;
    }
    if (added.length > 0) onAdded(added.length, false);
    setChosen(failed);
    const reasons = failed.filter((f) => f.failed);
    if (added.length > 0) {
      setPartial({
        added: added.map((a) => a.name),
        failed: reasons.map((f) => ({ name: f.name, message: f.failed ?? '' })),
      });
    } else if (reasons.length === 1 && chosen.length === 1 && reasons[0]) {
      const only = reasons[0];
      const [fromName, toName] = side === 'dependent' ? [ci.name, only.name] : [only.name, ci.name];
      setErrors((e) => ({
        ...e,
        others:
          only.failed === 'this relationship already exists'
            ? `${fromName} ${type.name} ${toName} already exists.`
            : upperFirst(only.failed ?? ''),
      }));
    } else if (reasons.length > 0) {
      setErrors((e) => ({
        ...e,
        others: `None was added. ${reasons.map((r) => `${r.name}: ${r.failed ?? ''}.`).join(' ')}`,
      }));
    }
  };

  const save = async () => {
    if (!editing) return;
    if (!groupFits(group)) {
      setErrors({ group: relationshipMessage('TooLong', []) });
      return;
    }
    setErrors({});
    setFailure(null);
    if (!dirty) {
      onSaved(reviewed !== null);
      onClose();
      return;
    }
    setSaving(true);
    try {
      await api('PUT', `/api/v1/relationships/${editing.id}`, { redundancyGroup: group }, { ifMatch: `"${etag}"` });
      onSaved(true);
      onClose();
    } catch (error) {
      const problem = asApiError(error);
      if (problem.status === 404) {
        setDeleted(true);
      } else if (problem.status === 412) {
        try {
          const { data } = await api<Relationship>('GET', `/api/v1/relationships/${editing.id}`);
          setConflict(data);
        } catch (again) {
          const reread = asApiError(again);
          if (reread.status === 404) setDeleted(true);
          else setFailure(reread);
        }
      } else if (problem.status === 400 && problem.fieldErrors.redundancyGroup) {
        setErrors({ group: relationshipMessage('TooLong', []) });
      } else {
        setFailure(problem);
      }
    } finally {
      setSaving(false);
    }
  };

  const review = () => {
    if (!conflict) return;
    setReviewed({ theirs: conflict, mine: group });
    setBaseGroup(conflict.redundancyGroup ?? '');
    setGroup(conflict.redundancyGroup ?? '');
    setEtag(conflict.eTag);
    setConflict(null);
  };

  const preview = previewText(ci, type, side, chosen, editing);
  const errorCount = Object.values(errors).filter(Boolean).length;
  const otherLabel = side === 'dependent' ? 'Depends on' : 'Depended on by';
  const kinds = others.length > 0 ? `${others.map((o) => `${o}s`).join(' or ')}` : 'CIs';

  return (
    <>
      <OverlayDrawer
        open
        position="end"
        className={styles.drawer}
        aria-labelledby="relationship-panel-title"
        onOpenChange={(_, data) => (data.open ? undefined : requestClose())}
      >
        <DrawerHeader>
          <DrawerHeaderTitle
            heading={{ id: 'relationship-panel-title' }}
            action={<Button appearance="subtle" aria-label="Close" icon={<DismissRegular />} onClick={requestClose} />}
          >
            {editing ? `Edit ${relationshipLabel(editing, ci.id)}` : `Add relationship to ${ci.name}`}
          </DrawerHeaderTitle>
        </DrawerHeader>
        <DrawerBody className={styles.body}>
          {errorCount > 1 ? (
            <Notice intent="error">
              <strong>The relationship isn’t added yet.</strong> Fix the {errorCount} fields marked below.
            </Notice>
          ) : null}
          {partial ? (
            <Notice intent="warning">
              <strong>
                {partial.added.length} of {partial.added.length + chosen.length} relationships added.
              </strong>{' '}
              {names(partial.added)} {partial.added.length === 1 ? 'was' : 'were'} added.{' '}
              {partial.failed.map((f) => `${f.name}: ${f.message}.`).join(' ')} Remove{' '}
              {chosen.length === 1 ? 'it' : 'them'} from the list, or close the panel.
            </Notice>
          ) : null}
          {reviewed ? (
            <Notice intent="warning">
              <strong>
                {reviewed.theirs.changedBy?.displayName ?? 'Someone else'} saved changes at{' '}
                {formatUtcTime(reviewed.theirs.changedAt)} UTC.
              </strong>{' '}
              The redundancy group now shows theirs, with yours beneath. Save keeps what the field shows.
            </Notice>
          ) : null}
          {deleted ? (
            <Notice intent="warning">
              This relationship was deleted while you were editing, so it can’t be saved.
            </Notice>
          ) : null}
          {failure ? <ErrorBar error={failure} onRetry={() => void (editing ? save() : add())} /> : null}

          {editing ? (
            <div className={styles.readOnly}>
              <Field label="Relationship type" hint="They can’t change: delete the relationship and add another.">
                <Input
                  readOnly
                  input={{ ref: firstField }}
                  value={editing.type}
                  contentAfter={
                    <span className={styles.help}>
                      {relationshipTypes.find((t) => t.name === editing.type)
                        ? endsText(relationshipTypes.find((t) => t.name === editing.type) as RelationshipType)
                        : ''}
                    </span>
                  }
                />
              </Field>
              <Field label={`${ci.name} is the`}>
                <Input
                  readOnly
                  value={
                    editing.from.id === ci.id
                      ? 'dependent (it depends on the other CI)'
                      : 'dependency (the other CI depends on it)'
                  }
                />
              </Field>
              <Field label={editing.from.id === ci.id ? 'Depends on' : 'Depended on by'}>
                <Input
                  readOnly
                  value={editing.from.id === ci.id ? editing.to.name : editing.from.name}
                  contentAfter={
                    <span className={styles.help}>
                      {editing.from.id === ci.id ? editing.to.type : editing.from.type}
                    </span>
                  }
                />
              </Field>
            </div>
          ) : (
            <>
              <Field
                label="Relationship type"
                required
                {...(errors.type
                  ? { validationState: 'error' as const, validationMessage: errors.type }
                  : type
                    ? {
                        hint: (
                          <>
                            <span className={styles.srOnly}>{endsText(type)}. </span>
                            {type.description ?? ''}
                          </>
                        ),
                      }
                    : {})}
              >
                <div className={styles.typeBox}>
                  <Combobox
                    className={styles.typeCombobox}
                    input={{ ref: firstField, className: type ? styles.typeInput : undefined }}
                    placeholder="Choose a relationship type"
                    value={typedType ?? typeName}
                    selectedOptions={typeName ? [typeName] : []}
                    onChange={(event) => setTypedType(event.target.value)}
                    onOpenChange={(_, data) => {
                      if (!data.open) setTypedType(null);
                    }}
                    onOptionSelect={(_, data) => {
                      setTypedType(null);
                      if (!data.optionValue) return;
                      setTypeName(data.optionValue);
                      setChosen([]);
                      setPartial(null);
                      setErrors((e) => ({ ...e, type: undefined, others: undefined }));
                    }}
                  >
                    {allowed
                      .filter((t) => typedType === null || t.name.includes(typedType.toLowerCase()))
                      .map((t) => (
                        <Option key={t.name} value={t.name} text={t.name}>
                          <span className={styles.option}>
                            <span className={styles.optionMono}>{t.name}</span>
                            <span className={styles.optionAfter}>{endsText(t)}</span>
                          </span>
                        </Option>
                      ))}
                  </Combobox>
                  {type && typedType === null ? (
                    <span className={styles.ends} aria-hidden="true">
                      {endsText(type)}
                    </span>
                  ) : null}
                </div>
              </Field>

              {type ? (
                sides.dependent && sides.dependency ? (
                  <Field label={`${ci.name} is the`} required>
                    <RadioGroup
                      value={chosenSide}
                      onChange={(_, data) => {
                        setChosenSide(data.value as Side);
                        setChosen([]);
                        setPartial(null);
                      }}
                    >
                      <Radio value="dependent" label="dependent (it depends on the other CI)" />
                      <Radio value="dependency" label="dependency (the other CI depends on it)" />
                    </RadioGroup>
                  </Field>
                ) : (
                  <div className={styles.side}>
                    <span id="relationship-side-label" className={styles.sideLabel}>
                      {ci.name} is the
                    </span>
                    <span aria-labelledby="relationship-side-label" className={styles.sideValue}>
                      {side === 'dependent'
                        ? 'dependent (it depends on the other CI)'
                        : 'dependency (the other CI depends on it)'}
                    </span>
                    <span className={styles.help}>
                      With {type.name}, a {ci.type} is always the {side}.
                    </span>
                  </div>
                )
              ) : null}

              <CiPicker
                label={otherLabel}
                types={types}
                allowedTypes={others}
                exclude={ci.id}
                chosen={chosen}
                disabled={!type}
                error={errors.others ?? null}
                hint={`One or more ${kinds} that aren’t retired.${chosen.length > 1 ? ' The type and group apply to each.' : ''}`}
                onChange={(next) => {
                  setChosen(next);
                  setErrors((e) => ({ ...e, others: undefined }));
                }}
              />
            </>
          )}

          <Field
            label="Redundancy group"
            {...(errors.group
              ? { validationState: 'error' as const, validationMessage: errors.group }
              : {
                  hint:
                    reviewed && reviewed.mine.trim() !== group.trim() ? (
                      <span className={styles.yours}>
                        Yours: <strong>{reviewed.mine.trim() || 'None'}</strong>
                        <Button size="small" appearance="transparent" onClick={() => setGroup(reviewed.mine)}>
                          Use mine
                        </Button>
                      </span>
                    ) : (
                      'Impact analysis (a later release) treats a dependent CI’s relationships with the same group as redundant: the dependent is down only when all of them are down. Up to 200 characters.'
                    ),
                })}
          >
            <Combobox
              freeform
              placeholder="None"
              value={group}
              selectedOptions={suggestions.some((s) => s.name === group) ? [group] : []}
              onChange={(event) => {
                setGroup(event.target.value);
                setErrors((e) => ({ ...e, group: undefined }));
              }}
              onOptionSelect={(_, data) => {
                if (data.optionText !== undefined) setGroup(data.optionText);
              }}
            >
              {suggestions.length > 0 ? (
                <OptionGroup label={`Groups on ${dependentName}’s relationships`}>
                  {suggestions.map((s) => (
                    <Option key={s.name} value={s.name} text={s.name}>
                      <span className={styles.option}>
                        {s.name}
                        <span className={styles.optionAfter}>
                          {s.relationships === 1
                            ? '1 relationship'
                            : `${s.relationships.toLocaleString('en-US')} relationships`}
                        </span>
                      </span>
                    </Option>
                  ))}
                </OptionGroup>
              ) : null}
            </Combobox>
          </Field>

          <div className={styles.preview}>
            <span className={styles.previewLabel}>Preview</span>
            <Text>{preview}</Text>
          </div>
        </DrawerBody>
        <DrawerFooter>
          <Button appearance="primary" disabled={saving || deleted} onClick={() => void (editing ? save() : add())}>
            {saving ? <Spinner size="tiny" aria-label={editing ? 'Saving' : 'Adding'} /> : null}
            {editing ? 'Save' : 'Add'}
          </Button>
          <Button disabled={saving} onClick={requestClose}>
            {partial ? 'Close' : 'Cancel'}
          </Button>
        </DrawerFooter>
      </OverlayDrawer>

      <Dialog open={conflict !== null} modalType="alert">
        <DialogSurface aria-describedby="relationship-conflict-body">
          <DialogBody>
            <DialogTitle>This relationship changed while you were editing</DialogTitle>
            <DialogContent id="relationship-conflict-body">
              {conflict
                ? `${conflict.changedBy?.displayName ?? 'Someone else'} changed its redundancy group at ${formatUtcTime(conflict.changedAt)} UTC. Your change isn’t saved yet.`
                : null}
            </DialogContent>
            <DialogActions>
              <Button
                onClick={() => {
                  setConflict(null);
                  onSaved(true);
                  onClose();
                }}
              >
                Discard mine
              </Button>
              <Button appearance="primary" onClick={review}>
                Review their changes
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={confirmDiscard} modalType="alert">
        <DialogSurface aria-describedby="relationship-discard-body">
          <DialogBody>
            <DialogTitle>Discard your changes?</DialogTitle>
            <DialogContent id="relationship-discard-body">
              {editing
                ? `Your change to ${relationshipLabel(editing, ci.id)} isn’t saved yet. If you close the panel, it’s lost.`
                : 'This relationship isn’t added yet. If you close the panel, what you entered is lost.'}
            </DialogContent>
            <DialogActions>
              <Button onClick={() => setConfirmDiscard(false)}>Keep editing</Button>
              <Button
                appearance="primary"
                onClick={() => {
                  setConfirmDiscard(false);
                  onClose();
                }}
              >
                Discard
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>
    </>
  );
}

/** "app-erp-db01 runs_on esx-mx-014: app-erp-db01 depends on esx-mx-014.", or with several CIs "… runs_on 2 CIs: …". */
function previewText(
  ci: CiDetail,
  type: RelationshipType | null,
  side: Side,
  chosen: Chosen[],
  editing: Relationship | null,
): string {
  if (editing)
    return `${editing.from.name} ${editing.type} ${editing.to.name}: ${editing.from.name} depends on ${editing.to.name}.`;
  if (!type || chosen.length === 0) return 'Choose the relationship type and a CI to see the relationship here.';
  const list = names(chosen.map((c) => c.name));
  if (side === 'dependent') {
    const target = chosen.length === 1 ? list : `${chosen.length} CIs`;
    return `${ci.name} ${type.name} ${target}: ${ci.name} depends on ${list}.`;
  }
  const source = chosen.length === 1 ? list : `${chosen.length} CIs`;
  return `${source} ${type.name} ${ci.name}: ${list} ${chosen.length === 1 ? 'depends' : 'depend'} on ${ci.name}.`;
}

function lowerFirst(text: string): string {
  return text.length > 0 ? text[0]?.toLowerCase() + text.slice(1).replace(/\.$/, '') : text;
}

function upperFirst(text: string): string {
  return text.length > 0 ? `${text[0]?.toUpperCase() ?? ''}${text.slice(1)}.` : text;
}

/** The groups on a dependent CI's relationships, for the suggestions; none while it isn't known. */
function useGroups(ciId: string | null): RedundancyGroupUse[] {
  const [groups, setGroups] = useState<RedundancyGroupUse[]>([]);
  useEffect(() => {
    setGroups([]);
    if (!ciId) return undefined;
    let current = true;
    api<{ groups: RedundancyGroupUse[] }>('GET', `/api/v1/cis/${ciId}/redundancy-groups`)
      .then(({ data }) => {
        if (current) setGroups(data.groups);
      })
      .catch(() => undefined);
    return () => {
      current = false;
    };
  }, [ciId]);
  return groups;
}

/**
 * Several CIs of the other end's type (DEC-046 item 6): a TagPicker that searches CI names as you type (never
 * retired CIs, never this CI), each option with its type; a CI whose add failed stays, marked.
 */
function CiPicker({
  label,
  types,
  allowedTypes,
  exclude,
  chosen,
  disabled,
  error,
  hint,
  onChange,
}: {
  label: string;
  types: CiType[] | null;
  allowedTypes: string[];
  exclude: string;
  chosen: Chosen[];
  disabled: boolean;
  error: string | null;
  hint: string;
  onChange: (chosen: Chosen[]) => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState('');
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<CiSummary[]>([]);
  const [searching, setSearching] = useState(false);
  const [searchFailed, setSearchFailed] = useState(false);
  const request = useRef(0);
  const allowedKey = allowedTypes.join(',');

  // Searches while the list is open or something is typed (the TagPicker opens itself as you type).
  useEffect(() => {
    if (!open && typed === '') return undefined;
    const current = ++request.current;
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ q: typed, limit: '10' });
      for (const name of allowedKey ? allowedKey.split(',') : []) query.append('type', name);
      setSearching(true);
      api<CiPageData>('GET', `/api/v1/cis?${query.toString()}`)
        .then(({ data }) => {
          if (request.current !== current) return;
          setOptions(data.items);
          setSearchFailed(false);
        })
        .catch(() => {
          if (request.current !== current) return;
          setOptions([]);
          setSearchFailed(true);
        })
        .finally(() => {
          if (request.current === current) setSearching(false);
        });
    }, 250);
    return () => window.clearTimeout(timer);
  }, [open, typed, allowedKey]);

  const shown = options.filter((o) => o.id !== exclude && !chosen.some((c) => c.id === o.id));
  return (
    <Field
      label={label}
      required
      {...(error ? { validationState: 'error' as const, validationMessage: error } : { hint })}
    >
      <TagPicker
        disabled={disabled}
        selectedOptions={chosen.map((c) => c.id)}
        onOpenChange={(_, data) => setOpen(data.open)}
        onOptionSelect={(_, data) => {
          if (data.value === '') return;
          const picked = options.find((o) => o.id === data.value);
          const next = data.selectedOptions
            .map(
              (id) =>
                chosen.find((c) => c.id === id) ?? (picked && picked.id === id ? { ...picked, failed: null } : null),
            )
            .filter((c): c is Chosen => c !== null)
            .map((c) => ({ id: c.id, name: c.name, type: c.type, failed: c.failed }));
          onChange(next);
          setTyped('');
        }}
      >
        <TagPickerControl>
          <TagPickerGroup aria-label={`${label}: chosen CIs`}>
            {chosen.map((c) => (
              <Tag
                key={c.id}
                value={c.id}
                shape="rounded"
                media={<TypeIcon type={c.type} types={types} />}
                className={mergeClasses(c.failed ? styles.badTag : undefined)}
                dismissIcon={{ 'aria-label': `Remove ${c.name}` }}
              >
                {c.name}
              </Tag>
            ))}
          </TagPickerGroup>
          <TagPickerInput
            placeholder={chosen.length > 0 ? 'Add another' : 'Search CIs by name'}
            value={typed}
            onChange={(event) => setTyped(event.target.value)}
          />
        </TagPickerControl>
        <TagPickerList>
          {shown.length === 0 ? (
            <TagPickerOption value="" text="">
              {searching
                ? 'Searching…'
                : searchFailed
                  ? 'Couldn’t search the CIs. Type again to retry.'
                  : 'No matching CIs'}
            </TagPickerOption>
          ) : (
            shown.map((o) => (
              <TagPickerOption
                key={o.id}
                value={o.id}
                text={o.name}
                media={<TypeIcon type={o.type} types={types} />}
                secondaryContent={o.type}
              >
                {o.name}
              </TagPickerOption>
            ))
          )}
        </TagPickerList>
      </TagPicker>
    </Field>
  );
}

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
  DrawerBody,
  DrawerFooter,
  DrawerHeader,
  DrawerHeaderTitle,
  Dropdown,
  Field,
  Input,
  Option,
  OptionGroup,
  OverlayDrawer,
  Radio,
  RadioGroup,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import { useEffect, useMemo, useRef, useState, type ReactElement, type Ref } from 'react';

import { api, ApiError } from '../api';
import { formatUtcTime } from '../format';
import { navigate } from '../router';
import { ErrorBar, Notice } from './common';
import {
  asApiError,
  categories,
  fieldMessage,
  setPendingToast,
  TypeIcon,
  type AttributeDef,
  type CiDetail,
  type CiPageData,
  type CiSummary,
  type CiType,
} from './inventory';

const useStyles = makeStyles({
  drawer: { width: '480px', maxWidth: '100vw' },
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalXL,
  },
  group: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    paddingTop: tokens.spacingVerticalM,
    borderTop: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  groupTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase400,
    lineHeight: tokens.lineHeightBase400,
    fontWeight: tokens.fontWeightSemibold,
  },
  label: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  option: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, width: '100%' },
  optionType: { marginLeft: 'auto', color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  yours: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  readOnlyType: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
});

/** New CI (from Search) or Edit CI (from its page). */
export type PanelMode = { kind: 'new' } | { kind: 'edit'; ci: CiDetail };

/** A linked CI as the form holds it: its ID, and its name and type for the field. */
interface LinkValue {
  id: string;
  name: string;
  type: string;
}

/** A text, number, date or choice is a string ('' when not set); yes/no a boolean or null; a link a LinkValue or null. */
type FieldValue = string | boolean | null | LinkValue;

interface FormState {
  type: string;
  name: string;
  values: Record<string, FieldValue>;
}

function initialValue(attribute: AttributeDef, ci: CiDetail | null): FieldValue {
  const value = ci?.attributes[attribute.name];
  switch (attribute.dataType) {
    case 'bool':
      return typeof value === 'boolean' ? value : null;
    case 'ciRef': {
      const reference = typeof value === 'string' ? ci?.references[value] : undefined;
      return typeof value === 'string'
        ? { id: value, name: reference?.name ?? value, type: reference?.type ?? '' }
        : null;
    }
    case 'number':
      return typeof value === 'number' ? String(value) : '';
    default:
      return typeof value === 'string' ? value : '';
  }
}

function formOf(ci: CiDetail, type: CiType | undefined): FormState {
  return {
    type: ci.type,
    name: ci.name,
    values: Object.fromEntries((type?.attributes ?? []).map((a) => [a.name, initialValue(a, ci)])),
  };
}

function isEmpty(value: FieldValue | undefined): boolean {
  return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

function same(a: FieldValue | undefined, b: FieldValue | undefined): boolean {
  if (isEmpty(a) && isEmpty(b)) return true;
  if (typeof a === 'object' && a !== null && typeof b === 'object' && b !== null) return a.id === b.id;
  return typeof a === 'string' && typeof b === 'string' ? a.trim() === b.trim() : a === b;
}

function display(value: FieldValue | undefined): string {
  if (isEmpty(value)) return 'Not set';
  if (typeof value === 'boolean') return value ? 'Yes' : 'No';
  if (typeof value === 'object' && value !== null) return value.name;
  return String(value);
}

function requestValue(attribute: AttributeDef, value: FieldValue | undefined): unknown {
  if (isEmpty(value)) return null;
  switch (attribute.dataType) {
    case 'number':
      return Number(value);
    case 'ciRef':
      return (value as LinkValue).id;
    case 'bool':
      return value;
    default:
      return (value as string).trim();
  }
}

/** The form's own checks, before the server's (which it mirrors). */
function check(form: FormState, type: CiType | undefined): Record<string, string> {
  const errors: Record<string, string> = {};
  if (!type) errors.type = 'Required';
  if (form.name.trim() === '') errors.name = 'Required';
  else if (form.name.trim().length > 400) errors.name = 'TooLong';
  for (const attribute of type?.attributes ?? []) {
    const value = form.values[attribute.name];
    if (attribute.required && isEmpty(value)) errors[`attributes.${attribute.name}`] = 'Required';
    else if (attribute.dataType === 'number' && !isEmpty(value) && !Number.isFinite(Number(value))) {
      errors[`attributes.${attribute.name}`] = 'InvalidNumber';
    }
  }
  return errors;
}

/** What someone else changed: the fields that differ between two versions of the form, as labels. */
function changedLabels(a: FormState, b: FormState, type: CiType | undefined): string[] {
  return [
    ...(a.name.trim() === b.name.trim() ? [] : ['Name']),
    ...(type?.attributes ?? []).filter((x) => !same(a.values[x.name], b.values[x.name])).map((x) => x.label),
  ];
}

function list(labels: string[]): string {
  return labels.length <= 1 ? (labels[0] ?? 'it') : `${labels.slice(0, -1).join(', ')} and ${labels.at(-1) ?? ''}`;
}

/**
 * The New CI and Edit CI panel (slice 5 boards `NewCi*`, `EditCi*`): an overlay drawer at every width,
 * 480 px. Values are checked here as the server will, then by the server, whose field errors land on
 * their fields. Editing needs the version the page read (If-Match): a 412 opens the conflict dialog,
 * whose "Review their changes" loads their values and marks each field that differs from what was
 * typed; a CI retired meanwhile says so and can't be saved. Closing with unsaved changes asks first.
 */
export function CiPanel({
  mode,
  types,
  onClose,
  onSaved,
}: {
  mode: PanelMode;
  types: CiType[];
  onClose: () => void;
  onSaved?: (ci: CiDetail, changed: boolean) => void;
}) {
  const styles = useStyles();
  const editing = mode.kind === 'edit' ? mode.ci : null;
  const [base, setBase] = useState<FormState>(() =>
    editing
      ? formOf(
          editing,
          types.find((t) => t.name === editing.type),
        )
      : { type: '', name: '', values: {} },
  );
  const [form, setForm] = useState<FormState>(base);
  const [etag, setEtag] = useState(editing?.eTag ?? '');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const [retired, setRetired] = useState(false);
  const [conflict, setConflict] = useState<{ theirs: CiDetail; changed: string[] } | null>(null);
  // After "Review their changes": what was typed, beside the fields that differ from it.
  const [mine, setMine] = useState<FormState | null>(null);
  const [reviewed, setReviewed] = useState<CiDetail | null>(null);
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const firstField = useRef<HTMLInputElement>(null);
  const type = types.find((t) => t.name === form.type);
  const dirty = changedLabels(base, form, type).length > 0 || base.type !== form.type;

  useEffect(() => {
    firstField.current?.focus();
  }, []);

  const set = (name: string, value: FieldValue) => setForm((f) => ({ ...f, values: { ...f.values, [name]: value } }));

  const requestClose = () => {
    if (dirty && !saving) setConfirmDiscard(true);
    else onClose();
  };

  const save = async () => {
    const local = check(form, type);
    setErrors(local);
    setFailure(null);
    if (Object.keys(local).length > 0 || !type) {
      focusFirstError(local);
      return;
    }
    if (editing && !dirty) {
      onClose();
      return;
    }
    const attributes = Object.fromEntries(type.attributes.map((a) => [a.name, requestValue(a, form.values[a.name])]));
    setSaving(true);
    try {
      if (!editing) {
        const { data } = await api<CiDetail>('POST', '/api/v1/cis', { type: type.name, name: form.name, attributes });
        setPendingToast(`${data.name} created`);
        onClose();
        navigate(`/ci/${data.id}`);
        return;
      }
      const { data } = await api<CiDetail>(
        'PUT',
        `/api/v1/cis/${editing.id}`,
        { name: form.name, attributes },
        { ifMatch: `"${etag}"` },
      );
      onSaved?.(data, true);
      onClose();
    } catch (error) {
      const problem = asApiError(error);
      if (editing && problem.status === 412) {
        await openConflict(editing.id);
      } else if (editing && problem.status === 409 && problem.code === 'retired') {
        setRetired(true);
      } else if (problem.status === 400 && Object.keys(problem.fieldErrors).length > 0) {
        const server = Object.fromEntries(
          Object.entries(problem.fieldErrors).map(([field, codes]) => [field, codes[0] ?? '']),
        );
        setErrors(server);
        focusFirstError(server);
      } else {
        setFailure(problem);
      }
    } finally {
      setSaving(false);
    }
  };

  const openConflict = async (id: string) => {
    try {
      const { data } = await api<CiDetail>('GET', `/api/v1/cis/${id}`);
      const theirs = formOf(
        data,
        types.find((t) => t.name === data.type),
      );
      setConflict({ theirs: data, changed: changedLabels(base, theirs, type) });
    } catch (error) {
      setFailure(asApiError(error));
    }
  };

  const review = () => {
    if (!conflict) return;
    const theirs = formOf(conflict.theirs, type);
    setMine(form);
    setReviewed(conflict.theirs);
    setBase(theirs);
    setForm(theirs);
    setEtag(conflict.theirs.eTag);
    setErrors({});
    setRetired(conflict.theirs.lifecycleState === 'Retired');
    setConflict(null);
  };

  const discardMine = () => {
    if (conflict) onSaved?.(conflict.theirs, false);
    setConflict(null);
    onClose();
  };

  const errorCount = Object.keys(errors).length;
  const attributeField = (attribute: AttributeDef) => {
    const field = `attributes.${attribute.name}`;
    const code = errors[field];
    const value = form.values[attribute.name];
    const differs = mine !== null && !same(mine.values[attribute.name], value);
    return (
      <AttributeField
        key={attribute.name}
        attribute={attribute}
        value={value ?? null}
        error={code ? fieldMessage(code, attribute) : null}
        changed={differs}
        yours={
          differs && mine ? (
            <span className={styles.yours}>
              Yours: <strong>{display(mine.values[attribute.name])}</strong>
              <Button
                size="small"
                appearance="transparent"
                onClick={() => set(attribute.name, mine.values[attribute.name] ?? null)}
              >
                Use mine
              </Button>
            </span>
          ) : null
        }
        onChange={(v) => set(attribute.name, v)}
      />
    );
  };

  const builtIn = type?.attributes.filter((a) => a.builtIn) ?? [];
  const custom = type?.attributes.filter((a) => !a.builtIn) ?? [];
  const nameDiffers = mine !== null && mine.name.trim() !== form.name.trim();

  return (
    <>
      <OverlayDrawer
        open
        position="end"
        className={styles.drawer}
        aria-labelledby="ci-panel-title"
        onOpenChange={(_, data) => (data.open ? undefined : requestClose())}
      >
        <DrawerHeader>
          <DrawerHeaderTitle
            heading={{ id: 'ci-panel-title' }}
            action={<Button appearance="subtle" aria-label="Close" icon={<DismissRegular />} onClick={requestClose} />}
          >
            {editing ? `Edit ${editing.name}` : 'New CI'}
          </DrawerHeaderTitle>
        </DrawerHeader>
        <DrawerBody className={styles.body}>
          {errorCount > 1 ? (
            <Notice intent="error">
              <strong>{editing ? 'The changes aren’t saved yet.' : 'The CI isn’t created yet.'}</strong> Fix the{' '}
              {errorCount} fields marked below.
            </Notice>
          ) : null}
          {reviewed ? (
            <Notice intent="warning">
              <strong>
                {who(reviewed)} saved changes at {formatUtcTime(reviewed.changedAt)} UTC.
              </strong>{' '}
              The fields marked Changed now show their values, with yours beneath. Save keeps what the fields show.
            </Notice>
          ) : null}
          {retired ? (
            <Notice intent="warning">This CI was retired while you were editing. Restore it to make changes.</Notice>
          ) : null}
          {failure ? <ErrorBar error={failure} onRetry={() => void save()} /> : null}

          {editing ? (
            <Field label="Type" hint="A CI’s type can’t change.">
              <Input readOnly value={editing.type} contentBefore={<TypeIcon type={editing.type} types={types} />} />
            </Field>
          ) : (
            <TypePicker
              types={types}
              value={form.type}
              inputRef={firstField}
              error={errors.type ? 'Choose a type.' : null}
              onChange={(name) => setForm((f) => ({ ...f, type: name, values: {} }))}
            />
          )}

          <Field
            label={{
              children: (
                <span className={styles.label}>
                  Name
                  {nameDiffers ? <ChangedBadge /> : null}
                </span>
              ),
            }}
            required
            data-field="ci-field-name"
            {...(errors.name
              ? { validationState: 'error' as const, validationMessage: fieldMessage(errors.name, null) }
              : {
                  hint:
                    nameDiffers && mine
                      ? `Yours: ${mine.name}`
                      : 'Up to 400 characters. Names don’t need to be unique.',
                })}
          >
            <Input
              {...(editing ? { input: { ref: firstField } } : {})}
              value={form.name}
              maxLength={400}
              onChange={(_, data) => setForm((f) => ({ ...f, name: data.value }))}
            />
          </Field>

          {builtIn.length > 0 ? (
            <section className={styles.group} aria-labelledby="ci-panel-builtin">
              <h3 id="ci-panel-builtin" className={styles.groupTitle}>
                {type?.name} attributes
              </h3>
              {builtIn.map(attributeField)}
            </section>
          ) : null}
          {custom.length > 0 ? (
            <section className={styles.group} aria-labelledby="ci-panel-custom">
              <h3 id="ci-panel-custom" className={styles.groupTitle}>
                Custom attributes
              </h3>
              <Text size={200}>Added to {type?.name} by an Admin.</Text>
              {custom.map(attributeField)}
            </section>
          ) : null}
        </DrawerBody>
        <DrawerFooter>
          <Button appearance="primary" disabled={saving || retired} onClick={() => void save()}>
            {saving ? <Spinner size="tiny" aria-label="Saving" /> : null}
            {editing ? 'Save' : 'Create'}
          </Button>
          <Button onClick={requestClose}>Cancel</Button>
        </DrawerFooter>
      </OverlayDrawer>

      <Dialog open={conflict !== null} modalType="alert">
        <DialogSurface aria-describedby="ci-conflict-body">
          <DialogBody>
            <DialogTitle>This CI changed while you were editing</DialogTitle>
            <DialogContent id="ci-conflict-body">
              {conflict
                ? `${who(conflict.theirs)} saved changes to ${list(conflict.changed)} at ${formatUtcTime(conflict.theirs.changedAt)} UTC. Your edits are not saved yet.`
                : null}
            </DialogContent>
            <DialogActions>
              <Button onClick={discardMine}>Discard mine</Button>
              <Button appearance="primary" onClick={review}>
                Review their changes
              </Button>
            </DialogActions>
          </DialogBody>
        </DialogSurface>
      </Dialog>

      <Dialog open={confirmDiscard} modalType="alert">
        <DialogSurface aria-describedby="ci-discard-body">
          <DialogBody>
            <DialogTitle>Discard your changes?</DialogTitle>
            <DialogContent id="ci-discard-body">
              {editing
                ? `Your changes to ${editing.name} aren’t saved yet. If you close the panel, they’re lost.`
                : 'This new CI isn’t created yet. If you close the panel, what you entered is lost.'}
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

function who(theirs: CiDetail): string {
  return theirs.changedBy?.displayName ?? 'Someone else';
}

function focusFirstError(errors: Record<string, string>) {
  const first = Object.keys(errors)[0];
  if (!first) return;
  window.setTimeout(() => {
    const id =
      first === 'name'
        ? 'ci-field-name'
        : first === 'type'
          ? 'ci-field-type'
          : `ci-field-${first.slice('attributes.'.length)}`;
    const element = document.querySelector<HTMLElement>(
      `[data-field="${id}"] input, [data-field="${id}"] button, [data-field="${id}"]`,
    );
    element?.focus();
  }, 0);
}

function ChangedBadge() {
  return (
    <Badge appearance="tint" color="severe" size="small">
      Changed
    </Badge>
  );
}

function TypePicker({
  types,
  value,
  inputRef,
  error,
  onChange,
}: {
  types: CiType[];
  value: string;
  inputRef: Ref<HTMLInputElement>;
  error: string | null;
  onChange: (name: string) => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const shown = useMemo(
    () => types.filter((t) => typed === null || t.name.toLowerCase().includes(typed.toLowerCase())),
    [types, typed],
  );
  const groups = [...categories, null].map((category) => ({
    category,
    types: shown.filter((t) => (category === null ? !categories.includes(t.category ?? '') : t.category === category)),
  }));
  return (
    <Field
      label="Type"
      required
      data-field="ci-field-type"
      {...(error ? { validationState: 'error' as const, validationMessage: error } : {})}
    >
      <Combobox
        input={{ ref: inputRef }}
        placeholder="Choose a type"
        value={typed ?? value}
        selectedOptions={value ? [value] : []}
        onChange={(event) => setTyped(event.target.value)}
        onOpenChange={(_, data) => {
          if (!data.open) setTyped(null);
        }}
        onOptionSelect={(_, data) => {
          setTyped(null);
          if (data.optionValue) onChange(data.optionValue);
        }}
      >
        {groups
          .filter((g) => g.types.length > 0)
          .map((g) => (
            <OptionGroup key={g.category ?? 'Other'} label={g.category ?? 'Other'}>
              {g.types.map((t) => (
                <Option key={t.name} value={t.name} text={t.name}>
                  <span className={styles.option}>
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

function AttributeField({
  attribute,
  value,
  error,
  changed,
  yours,
  onChange,
}: {
  attribute: AttributeDef;
  value: FieldValue;
  error: string | null;
  changed: boolean;
  yours: ReactElement | null;
  onChange: (value: FieldValue) => void;
}) {
  const styles = useStyles();
  const id = `ci-field-${attribute.name}`;
  const label = {
    children: (
      <span className={styles.label}>
        {attribute.label}
        {changed ? <ChangedBadge /> : null}
      </span>
    ),
  };
  const hint: ReactElement | string | null =
    yours ?? (attribute.dataType === 'ciRef' && attribute.refType ? `A ${attribute.refType} CI.` : null);
  const state = {
    ...(error ? { validationState: 'error' as const, validationMessage: error } : {}),
    ...(hint && !error ? { hint } : {}),
  };

  if (attribute.dataType === 'bool') {
    return (
      <Field label={label} required={attribute.required} data-field={id} {...state}>
        <RadioGroup
          layout="horizontal"
          value={value === true ? 'yes' : value === false ? 'no' : 'unset'}
          onChange={(_, data) => onChange(data.value === 'yes' ? true : data.value === 'no' ? false : null)}
        >
          <Radio value="yes" label="Yes" />
          <Radio value="no" label="No" />
          {attribute.required ? null : <Radio value="unset" label="Not set" />}
        </RadioGroup>
      </Field>
    );
  }
  if (attribute.dataType === 'enum') {
    const current = typeof value === 'string' ? value : '';
    return (
      <Field label={label} required={attribute.required} data-field={id} {...state}>
        <Dropdown
          value={current === '' ? (attribute.required ? '' : 'Not set') : current}
          selectedOptions={[current]}
          placeholder="Choose a value"
          onOptionSelect={(_, data) => onChange(data.optionValue ?? '')}
        >
          {attribute.required ? null : (
            <Option value="" text="Not set">
              Not set
            </Option>
          )}
          {(attribute.choices ?? []).map((choice) => (
            <Option key={choice} value={choice}>
              {choice}
            </Option>
          ))}
        </Dropdown>
      </Field>
    );
  }
  if (attribute.dataType === 'ciRef') {
    return (
      <Field label={label} required={attribute.required} data-field={id} {...state}>
        <LinkPicker attribute={attribute} value={value as LinkValue | null} onChange={onChange} />
      </Field>
    );
  }
  return (
    <Field label={label} required={attribute.required} data-field={id} {...state}>
      <Input
        type={attribute.dataType === 'date' ? 'date' : 'text'}
        {...(attribute.dataType === 'number' ? { inputMode: 'decimal' as const } : {})}
        value={typeof value === 'string' ? value : ''}
        onChange={(_, data) => onChange(data.value)}
      />
    </Field>
  );
}

/**
 * A link to another CI: a Combobox that searches CI names as you type (the attribute's type only, if it
 * names one; never retired CIs), each option with its type, and a clear button.
 */
function LinkPicker({
  attribute,
  value,
  onChange,
}: {
  attribute: AttributeDef;
  value: LinkValue | null;
  onChange: (value: FieldValue) => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<CiSummary[]>([]);
  const [searching, setSearching] = useState(false);
  const request = useRef(0);

  useEffect(() => {
    if (!open) return undefined;
    const current = ++request.current;
    const timer = window.setTimeout(() => {
      const query = new URLSearchParams({ q: typed ?? '', limit: '10' });
      if (attribute.refType) query.append('type', attribute.refType);
      setSearching(true);
      api<CiPageData>('GET', `/api/v1/cis?${query.toString()}`)
        .then(({ data }) => {
          if (request.current === current) setOptions(data.items);
        })
        .catch(() => {
          if (request.current === current) setOptions([]);
        })
        .finally(() => {
          if (request.current === current) setSearching(false);
        });
    }, 250);
    return () => window.clearTimeout(timer);
  }, [open, typed, attribute.refType]);

  return (
    <Combobox
      clearable
      placeholder={attribute.refType ? `Search ${attribute.refType} CIs` : 'Search CIs'}
      value={typed ?? value?.name ?? ''}
      selectedOptions={value ? [value.id] : []}
      onChange={(event) => setTyped(event.target.value)}
      onOpenChange={(_, data) => {
        setOpen(data.open);
        if (!data.open) setTyped(null);
      }}
      onOptionSelect={(_, data) => {
        setTyped(null);
        const chosen = options.find((o) => o.id === data.optionValue);
        onChange(chosen ? { id: chosen.id, name: chosen.name, type: chosen.type } : null);
      }}
    >
      {options.length === 0 ? (
        <Option disabled value="" text="">
          {searching ? 'Searching…' : 'No matching CIs'}
        </Option>
      ) : (
        options.map((o) => (
          <Option key={o.id} value={o.id} text={o.name}>
            <span className={styles.option}>
              <TypeIcon type={o.type} types={null} />
              {o.name}
              <span className={styles.optionType}>{o.type}</span>
            </span>
          </Option>
        ))
      )}
    </Combobox>
  );
}

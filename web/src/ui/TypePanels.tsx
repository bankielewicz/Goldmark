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
  Switch,
  Textarea,
  makeStyles,
  mergeClasses,
  tokens,
} from '@fluentui/react-components';
import { DismissRegular } from '@fluentui/react-icons';
import { useEffect, useRef, useState, type KeyboardEvent, type ReactNode } from 'react';

import { api, ApiError } from '../api';
import { formatAgo } from '../format';
import { ErrorBar, Notice } from './common';
import {
  asApiError,
  categories,
  IconByKey,
  kindLabels,
  TypeIcon,
  typeIcons,
  type AttributeDef,
  type AttributeKind,
  type CiType,
  type RelationshipType,
  type TypesUsage,
} from './inventory';

/**
 * Screen 14's side panels (slice 5b boards `NewType*`, `Add*`, `EditChoice`, `EditConflict`, `NewRel`; DEC-045): an
 * overlay drawer at every width, 480 px, as slice 5's CI panel. Values are checked here as the server will, then by
 * the server, whose field errors land on their fields. A change needs the version the page read (If-Match): a 412
 * opens the "changed while you were editing" dialog, whose "Review their changes" loads their values and marks
 * each field that differs from what was typed. Closing with unsaved changes asks first.
 */

const useStyles = makeStyles({
  drawer: { width: '480px', maxWidth: '100vw' },
  body: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalL,
    paddingBottom: tokens.spacingVerticalXL,
  },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  label: { display: 'inline-flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  legend: { fontWeight: tokens.fontWeightSemibold, padding: 0, marginBottom: tokens.spacingVerticalXS },
  fieldset: {
    border: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  required: { color: tokens.colorPaletteRedForeground3 },
  hint: {
    color: tokens.colorNeutralForeground3,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  error: {
    color: tokens.colorPaletteRedForeground1,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  icons: { display: 'grid', gridTemplateColumns: 'repeat(7, minmax(0, 1fr))', gap: tokens.spacingHorizontalSNudge },
  icon: {
    height: '44px',
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    borderRadius: tokens.borderRadiusMedium,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke1}`,
    backgroundColor: tokens.colorNeutralBackground1,
    color: tokens.colorNeutralForeground2,
    fontSize: '20px',
    cursor: 'pointer',
    ':focus-visible': { outline: `${tokens.strokeWidthThick} solid ${tokens.colorStrokeFocus2}`, outlineOffset: '1px' },
  },
  iconChecked: {
    border: `${tokens.strokeWidthThick} solid ${tokens.colorBrandStroke1}`,
    backgroundColor: tokens.colorBrandBackground2,
    color: tokens.colorNeutralForeground1,
  },
  choices: {
    listStyleType: 'none',
    margin: 0,
    padding: 0,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  choice: {
    height: '32px',
    boxSizing: 'border-box',
    padding: `0 ${tokens.spacingHorizontalXS} 0 ${tokens.spacingHorizontalM}`,
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    backgroundColor: tokens.colorNeutralBackground3,
    borderRadius: tokens.borderRadiusMedium,
  },
  choiceText: { flex: 1, minWidth: 0, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' },
  choiceCount: { color: tokens.colorNeutralForeground3, fontSize: tokens.fontSizeBase200 },
  choiceReason: { paddingLeft: tokens.spacingHorizontalM },
  addChoice: { display: 'flex', gap: tokens.spacingHorizontalS },
  addChoiceInput: { flex: 1, minWidth: 0 },
  option: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, width: '100%' },
  counter: { alignSelf: 'flex-end' },
  yours: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
});

/** Field errors keyed as the API reports them. */
type Errors = Record<string, string>;

function serverErrors(problem: ApiError): Errors {
  return Object.fromEntries(Object.entries(problem.fieldErrors).map(([field, codes]) => [field, codes[0] ?? '']));
}

function focusFirstError(errors: Errors) {
  const first = Object.keys(errors)[0];
  if (!first) return;
  window.setTimeout(() => {
    document
      .querySelector<HTMLElement>(
        `[data-field="${first}"] input, [data-field="${first}"] textarea, [data-field="${first}"] button`,
      )
      ?.focus();
  }, 0);
}

function ChangedBadge() {
  return (
    <Badge appearance="tint" color="severe" size="small">
      Changed
    </Badge>
  );
}

/** The drawer, its footer, the error summary and the "Discard your changes?" dialog every panel shares. */
function PanelShell({
  id,
  title,
  primary,
  saving,
  disabled,
  dirty,
  errorCount,
  summary,
  onSave,
  onClose,
  children,
}: {
  id: string;
  title: string;
  primary: string;
  saving: boolean;
  disabled?: boolean;
  dirty: boolean;
  errorCount: number;
  summary: string;
  onSave: () => void;
  onClose: () => void;
  children: ReactNode;
}) {
  const styles = useStyles();
  const [confirmDiscard, setConfirmDiscard] = useState(false);
  const requestClose = () => {
    if (dirty && !saving) setConfirmDiscard(true);
    else onClose();
  };
  return (
    <>
      <OverlayDrawer
        open
        position="end"
        className={styles.drawer}
        aria-labelledby={`${id}-title`}
        onOpenChange={(_, data) => (data.open ? undefined : requestClose())}
      >
        <DrawerHeader>
          <DrawerHeaderTitle
            heading={{ id: `${id}-title` }}
            action={<Button appearance="subtle" aria-label="Close" icon={<DismissRegular />} onClick={requestClose} />}
          >
            {title}
          </DrawerHeaderTitle>
        </DrawerHeader>
        <DrawerBody className={styles.body}>
          {errorCount > 1 ? (
            <Notice intent="error">
              <strong>{summary}</strong> Fix the {errorCount} fields marked below.
            </Notice>
          ) : null}
          {children}
        </DrawerBody>
        <DrawerFooter>
          <Button appearance="primary" disabled={saving || disabled === true} onClick={onSave}>
            {saving ? <Spinner size="tiny" aria-label="Saving" /> : null}
            {primary}
          </Button>
          <Button onClick={requestClose}>Cancel</Button>
        </DrawerFooter>
      </OverlayDrawer>
      <Dialog open={confirmDiscard} modalType="alert">
        <DialogSurface aria-describedby={`${id}-discard-body`}>
          <DialogBody>
            <DialogTitle>Discard your changes?</DialogTitle>
            <DialogContent id={`${id}-discard-body`}>
              What you entered isn’t saved yet. If you close the panel, it’s lost.
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

/** The "changed while you were editing" dialog (slice 5's, DEC-044), for an attribute or a relationship type. */
function ConflictDialog({
  what,
  text,
  onDiscard,
  onReview,
}: {
  what: string;
  text: string | null;
  onDiscard: () => void;
  onReview: () => void;
}) {
  return (
    <Dialog open={text !== null} modalType="alert">
      <DialogSurface aria-describedby="type-conflict-body">
        <DialogBody>
          <DialogTitle>This {what} changed while you were editing</DialogTitle>
          <DialogContent id="type-conflict-body">{text}</DialogContent>
          <DialogActions>
            <Button onClick={onDiscard}>Discard mine</Button>
            <Button appearance="primary" onClick={onReview}>
              Review their changes
            </Button>
          </DialogActions>
        </DialogBody>
      </DialogSurface>
    </Dialog>
  );
}

function conflictText(
  label: string,
  theirs: { changedBy: AttributeDef['changedBy']; changedAt: string | null },
  changed: string[],
) {
  const who = theirs.changedBy?.displayName ?? 'Someone else';
  const when = theirs.changedAt ? ` ${formatAgo(theirs.changedAt)}` : '';
  return `${who} changed ${label}${when}${changed.length > 0 ? ` (${changed.join(', ')})` : ''}. Review their changes, or discard yours.`;
}

/** A type picker with "Any type" first (an attribute's link target, a relationship type's ends), types grouped as §9.3. */
function TypeSelect({
  field,
  label,
  hint,
  value,
  types,
  error,
  onChange,
}: {
  field: string;
  label: string;
  hint: string;
  value: string | null;
  types: CiType[];
  error: string | null;
  onChange: (type: string | null) => void;
}) {
  const styles = useStyles();
  const [typed, setTyped] = useState<string | null>(null);
  const shown = types.filter((t) => typed === null || t.name.toLowerCase().includes(typed.toLowerCase()));
  const groups = [...categories, null]
    .map((category) => ({
      category,
      types: shown.filter((t) =>
        category === null ? !categories.includes(t.category ?? '') : t.category === category,
      ),
    }))
    .filter((g) => g.types.length > 0);
  return (
    <Field
      label={label}
      required
      data-field={field}
      {...(error ? { validationState: 'error' as const, validationMessage: error } : { hint })}
    >
      <Combobox
        value={typed ?? value ?? 'Any type'}
        selectedOptions={[value ?? '']}
        onChange={(event) => setTyped(event.target.value)}
        onOpenChange={(_, data) => {
          if (!data.open) setTyped(null);
        }}
        onOptionSelect={(_, data) => {
          setTyped(null);
          onChange(data.optionValue ? data.optionValue : null);
        }}
      >
        <Option value="" text="Any type">
          Any type
        </Option>
        {groups.map((g) => (
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

/* ------------------------------------------------------------------ New CI type */

const typeNamePattern = /^[A-Z][A-Za-z0-9]{0,99}$/;

function typeFieldMessage(field: string, code: string): string {
  switch (`${field}:${code}`) {
    case 'name:Required':
      return 'Enter a name.';
    case 'name:DuplicateName':
      return 'A type with this name already exists.';
    case 'name:InvalidName':
      return 'Use letters and digits, starting with a capital letter.';
    case 'icon:InvalidIcon':
      return 'Choose one of the icons.';
    default:
      return field === 'category' ? 'Choose a group.' : 'This value isn’t valid.';
  }
}

/** The New CI type panel (boards `NewType`, `NewTypeError`): a name that never changes, one of §9.3's groups, an icon. */
export function NewTypePanel({
  types,
  onClose,
  onSaved,
}: {
  types: CiType[];
  onClose: () => void;
  onSaved: (type: CiType) => void;
}) {
  const styles = useStyles();
  const [name, setName] = useState('');
  const [category, setCategory] = useState<string | null>(null);
  const [icon, setIcon] = useState('generic');
  const [errors, setErrors] = useState<Errors>({});
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const first = useRef<HTMLInputElement>(null);
  useEffect(() => first.current?.focus(), []);

  const save = async () => {
    const local: Errors = {};
    const trimmed = name.trim();
    if (trimmed === '') local.name = 'Required';
    else if (!typeNamePattern.test(trimmed)) local.name = 'InvalidName';
    else if (types.some((t) => t.name.toLowerCase() === trimmed.toLowerCase())) local.name = 'DuplicateName';
    if (!category) local.category = 'Required';
    setErrors(local);
    setFailure(null);
    if (Object.keys(local).length > 0) {
      focusFirstError(local);
      return;
    }
    setSaving(true);
    try {
      const { data } = await api<CiType>('POST', '/api/v1/types', { name: trimmed, category, icon });
      onSaved(data);
    } catch (error) {
      const problem = asApiError(error);
      if (problem.status === 400 && Object.keys(problem.fieldErrors).length > 0) {
        const server = serverErrors(problem);
        setErrors(server);
        focusFirstError(server);
      } else {
        setFailure(problem);
      }
    } finally {
      setSaving(false);
    }
  };

  const message = (field: string) => (errors[field] ? typeFieldMessage(field, errors[field]) : null);
  return (
    <PanelShell
      id="new-type"
      title="New CI type"
      primary="Create"
      saving={saving}
      dirty={name.trim() !== '' || category !== null || icon !== 'generic'}
      errorCount={Object.keys(errors).length}
      summary="The type isn’t created yet."
      onSave={() => void save()}
      onClose={onClose}
    >
      {failure ? <ErrorBar error={failure} onRetry={() => void save()} /> : null}
      <Field
        label="Name"
        required
        data-field="name"
        {...(message('name')
          ? { validationState: 'error' as const, validationMessage: message('name') }
          : {
              hint: 'Letters and digits, starting with a capital: Printer, LoadBalancerPool. It can’t change later; the API and links use it.',
            })}
      >
        <Input
          value={name}
          maxLength={100}
          input={{ ref: first, className: styles.mono }}
          onChange={(_, data) => setName(data.value)}
        />
      </Field>
      <Field
        label="Group"
        required
        data-field="category"
        {...(message('category') ? { validationState: 'error' as const, validationMessage: message('category') } : {})}
      >
        <Dropdown
          placeholder="Choose a group"
          value={category ?? ''}
          selectedOptions={category ? [category] : []}
          onOptionSelect={(_, data) => setCategory(data.optionValue ?? null)}
        >
          {categories.map((c) => (
            <Option key={c} value={c}>
              {c}
            </Option>
          ))}
        </Dropdown>
      </Field>
      <IconPicker value={icon} error={message('icon')} onChange={setIcon} />
    </PanelShell>
  );
}

/** A grid of the type icons as one radio group: arrow keys move and choose, Tab leaves it. */
function IconPicker({
  value,
  error,
  onChange,
}: {
  value: string;
  error: string | null;
  onChange: (icon: string) => void;
}) {
  const styles = useStyles();
  const buttons = useRef<(HTMLButtonElement | null)[]>([]);
  const move = (event: KeyboardEvent, index: number) => {
    const step = { ArrowRight: 1, ArrowDown: 7, ArrowLeft: -1, ArrowUp: -7 }[event.key];
    if (step === undefined && event.key !== 'Home' && event.key !== 'End') return;
    event.preventDefault();
    const next =
      event.key === 'Home'
        ? 0
        : event.key === 'End'
          ? typeIcons.length - 1
          : Math.min(typeIcons.length - 1, Math.max(0, index + (step ?? 0)));
    const icon = typeIcons[next];
    if (!icon) return;
    onChange(icon.key);
    buttons.current[next]?.focus();
  };
  return (
    <div className={styles.fieldset} data-field="icon">
      <span id="new-type-icon" className={styles.legend}>
        Icon
      </span>
      <div
        role="radiogroup"
        aria-labelledby="new-type-icon"
        aria-describedby="new-type-icon-hint"
        className={styles.icons}
      >
        {typeIcons.map((icon, index) => (
          <button
            key={icon.key}
            ref={(element) => {
              buttons.current[index] = element;
            }}
            type="button"
            role="radio"
            aria-checked={icon.key === value}
            aria-label={icon.name}
            title={icon.name}
            tabIndex={icon.key === value ? 0 : -1}
            className={mergeClasses(styles.icon, icon.key === value && styles.iconChecked)}
            onClick={() => onChange(icon.key)}
            onKeyDown={(event) => move(event, index)}
          >
            <IconByKey icon={icon.key} />
          </button>
        ))}
      </div>
      <span id="new-type-icon-hint" className={error ? styles.error : styles.hint}>
        {error ?? 'Shown beside the type’s CIs everywhere.'}
      </span>
    </div>
  );
}

/* ------------------------------------------------------------------ Add and Edit attribute */

const attributeNamePattern = /^[a-z][A-Za-z0-9]{0,63}$/;

/** The name an attribute's label suggests: "Cost centre" → costCentre, "vCPUs reserved" → vcpusReserved. */
export function suggestName(label: string): string {
  const words = label
    .normalize('NFD')
    .replace(/[̀-ͯ]/g, '')
    .split(/[^A-Za-z0-9]+/)
    .filter(Boolean);
  const joined = words
    .map((word, i) => (i === 0 ? word.toLowerCase() : word.charAt(0).toUpperCase() + word.slice(1).toLowerCase()))
    .join('')
    .replace(/^[0-9]+/, '');
  return joined.slice(0, 64);
}

interface AttributeForm {
  label: string;
  required: boolean;
  choices: string[];
}

function formOf(attribute: AttributeDef): AttributeForm {
  return { label: attribute.label, required: attribute.required, choices: attribute.choices ?? [] };
}

function changedFields(a: AttributeForm, b: AttributeForm): string[] {
  return [
    ...(a.label.trim() === b.label.trim() ? [] : ['Label']),
    ...(a.required === b.required ? [] : ['Required']),
    ...(a.choices.join('\n') === b.choices.join('\n') ? [] : ['Choices']),
  ];
}

function attributeFieldMessage(field: string, code: string, typeName: string): string {
  switch (`${field}:${code}`) {
    case 'label:Required':
      return 'Enter a label.';
    case 'label:DuplicateLabel':
      return 'This label is already used.';
    case 'label:InvalidLabel':
      return 'Use up to 100 characters, without line breaks.';
    case 'name:Required':
      return 'Enter a name.';
    case 'name:DuplicateName':
      return `This name is already used by another attribute of ${typeName}.`;
    case 'name:InvalidName':
      return 'Use letters and digits, starting with a small letter.';
    case 'dataType:Required':
      return 'Choose a kind.';
    case 'choices:Required':
      return 'Add at least one choice.';
    case 'choices:ChoiceInUse':
      return 'A choice some CI uses can’t be removed. Close the panel to see the counts again.';
    case 'choices:InvalidChoices':
      return 'Choices are 100 characters at most, without line breaks; at most 100 of them.';
    case 'refType:UnknownType':
      return 'That type no longer exists. Choose another.';
    default:
      return code.startsWith('Twice:') ? `${code.slice('Twice:'.length)} is listed twice.` : 'This value isn’t valid.';
  }
}

export type AttributeMode = { kind: 'add'; type: CiType } | { kind: 'edit'; type: CiType; attribute: AttributeDef };

/**
 * Add attribute and Edit attribute (boards `AddChoices*`, `AddLink`, `AddErrors`, `EditChoice`, `EditConflict`): a
 * label, a name suggested from it that never changes, a kind and link target that never change, the required flag
 * and, for a list of choices, its choices (one some CI uses can't be removed: DEC-045 item 2).
 */
export function AttributePanel({
  mode,
  types,
  usage,
  onClose,
  onSaved,
}: {
  mode: AttributeMode;
  types: CiType[];
  usage: TypesUsage | null;
  onClose: () => void;
  onSaved: (attribute: AttributeDef, changed: boolean) => void;
}) {
  const styles = useStyles();
  const editing = mode.kind === 'edit' ? mode.attribute : null;
  const type = mode.type;
  const [base, setBase] = useState<AttributeForm>(() =>
    editing ? formOf(editing) : { label: '', required: false, choices: [] },
  );
  const [form, setForm] = useState<AttributeForm>(base);
  const [name, setName] = useState(editing?.name ?? '');
  const [nameTouched, setNameTouched] = useState(false);
  const [kind, setKind] = useState<AttributeKind | null>(editing?.dataType ?? null);
  const [refType, setRefType] = useState<string | null>(editing?.refType ?? null);
  const [newChoice, setNewChoice] = useState('');
  const [etag, setEtag] = useState(editing?.eTag ?? '');
  const [errors, setErrors] = useState<Errors>({});
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const [gone, setGone] = useState(false);
  const [conflict, setConflict] = useState<{ theirs: AttributeDef; changed: string[] } | null>(null);
  const [mine, setMine] = useState<AttributeForm | null>(null);
  const first = useRef<HTMLInputElement>(null);
  useEffect(() => first.current?.focus(), []);

  const typeUsage = usage?.types.find((t) => t.name === type.name);
  const cis = typeUsage?.cis ?? 0;
  const attributeUsage = editing ? typeUsage?.attributes.find((a) => a.name === editing.name) : undefined;
  const withoutValue = cis - (attributeUsage?.values ?? 0);
  const choiceCount = (choice: string) => attributeUsage?.choices?.[choice] ?? 0;
  const dirty = changedFields(base, form).length > 0 || (!editing && (name !== '' || kind !== null));

  const setLabel = (label: string) => {
    setForm((f) => ({ ...f, label }));
    if (!editing && !nameTouched) setName(suggestName(label));
  };

  const addChoice = () => {
    const choice = newChoice.trim();
    if (choice === '') return;
    if (form.choices.includes(choice)) {
      setErrors((e) => ({ ...e, choices: `Twice:${choice}` }));
      return;
    }
    setForm((f) => ({ ...f, choices: [...f.choices, choice] }));
    setNewChoice('');
    setErrors((e) => Object.fromEntries(Object.entries(e).filter(([field]) => field !== 'choices')));
  };

  const check = (): Errors => {
    const local: Errors = {};
    if (form.label.trim() === '') local.label = 'Required';
    else if (form.label.trim().length > 100) local.label = 'InvalidLabel';
    else if (
      type.attributes.some((a) => a.name !== editing?.name && a.label.toLowerCase() === form.label.trim().toLowerCase())
    ) {
      local.label = 'DuplicateLabel';
    }
    if (!editing) {
      if (name.trim() === '') local.name = 'Required';
      else if (!attributeNamePattern.test(name.trim())) local.name = 'InvalidName';
      else if (type.attributes.some((a) => a.name === name.trim())) local.name = 'DuplicateName';
      if (!kind) local.dataType = 'Required';
    }
    if ((editing?.dataType ?? kind) === 'enum' && form.choices.length === 0) local.choices = 'Required';
    return local;
  };

  const save = async () => {
    const local = check();
    setErrors(local);
    setFailure(null);
    if (Object.keys(local).length > 0) {
      focusFirstError(local);
      return;
    }
    if (editing && changedFields(base, form).length === 0) {
      onClose();
      return;
    }
    const choices = (editing?.dataType ?? kind) === 'enum' ? form.choices : null;
    setSaving(true);
    try {
      if (!editing) {
        const { data } = await api<AttributeDef>('POST', `/api/v1/types/${encodeURIComponent(type.name)}/attributes`, {
          name: name.trim(),
          label: form.label.trim(),
          dataType: kind,
          required: form.required,
          choices,
          refType: kind === 'ciRef' ? refType : null,
        });
        onSaved(data, true);
        return;
      }
      const { data } = await api<AttributeDef>(
        'PUT',
        `/api/v1/types/${encodeURIComponent(type.name)}/attributes/${encodeURIComponent(editing.name)}`,
        { label: form.label.trim(), required: form.required, choices },
        { ifMatch: `"${etag}"` },
      );
      onSaved(data, true);
    } catch (error) {
      const problem = asApiError(error);
      if (editing && problem.status === 412) {
        await openConflict();
      } else if (editing && problem.status === 404) {
        setGone(true);
      } else if (problem.status === 400 && Object.keys(problem.fieldErrors).length > 0) {
        const server = serverErrors(problem);
        setErrors(server);
        focusFirstError(server);
      } else {
        setFailure(problem);
      }
    } finally {
      setSaving(false);
    }
  };

  const openConflict = async () => {
    try {
      const { data } = await api<{ types: CiType[] }>('GET', '/api/v1/types');
      const theirs = data.types.find((t) => t.name === type.name)?.attributes.find((a) => a.name === editing?.name);
      if (!theirs) setGone(true);
      else setConflict({ theirs, changed: changedFields(base, formOf(theirs)) });
    } catch (error) {
      setFailure(asApiError(error));
    }
  };

  const review = () => {
    if (!conflict) return;
    const theirs = formOf(conflict.theirs);
    setMine(form);
    setBase(theirs);
    setForm(theirs);
    setEtag(conflict.theirs.eTag);
    setErrors({});
    setConflict(null);
  };

  const message = (field: string) => (errors[field] ? attributeFieldMessage(field, errors[field], type.name) : null);
  const labelDiffers = mine !== null && mine.label.trim() !== form.label.trim();
  const requiredDiffers = mine !== null && mine.required !== form.required;
  const choicesDiffer = mine !== null && mine.choices.join('\n') !== form.choices.join('\n');
  const shownKind = editing?.dataType ?? kind;
  const requiredHint =
    form.required && cis > 0 && (!editing || !base.required)
      ? `CIs that have no value keep it until they’re next saved; then it must be filled in. ${withoutValue.toLocaleString('en-US')} ${type.name} ${withoutValue === 1 ? 'CI has' : 'CIs have'} no ${form.label.trim() || 'value'}.`
      : null;

  const changedLabel = (text: string, differs: boolean) => ({
    children: (
      <span className={styles.label}>
        {text}
        {differs ? <ChangedBadge /> : null}
      </span>
    ),
  });
  const yours = (text: string, use: () => void) => (
    <span className={styles.yours}>
      Yours: <strong>{text}</strong>
      <Button size="small" appearance="transparent" onClick={use}>
        Use mine
      </Button>
    </span>
  );

  return (
    <>
      <PanelShell
        id="attribute"
        title={editing ? `Edit ${editing.label}` : `Add attribute to ${type.name}`}
        primary={editing ? 'Save' : 'Add'}
        saving={saving}
        disabled={gone}
        dirty={dirty}
        errorCount={Object.keys(errors).length}
        summary={editing ? 'The changes aren’t saved yet.' : 'The attribute isn’t added yet.'}
        onSave={() => void save()}
        onClose={onClose}
      >
        {mine ? (
          <Notice intent="warning">
            The fields marked Changed now show their values, with yours beneath. Save keeps what the fields show.
          </Notice>
        ) : null}
        {gone ? <Notice intent="warning">This attribute was deleted while you were editing.</Notice> : null}
        {failure ? <ErrorBar error={failure} onRetry={() => void save()} /> : null}

        <Field
          label={changedLabel('Label', labelDiffers)}
          required
          data-field="label"
          {...(message('label')
            ? { validationState: 'error' as const, validationMessage: message('label') }
            : labelDiffers && mine
              ? { hint: yours(mine.label, () => setForm((f) => ({ ...f, label: mine.label }))) }
              : {})}
        >
          <Input
            input={{ ref: first }}
            value={form.label}
            maxLength={100}
            onChange={(_, data) => setLabel(data.value)}
          />
        </Field>

        {editing ? (
          <Field label="Name" hint="It can’t change.">
            <Input readOnly value={editing.name} input={{ className: styles.mono }} />
          </Field>
        ) : (
          <Field
            label="Name"
            required
            data-field="name"
            {...(message('name')
              ? { validationState: 'error' as const, validationMessage: message('name') }
              : {
                  hint: 'Suggested from the label. Letters and digits, starting with a small letter. It can’t change later.',
                })}
          >
            <Input
              value={name}
              maxLength={64}
              input={{ className: styles.mono }}
              onChange={(_, data) => {
                setNameTouched(true);
                setName(data.value);
              }}
            />
          </Field>
        )}

        {editing ? (
          <Field label="Kind">
            <Input readOnly value={kindLabels[editing.dataType]} />
          </Field>
        ) : (
          <Field
            label="Kind"
            required
            data-field="dataType"
            {...(message('dataType')
              ? { validationState: 'error' as const, validationMessage: message('dataType') }
              : { hint: 'It can’t change later.' })}
          >
            <RadioGroup value={kind ?? ''} onChange={(_, data) => setKind(data.value as AttributeKind)}>
              {(Object.keys(kindLabels) as AttributeKind[]).map((k) => (
                <Radio key={k} value={k} label={kindLabels[k]} />
              ))}
            </RadioGroup>
          </Field>
        )}

        <Field
          label={changedLabel('Required', requiredDiffers)}
          {...(requiredHint
            ? { validationState: 'warning' as const, validationMessage: requiredHint }
            : requiredDiffers && mine
              ? {
                  hint: yours(mine.required ? 'Required' : 'Not required', () =>
                    setForm((f) => ({ ...f, required: mine.required })),
                  ),
                }
              : {})}
        >
          <Switch
            checked={form.required}
            label="Every CI of this type needs a value"
            onChange={(_, data) => setForm((f) => ({ ...f, required: data.checked }))}
          />
        </Field>

        {shownKind === 'enum' ? (
          <div className={styles.fieldset} data-field="choices">
            <span id="attribute-choices" className={styles.legend}>
              <span className={styles.label}>
                Choices
                <span className={styles.required} aria-hidden="true">
                  *
                </span>
                {choicesDiffer ? <ChangedBadge /> : null}
              </span>
            </span>
            <ul aria-labelledby="attribute-choices" className={styles.choices}>
              {form.choices.map((choice) => {
                const count = editing && (editing.choices ?? []).includes(choice) ? choiceCount(choice) : null;
                const locked = count !== null && count > 0;
                const reason = locked
                  ? `${count.toLocaleString('en-US')} ${count === 1 ? 'CI uses' : 'CIs use'} ${choice}. Change them first.`
                  : null;
                const reasonId = `choice-reason-${form.choices.indexOf(choice)}`;
                return (
                  <li key={choice}>
                    <div className={styles.choice}>
                      <span className={styles.choiceText}>{choice}</span>
                      {editing ? (
                        <span className={styles.choiceCount}>
                          {count === null ? 'New' : `${count.toLocaleString('en-US')} ${count === 1 ? 'CI' : 'CIs'}`}
                        </span>
                      ) : null}
                      <Button
                        size="small"
                        appearance="subtle"
                        icon={<DismissRegular />}
                        aria-label={`Remove ${choice}`}
                        disabledFocusable={locked}
                        {...(reason ? { 'aria-describedby': reasonId } : {})}
                        onClick={() => setForm((f) => ({ ...f, choices: f.choices.filter((c) => c !== choice) }))}
                      />
                    </div>
                    {reason ? (
                      <span id={reasonId} className={mergeClasses(styles.hint, styles.choiceReason)}>
                        {reason}
                      </span>
                    ) : null}
                  </li>
                );
              })}
            </ul>
            <div className={styles.addChoice}>
              <Input
                className={styles.addChoiceInput}
                aria-label="New choice"
                placeholder="Add a choice"
                value={newChoice}
                maxLength={100}
                onChange={(_, data) => setNewChoice(data.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    addChoice();
                  }
                }}
              />
              {/* "Add choice", not "Add": the panel's own Add button saves the attribute. */}
              <Button aria-label="Add choice" onClick={addChoice}>
                Add
              </Button>
            </div>
            {message('choices') ? (
              <span className={styles.error} role="alert">
                {message('choices')}
              </span>
            ) : choicesDiffer && mine ? (
              yours(mine.choices.join(', ') || 'None', () => setForm((f) => ({ ...f, choices: mine.choices })))
            ) : editing ? (
              <span className={styles.hint}>A choice in use can’t be removed.</span>
            ) : null}
          </div>
        ) : null}

        {shownKind === 'ciRef' ? (
          editing ? (
            <Field label="Target type" hint="It can’t change.">
              <Input
                readOnly
                value={editing.refType ?? 'Any type'}
                {...(editing.refType ? { contentBefore: <TypeIcon type={editing.refType} types={types} /> } : {})}
              />
            </Field>
          ) : (
            <TypeSelect
              field="refType"
              label="Target type"
              hint="The CIs it can link to; “Any type” is first in the list. It can’t change later."
              value={refType}
              types={types}
              error={message('refType')}
              onChange={setRefType}
            />
          )
        ) : null}
      </PanelShell>
      <ConflictDialog
        what="attribute"
        text={conflict ? conflictText(editing?.label ?? '', conflict.theirs, conflict.changed) : null}
        onDiscard={() => {
          setConflict(null);
          if (conflict) onSaved(conflict.theirs, false);
        }}
        onReview={review}
      />
    </>
  );
}

/* ------------------------------------------------------------------ Relationship types */

const relationshipNamePattern = /^[a-z][a-z0-9_]{0,63}$/;
const maxDescription = 400;

function relationshipFieldMessage(field: string, code: string): string {
  switch (`${field}:${code}`) {
    case 'name:Required':
      return 'Enter a name.';
    case 'name:DuplicateName':
      return 'A relationship type with this name already exists.';
    case 'name:InvalidName':
      return 'Use small letters, digits and _, starting with a letter.';
    case 'from:UnknownType':
    case 'to:UnknownType':
      return 'That type no longer exists. Choose another.';
    case 'description:TooLong':
      return `Use ${maxDescription} characters at most.`;
    case 'description:InvalidDescription':
      return 'Remove the control characters.';
    default:
      return field === 'impact' ? 'Choose an impact.' : 'This value isn’t valid.';
  }
}

export type RelationshipMode = { kind: 'new' } | { kind: 'edit'; relationshipType: RelationshipType };

interface RelationshipForm {
  impact: 'Down' | 'None';
  description: string;
}

function relationshipChanges(a: RelationshipForm, b: RelationshipForm): string[] {
  return [
    ...(a.impact === b.impact ? [] : ['Impact']),
    ...(a.description.trim() === b.description.trim() ? [] : ['Description']),
  ];
}

/**
 * New relationship type (board `NewRel`) and its Edit (the same panel, its name and ends read only: DEC-045 item 4):
 * a name that never changes, From and To types with "Any type" first, the impact and a description.
 */
export function RelationshipTypePanel({
  mode,
  types,
  onClose,
  onSaved,
}: {
  mode: RelationshipMode;
  types: CiType[];
  onClose: () => void;
  onSaved: (relationshipType: RelationshipType, changed: boolean) => void;
}) {
  const styles = useStyles();
  const editing = mode.kind === 'edit' ? mode.relationshipType : null;
  const start: RelationshipForm = { impact: editing?.impact ?? 'Down', description: editing?.description ?? '' };
  const [base, setBase] = useState(start);
  const [form, setForm] = useState(start);
  const [name, setName] = useState(editing?.name ?? '');
  const [from, setFrom] = useState<string | null>(editing?.from ?? null);
  const [to, setTo] = useState<string | null>(editing?.to ?? null);
  const [etag, setEtag] = useState(editing?.eTag ?? '');
  const [errors, setErrors] = useState<Errors>({});
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [saving, setSaving] = useState(false);
  const [gone, setGone] = useState(false);
  const [conflict, setConflict] = useState<{ theirs: RelationshipType; changed: string[] } | null>(null);
  const [mine, setMine] = useState<RelationshipForm | null>(null);
  const first = useRef<HTMLInputElement>(null);
  const firstTextarea = useRef<HTMLTextAreaElement>(null);
  useEffect(() => {
    if (editing) firstTextarea.current?.focus();
    else first.current?.focus();
  }, [editing]);

  const dirty =
    relationshipChanges(base, form).length > 0 || (!editing && (name !== '' || from !== null || to !== null));

  const save = async () => {
    const local: Errors = {};
    if (!editing) {
      if (name.trim() === '') local.name = 'Required';
      else if (!relationshipNamePattern.test(name.trim())) local.name = 'InvalidName';
    }
    if (form.description.trim().length > maxDescription) local.description = 'TooLong';
    setErrors(local);
    setFailure(null);
    if (Object.keys(local).length > 0) {
      focusFirstError(local);
      return;
    }
    if (editing && relationshipChanges(base, form).length === 0) {
      onClose();
      return;
    }
    setSaving(true);
    try {
      const description = form.description.trim() === '' ? null : form.description;
      if (!editing) {
        const { data } = await api<RelationshipType>('POST', '/api/v1/relationship-types', {
          name: name.trim(),
          from,
          to,
          impact: form.impact,
          description,
        });
        onSaved(data, true);
        return;
      }
      const { data } = await api<RelationshipType>(
        'PUT',
        `/api/v1/relationship-types/${encodeURIComponent(editing.name)}`,
        { impact: form.impact, description },
        { ifMatch: `"${etag}"` },
      );
      onSaved(data, true);
    } catch (error) {
      const problem = asApiError(error);
      if (editing && problem.status === 412) {
        await openConflict();
      } else if (editing && problem.status === 404) {
        setGone(true);
      } else if (problem.status === 400 && Object.keys(problem.fieldErrors).length > 0) {
        const server = serverErrors(problem);
        setErrors(server);
        focusFirstError(server);
      } else {
        setFailure(problem);
      }
    } finally {
      setSaving(false);
    }
  };

  const openConflict = async () => {
    try {
      const { data } = await api<{ relationshipTypes: RelationshipType[] }>('GET', '/api/v1/types');
      const theirs = data.relationshipTypes.find((r) => r.name === editing?.name);
      if (!theirs) setGone(true);
      else
        setConflict({
          theirs,
          changed: relationshipChanges(base, { impact: theirs.impact, description: theirs.description ?? '' }),
        });
    } catch (error) {
      setFailure(asApiError(error));
    }
  };

  const review = () => {
    if (!conflict) return;
    const theirs = { impact: conflict.theirs.impact, description: conflict.theirs.description ?? '' };
    setMine(form);
    setBase(theirs);
    setForm(theirs);
    setEtag(conflict.theirs.eTag);
    setErrors({});
    setConflict(null);
  };

  const message = (field: string) => (errors[field] ? relationshipFieldMessage(field, errors[field]) : null);
  const impactDiffers = mine !== null && mine.impact !== form.impact;
  const descriptionDiffers = mine !== null && mine.description.trim() !== form.description.trim();
  const yours = (text: string, use: () => void) => (
    <span className={styles.yours}>
      Yours: <strong>{text}</strong>
      <Button size="small" appearance="transparent" onClick={use}>
        Use mine
      </Button>
    </span>
  );
  const typeHint = (dependent: boolean) =>
    dependent ? 'The dependent CI. “Any type” is first in the list.' : 'The CI it depends on.';

  return (
    <>
      <PanelShell
        id="relationship-type"
        title={editing ? `Edit ${editing.name}` : 'New relationship type'}
        primary={editing ? 'Save' : 'Create'}
        saving={saving}
        disabled={gone}
        dirty={dirty}
        errorCount={Object.keys(errors).length}
        summary={editing ? 'The changes aren’t saved yet.' : 'The relationship type isn’t created yet.'}
        onSave={() => void save()}
        onClose={onClose}
      >
        {mine ? (
          <Notice intent="warning">
            The fields marked Changed now show their values, with yours beneath. Save keeps what the fields show.
          </Notice>
        ) : null}
        {gone ? <Notice intent="warning">This relationship type was deleted while you were editing.</Notice> : null}
        {failure ? <ErrorBar error={failure} onRetry={() => void save()} /> : null}

        {editing ? (
          <>
            <Field label="Name" hint="It can’t change.">
              <Input readOnly value={editing.name} input={{ className: styles.mono }} />
            </Field>
            <Field label="From type" hint="It can’t change.">
              <Input readOnly value={editing.from ?? 'Any type'} />
            </Field>
            <Field label="To type" hint="It can’t change.">
              <Input readOnly value={editing.to ?? 'Any type'} />
            </Field>
          </>
        ) : (
          <>
            <Field
              label="Name"
              required
              data-field="name"
              {...(message('name')
                ? { validationState: 'error' as const, validationMessage: message('name') }
                : {
                    hint: 'Small letters, digits and _, starting with a letter: replicates_to. It can’t change later.',
                  })}
            >
              <Input
                value={name}
                maxLength={64}
                input={{ ref: first, className: styles.mono }}
                onChange={(_, data) => setName(data.value)}
              />
            </Field>
            <TypeSelect
              field="from"
              label="From type"
              hint={typeHint(true)}
              value={from}
              types={types}
              error={message('from')}
              onChange={setFrom}
            />
            <TypeSelect
              field="to"
              label="To type"
              hint={typeHint(false)}
              value={to}
              types={types}
              error={message('to')}
              onChange={setTo}
            />
          </>
        )}

        <Field
          label={{
            children: (
              <span className={styles.label}>
                Impact
                {impactDiffers ? <ChangedBadge /> : null}
              </span>
            ),
          }}
          required
          data-field="impact"
          {...(message('impact')
            ? { validationState: 'error' as const, validationMessage: message('impact') }
            : impactDiffers && mine
              ? { hint: yours(mine.impact, () => setForm((f) => ({ ...f, impact: mine.impact }))) }
              : {})}
        >
          <RadioGroup
            value={form.impact}
            onChange={(_, data) => setForm((f) => ({ ...f, impact: data.value as 'Down' | 'None' }))}
          >
            <Radio
              value="Down"
              label={{
                children: <ImpactLabel title="Down" text="When the CI it points to is down, this CI is impacted." />,
              }}
            />
            <Radio
              value="None"
              label={{ children: <ImpactLabel title="None" text="No impact passes along this relationship." /> }}
            />
          </RadioGroup>
        </Field>

        <Field
          label={{
            children: (
              <span className={styles.label}>
                Description
                {descriptionDiffers ? <ChangedBadge /> : null}
              </span>
            ),
          }}
          data-field="description"
          {...(message('description')
            ? { validationState: 'error' as const, validationMessage: message('description') }
            : descriptionDiffers && mine
              ? {
                  hint: yours(mine.description || 'None', () =>
                    setForm((f) => ({ ...f, description: mine.description })),
                  ),
                }
              : { hint: `${form.description.length} / ${maxDescription}` })}
        >
          <Textarea
            textarea={{ ref: firstTextarea }}
            rows={3}
            resize="vertical"
            value={form.description}
            onChange={(_, data) => setForm((f) => ({ ...f, description: data.value }))}
          />
        </Field>
      </PanelShell>
      <ConflictDialog
        what="relationship type"
        text={conflict ? conflictText(editing?.name ?? '', conflict.theirs, conflict.changed) : null}
        onDiscard={() => {
          setConflict(null);
          if (conflict) onSaved(conflict.theirs, false);
        }}
        onReview={review}
      />
    </>
  );
}

function ImpactLabel({ title, text }: { title: string; text: string }) {
  const styles = useStyles();
  return (
    <span>
      {title}
      <br />
      <span className={styles.hint}>{text}</span>
    </span>
  );
}

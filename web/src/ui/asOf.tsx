import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  DialogTrigger,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Text,
  ToggleButton,
  Tooltip,
  makeStyles,
  tokens,
  useId,
} from '@fluentui/react-components';
import { ClockRegular, ErrorCircleRegular, HistoryRegular } from '@fluentui/react-icons';
import { useRef, useState, type ReactNode } from 'react';

import { formatMoment, hasPast, localHint, parseAsOf, setAsOf, shortMoment, useAsOf } from '../asOf';
import { useLocation } from '../router';

const useStyles = makeStyles({
  control: { whiteSpace: 'nowrap', fontVariantNumeric: 'tabular-nums' },
  // The banner stays in view while the page scrolls (the boards: sticky under the top bar).
  banner: { position: 'sticky', top: 0, zIndex: 2 },
  bannerText: { display: 'flex', flexWrap: 'wrap', columnGap: tokens.spacingHorizontalS },
  local: { color: tokens.colorNeutralForeground2, whiteSpace: 'nowrap' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  fieldset: {
    margin: 0,
    padding: 0,
    border: 'none',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
  },
  legend: {
    position: 'absolute',
    width: '1px',
    height: '1px',
    overflow: 'hidden',
    clip: 'rect(0 0 0 0)',
    whiteSpace: 'nowrap',
  },
  row: { display: 'flex', alignItems: 'flex-end', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  date: { width: '170px' },
  time: { width: '110px', fontVariantNumeric: 'tabular-nums' },
  utc: {
    height: '32px',
    display: 'inline-flex',
    alignItems: 'center',
    fontWeight: tokens.fontWeightSemibold,
    color: tokens.colorNeutralForeground2,
  },
  hint: { color: tokens.colorNeutralForeground3 },
  error: {
    display: 'inline-flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorPaletteRedForeground1,
  },
  quick: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalXS },
  quickLabel: { fontWeight: tokens.fontWeightSemibold, color: tokens.colorNeutralForeground2 },
  quickButtons: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  actions: { width: '100%', display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  spacer: { flexGrow: 1 },
  empty: {
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalXXXL} ${tokens.spacingHorizontalXXL}`,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalM,
    textAlign: 'center',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
  },
  emptyIcon: { fontSize: '40px', color: tokens.colorNeutralForeground3 },
  emptyTitle: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
    overflowWrap: 'anywhere',
  },
  emptyActions: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap', justifyContent: 'center' },
});

const pad = (n: number) => String(n).padStart(2, '0');

/** A moment's UTC date and time as the dialog's fields hold them ("2026-10-06", "14:00"). */
function fields(date: Date): { date: string; time: string } {
  return { date: date.toISOString().slice(0, 10), time: `${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())}` };
}

/** The fields as a moment the API takes ("2026-10-06T14:00Z"), or which field is wrong. */
function moment(date: string, time: string): { value: string } | { error: string; bad: 'date' | 'time' | 'both' } {
  const day = date.trim();
  const clock = time.trim().match(/^(\d{1,2}):(\d{2})$/);
  if (!day && !time.trim()) return { error: 'Enter a date and a time.', bad: 'both' };
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return { error: 'Enter a date and a time.', bad: 'date' };
  if (!clock || Number(clock[1]) > 23 || Number(clock[2]) > 59)
    return { error: 'Enter a date and a time.', bad: 'time' };
  const year = Number(day.slice(0, 4));
  if (year < 2000 || year > 9998) return { error: 'Use a year from 2000 to 9998.', bad: 'date' };
  const value = parseAsOf(`${day}T${pad(Number(clock[1]))}:${clock[2]}Z`);
  if (!value) return { error: 'Enter a date and a time.', bad: 'date' };
  if (new Date(value).getTime() > Date.now()) return { error: 'That time is in the future.', bad: 'both' };
  return { value };
}

/**
 * The top bar's As-of control (DEC-047 item 10; the TopBarStates board): on Search, a CI's page and the Graph
 * explorer a ToggleButton, checked while a moment is shown, that opens the dialog; elsewhere a disabled button that
 * stays focusable, with the Tooltip "This page always shows the present." Its accessible name always carries the
 * whole moment, also when its label is shortened on the rail and on a phone.
 */
export function AsOfControl({ wide }: { wide: boolean }) {
  const styles = useStyles();
  const { pathname } = useLocation();
  const asOf = useAsOf();
  const [open, setOpen] = useState(false);
  // A new dialog each time it opens, so its fields start from the moment shown then.
  const [opened, setOpened] = useState(0);
  const value = asOf ? formatMoment(asOf) : 'Now';
  const label = wide ? `As of: ${value}` : asOf ? shortMoment(asOf) : 'Now';
  const name = `As of: ${value}. Choose a point in time`;
  if (!hasPast(pathname)) {
    return (
      <Tooltip content="This page always shows the present." relationship="description">
        <Button
          appearance="outline"
          icon={<HistoryRegular />}
          disabledFocusable
          aria-label={name}
          className={styles.control}
        >
          {label}
        </Button>
      </Tooltip>
    );
  }
  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        setOpen(data.open);
        if (data.open) setOpened((n) => n + 1);
      }}
    >
      <DialogTrigger disableButtonEnhancement>
        <ToggleButton
          appearance="outline"
          checked={asOf !== null}
          icon={<HistoryRegular />}
          aria-haspopup="dialog"
          aria-label={name}
          className={styles.control}
        >
          {label}
        </ToggleButton>
      </DialogTrigger>
      <AsOfDialog key={opened} asOf={asOf} onClose={() => setOpen(false)} />
    </Dialog>
  );
}

/**
 * "Show Goldmark as of" (the DialogStates board). The Date is the browser's date field and the Time is typed,
 * 24-hour (owner, DEC-047 item 10: no date-picker package). Both are UTC; the hint gives the moment in the person's
 * own time. A quick choice fills the fields and leaves Apply to the person (Notes default 6). Errors show on Apply,
 * under the fields, and focus goes to the first field in error.
 */
export function AsOfDialog({ asOf, onClose }: { asOf: string | null; onClose: () => void }) {
  const styles = useStyles();
  const start = asOf ? fields(new Date(asOf)) : { date: '', time: '' };
  const [date, setDate] = useState(start.date);
  const [time, setTime] = useState(start.time);
  const [problem, setProblem] = useState<{ error: string; bad: 'date' | 'time' | 'both' } | null>(null);
  const dateRef = useRef<HTMLInputElement>(null);
  const timeRef = useRef<HTMLInputElement>(null);
  const hintId = useId('asof-hint');
  const errorId = useId('asof-error');
  const quickId = useId('asof-quick');
  const typed = moment(date, time);
  const hint = 'value' in typed ? localHint(typed.value) : null;

  const fill = (msAgo: number) => {
    const filled = fields(new Date(Date.now() - msAgo));
    setDate(filled.date);
    setTime(filled.time);
    setProblem(null);
  };
  const apply = () => {
    const result = moment(date, time);
    if ('error' in result) {
      setProblem(result);
      (result.bad === 'time' ? timeRef : dateRef).current?.focus();
      return;
    }
    onClose();
    setAsOf(result.value);
  };
  const described = [problem ? errorId : null, hint ? hintId : null].filter(Boolean).join(' ') || undefined;

  return (
    <DialogSurface aria-label="Choose a point in time">
      <form
        noValidate
        onSubmit={(event) => {
          event.preventDefault();
          apply();
        }}
      >
        <DialogBody>
          <DialogTitle>Show Goldmark as of</DialogTitle>
          <DialogContent className={styles.form}>
            <Text>
              Search, CI pages and the Graph explorer show what was recorded at this time. A past view is read-only.
            </Text>
            <fieldset className={styles.fieldset} aria-describedby={described}>
              <legend className={styles.legend}>Point in time, UTC</legend>
              <div className={styles.row}>
                <Field label="Date" required validationState={problem && problem.bad !== 'time' ? 'error' : 'none'}>
                  <Input
                    input={{ ref: dateRef }}
                    className={styles.date}
                    type="date"
                    min="2000-01-01"
                    max="9998-12-31"
                    value={date}
                    onChange={(_, data) => setDate(data.value)}
                  />
                </Field>
                <Field label="Time" required validationState={problem && problem.bad !== 'date' ? 'error' : 'none'}>
                  <Input
                    input={{ ref: timeRef }}
                    className={styles.time}
                    inputMode="numeric"
                    autoComplete="off"
                    placeholder="HH:MM"
                    maxLength={5}
                    contentAfter={<ClockRegular aria-hidden />}
                    value={time}
                    onChange={(_, data) => setTime(data.value)}
                  />
                </Field>
                <span className={styles.utc}>UTC</span>
              </div>
              <Text id={hintId} size={200} className={styles.hint} aria-live="polite">
                {hint ?? ''}
              </Text>
              {problem ? (
                <Text id={errorId} size={200} role="alert" className={styles.error}>
                  <ErrorCircleRegular aria-hidden />
                  {problem.error}
                </Text>
              ) : null}
            </fieldset>
            <div role="group" aria-labelledby={quickId} className={styles.quick}>
              <Text id={quickId} size={200} className={styles.quickLabel}>
                Quick choices
              </Text>
              <div className={styles.quickButtons}>
                <Button size="small" onClick={() => fill(60 * 60 * 1000)}>
                  1 hour ago
                </Button>
                <Button size="small" onClick={() => fill(24 * 60 * 60 * 1000)}>
                  Yesterday
                </Button>
                <Button size="small" onClick={() => fill(7 * 24 * 60 * 60 * 1000)}>
                  7 days ago
                </Button>
              </div>
            </div>
          </DialogContent>
          <DialogActions fluid className={styles.actions}>
            {asOf ? (
              <Button
                onClick={() => {
                  onClose();
                  setAsOf(null);
                }}
              >
                Return to now
              </Button>
            ) : null}
            <span className={styles.spacer} />
            <Button onClick={onClose}>Cancel</Button>
            <Button appearance="primary" type="submit">
              Apply
            </Button>
          </DialogActions>
        </DialogBody>
      </form>
    </DialogSurface>
  );
}

/** "Choose another time": the same dialog as the top bar's control, from a page's own empty state. */
export function ChooseAsOfButton({ asOf }: { asOf: string | null }) {
  const [open, setOpen] = useState(false);
  const [opened, setOpened] = useState(0);
  return (
    <Dialog
      open={open}
      onOpenChange={(_, data) => {
        setOpen(data.open);
        if (data.open) setOpened((n) => n + 1);
      }}
    >
      <DialogTrigger disableButtonEnhancement>
        <Button aria-haspopup="dialog">Choose another time</Button>
      </DialogTrigger>
      <AsOfDialog key={opened} asOf={asOf} onClose={() => setOpen(false)} />
    </Dialog>
  );
}

/**
 * A past view with nothing to show (the SearchNothing, CiNotYet and GraphNotYet boards): what, why, and the two ways
 * on: back to the present, or another moment.
 */
export function PastEmpty({
  asOf,
  title,
  headingLevel = 2,
  children,
}: {
  asOf: string;
  title: string;
  headingLevel?: 1 | 2;
  children?: ReactNode;
}) {
  const styles = useStyles();
  const Heading = headingLevel === 1 ? 'h1' : 'h2';
  const headingId = useId('past-empty');
  return (
    <section aria-labelledby={headingId} className={styles.empty}>
      <HistoryRegular aria-hidden className={styles.emptyIcon} />
      <Heading id={headingId} className={styles.emptyTitle}>
        {title}
      </Heading>
      {children}
      <div className={styles.emptyActions}>
        <Button appearance="primary" onClick={() => setAsOf(null)}>
          Return to now
        </Button>
        <ChooseAsOfButton asOf={asOf} />
      </div>
    </section>
  );
}

/**
 * The banner of a past view (the boards; DEC-047 item 10: amber, Fluent's warning intent, with the clock-arrow
 * icon): what is shown, in UTC and in the person's own time, and the way back to the present.
 */
export function AsOfBanner({ asOf, subject = 'Viewing' }: { asOf: string; subject?: 'Viewing' | 'Graph' }) {
  const styles = useStyles();
  return (
    <div className={styles.banner}>
      <MessageBar intent="warning" icon={<HistoryRegular />} politeness="polite">
        <MessageBarBody className={styles.bannerText}>
          <strong>{`${subject} as of ${formatMoment(asOf)} (read-only)`}</strong>
          <span className={styles.local}>{localHint(asOf)}</span>
        </MessageBarBody>
        <MessageBarActions>
          <Button size="small" onClick={() => setAsOf(null)}>
            Return to now
          </Button>
        </MessageBarActions>
      </MessageBar>
    </div>
  );
}

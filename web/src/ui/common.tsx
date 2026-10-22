import {
  Button,
  Checkbox,
  Field,
  Input,
  MessageBar,
  MessageBarActions,
  MessageBarBody,
  Text,
  makeStyles,
  mergeClasses,
  tokens,
  type InputProps,
} from '@fluentui/react-components';
import {
  ArrowDownloadRegular,
  CheckmarkCircleRegular,
  CircleRegular,
  CopyRegular,
  DismissCircleRegular,
  EyeOffRegular,
  EyeRegular,
} from '@fluentui/react-icons';
import { useEffect, useRef, useState, type ReactNode, type Ref } from 'react';

import type { ApiError } from '../api';

const useStyles = makeStyles({
  page: {
    minHeight: '100vh',
    boxSizing: 'border-box',
    padding: `${tokens.spacingVerticalXXXL} ${tokens.spacingHorizontalL} ${tokens.spacingVerticalL}`,
    backgroundColor: tokens.colorNeutralBackground3,
    color: tokens.colorNeutralForeground1,
    display: 'flex',
    flexDirection: 'column',
    alignItems: 'center',
    gap: tokens.spacingVerticalXXL,
    '@media (max-width: 479px)': { padding: `${tokens.spacingVerticalXL} ${tokens.spacingHorizontalL}` },
  },
  brand: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalS },
  mark: {
    width: '32px',
    height: '32px',
    borderRadius: tokens.borderRadiusLarge,
    backgroundColor: tokens.colorBrandBackground,
    color: tokens.colorNeutralForegroundOnBrand,
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'center',
    fontSize: tokens.fontSizeBase500,
    fontWeight: tokens.fontWeightBold,
  },
  card: {
    width: '100%',
    boxSizing: 'border-box',
    backgroundColor: tokens.colorNeutralBackground1,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusLarge,
    padding: tokens.spacingHorizontalXXL,
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalM,
  },
  narrow: { maxWidth: '400px' },
  wide: { maxWidth: '560px' },
  heading: {
    margin: 0,
    fontSize: tokens.fontSizeBase600,
    lineHeight: tokens.lineHeightBase600,
    fontWeight: tokens.fontWeightSemibold,
    ':focus-visible': { outlineOffset: '2px' },
  },
  footer: { marginTop: 'auto', fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  mono: { fontFamily: tokens.fontFamilyMonospace },
  codeInput: { fontFamily: tokens.fontFamilyMonospace, letterSpacing: '0.1em', maxWidth: '180px' },
  rules: {
    margin: 0,
    padding: 0,
    listStyleType: 'none',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXXS,
    fontSize: tokens.fontSizeBase200,
  },
  rule: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalXS,
    color: tokens.colorNeutralForeground3,
  },
  rulePass: { color: tokens.colorPaletteGreenForeground1 },
  ruleFail: { color: tokens.colorPaletteRedForeground1 },
  progress: {
    margin: 0,
    padding: 0,
    listStyleType: 'none',
    display: 'grid',
    gridTemplateColumns: 'repeat(4, minmax(0, 1fr))',
    gap: tokens.spacingHorizontalXS,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
  },
  step: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    color: tokens.colorNeutralForeground3,
  },
  stepDone: { color: tokens.colorNeutralForeground2 },
  stepCurrent: { color: tokens.colorNeutralForeground1, fontWeight: tokens.fontWeightSemibold },
  bar: { height: '4px', borderRadius: tokens.borderRadiusSmall, backgroundColor: tokens.colorNeutralStroke1 },
  barOn: { backgroundColor: tokens.colorBrandForeground1 },
  qrRow: { display: 'flex', gap: tokens.spacingHorizontalL, alignItems: 'flex-start', flexWrap: 'wrap' },
  qr: {
    width: '184px',
    height: '184px',
    flexShrink: 0,
    backgroundColor: '#ffffff',
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  keyColumn: {
    flexGrow: 1,
    minWidth: '180px',
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalS,
    alignItems: 'flex-start',
  },
  keyText: {
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase400,
    letterSpacing: '0.06em',
    wordBreak: 'break-all',
  },
  hint: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  codes: {
    margin: 0,
    padding: `${tokens.spacingVerticalM} ${tokens.spacingHorizontalL}`,
    listStyleType: 'none',
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXXL}`,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase400,
    fontWeight: tokens.fontWeightSemibold,
    letterSpacing: '0.04em',
  },
  row: { display: 'flex', gap: tokens.spacingHorizontalS, flexWrap: 'wrap' },
  reference: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground2 },
});

/** Screens 1, 1b and 2: no app shell, the brand above one card (400 px, or 560 px for wizards). */
export function UnauthenticatedLayout({
  wide = false,
  title = 'Goldmark',
  children,
}: {
  wide?: boolean;
  title?: string;
  children: ReactNode;
}) {
  const styles = useStyles();
  return (
    <div className={styles.page}>
      <div className={styles.brand}>
        <span aria-hidden="true" className={styles.mark}>
          G
        </span>
        <Text weight="semibold" size={400}>
          {title}
        </Text>
      </div>
      <main className={mergeClasses(styles.card, wide ? styles.wide : styles.narrow)} aria-labelledby="page-heading">
        {children}
      </main>
      <footer className={styles.footer}>Goldmark</footer>
    </div>
  );
}

/** The page's h1, focused whenever `focusKey` changes so a step change is announced (screens 1b, 2). */
export function PageHeading({ children, focusKey }: { children: ReactNode; focusKey?: string | number }) {
  const styles = useStyles();
  const ref = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    if (focusKey !== undefined) {
      ref.current?.focus();
    }
  }, [focusKey]);
  return (
    <h1 id="page-heading" ref={ref} tabIndex={-1} className={styles.heading}>
      {children}
    </h1>
  );
}

/** A server or network error: what happened, Retry, and a Reference ID matching the server log. */
export function ErrorBar({
  error,
  onRetry,
  message,
}: {
  error: ApiError;
  onRetry?: () => void;
  message?: string | undefined;
}) {
  const styles = useStyles();
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => ref.current?.focus(), [error]);
  return (
    <div ref={ref} tabIndex={-1}>
      <MessageBar intent="error">
        <MessageBarBody>
          {message ??
            (error.status === 0
              ? 'Couldn’t reach Goldmark. Check your connection and try again.'
              : 'Something went wrong on the server.')}
          {error.reference ? (
            <div className={styles.reference}>
              Reference ID <span className={styles.mono}>{error.reference}</span>{' '}
              <CopyButton value={error.reference} label="Copy" size="small" appearance="transparent" />
            </div>
          ) : null}
        </MessageBarBody>
        {onRetry ? (
          <MessageBarActions>
            <Button size="small" onClick={onRetry}>
              Retry
            </Button>
          </MessageBarActions>
        ) : null}
      </MessageBar>
    </div>
  );
}

/** A MessageBar that takes focus when it appears, as the boards require for errors and warnings. */
export function Notice({
  intent,
  children,
}: {
  intent: 'error' | 'warning' | 'info' | 'success';
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (intent === 'error' || intent === 'warning') {
      ref.current?.focus();
    }
  }, [intent, children]);
  return (
    <div ref={ref} tabIndex={-1}>
      <MessageBar intent={intent} politeness={intent === 'error' || intent === 'warning' ? 'assertive' : 'polite'}>
        <MessageBarBody>{children}</MessageBarBody>
      </MessageBar>
    </div>
  );
}

export function CopyButton({
  value,
  label,
  size = 'small',
  appearance = 'secondary',
}: {
  value: string;
  label: string;
  size?: 'small' | 'medium';
  appearance?: 'secondary' | 'transparent';
}) {
  const [copied, setCopied] = useState(false);
  useEffect(() => {
    if (!copied) return undefined;
    const timer = window.setTimeout(() => setCopied(false), 2000);
    return () => window.clearTimeout(timer);
  }, [copied]);
  return (
    <Button
      size={size}
      appearance={appearance}
      icon={<CopyRegular />}
      onClick={() => {
        void navigator.clipboard.writeText(value).then(() => setCopied(true));
      }}
    >
      <span aria-live="polite">{copied ? 'Copied' : label}</span>
    </Button>
  );
}

/** A password field with a show/hide toggle that works with password managers. */
export function PasswordInput(
  props: Omit<InputProps, 'type' | 'contentAfter'> & { autoComplete: 'current-password' | 'new-password' },
) {
  const [visible, setVisible] = useState(false);
  return (
    <Input
      {...props}
      type={visible ? 'text' : 'password'}
      contentAfter={
        <Button
          appearance="transparent"
          size="small"
          aria-label={visible ? 'Hide password' : 'Show password'}
          aria-pressed={visible}
          icon={visible ? <EyeOffRegular /> : <EyeRegular />}
          onClick={() => setVisible((v) => !v)}
        />
      }
    />
  );
}

export type RuleStatus = 'pass' | 'fail' | 'pending';

/** The password rules as a live checklist, icon and text (never colour alone). */
export function PasswordChecklist({
  password,
  confirmation,
  common,
  checkedWhen = 'continue',
}: {
  password: string;
  confirmation: string;
  common: boolean | null;
  /** When the server checks the common-password rule: on the step's Continue, or a dialog's save. */
  checkedWhen?: 'continue' | 'save';
}) {
  const styles = useStyles();
  const length: RuleStatus =
    password.length === 0 ? 'pending' : password.length >= 12 && password.length <= 128 ? 'pass' : 'fail';
  const matches: RuleStatus = confirmation.length === 0 ? 'pending' : password === confirmation ? 'pass' : 'fail';
  const rules: [string, RuleStatus][] = [
    ['12 to 128 characters', length],
    [
      common === null
        ? `Not a commonly used password (checked when you ${checkedWhen})`
        : 'Not a commonly used password',
      common === null ? 'pending' : common ? 'fail' : 'pass',
    ],
    ['Matches the confirmation', matches],
  ];
  return (
    <ul className={styles.rules} aria-label="Password rules">
      {rules.map(([text, status]) => (
        <li
          key={text}
          className={mergeClasses(
            styles.rule,
            status === 'pass' && styles.rulePass,
            status === 'fail' && styles.ruleFail,
          )}
        >
          {status === 'pass' ? (
            <CheckmarkCircleRegular aria-hidden />
          ) : status === 'fail' ? (
            <DismissCircleRegular aria-hidden />
          ) : (
            <CircleRegular aria-hidden />
          )}
          {text}
          {status === 'pass' ? ' · done' : status === 'fail' ? ' · not met' : ''}
        </li>
      ))}
    </ul>
  );
}

/** One numeric field for a 6-digit code (never six boxes). */
export function CodeInput({
  label,
  value,
  onChange,
  error,
  disabled = false,
  inputRef,
}: {
  label: string;
  value: string;
  onChange: (value: string) => void;
  error?: string | null;
  disabled?: boolean;
  inputRef?: Ref<HTMLInputElement>;
}) {
  const styles = useStyles();
  return (
    <Field label={label} validationState={error ? 'error' : 'none'} {...(error ? { validationMessage: error } : {})}>
      <Input
        {...(inputRef ? { input: { ref: inputRef } } : {})}
        className={styles.codeInput}
        inputMode="numeric"
        autoComplete="one-time-code"
        maxLength={6}
        value={value}
        disabled={disabled}
        onChange={(_, data) => onChange(data.value.replace(/\D/g, ''))}
      />
    </Field>
  );
}

/** Step 3 of screens 1b and 2: the QR code Goldmark serves, the same key as text, and the code. */
export function AuthenticatorSetup({
  keyText,
  qrSource,
  code,
  onCodeChange,
  error,
  codeLabel = '6-digit code from the app',
  intro = 'Scan this code with your authenticator app, or enter the key by hand.',
}: {
  keyText: string;
  qrSource: string;
  code: string;
  onCodeChange: (code: string) => void;
  error: string | null;
  codeLabel?: string;
  intro?: string;
}) {
  const styles = useStyles();
  return (
    <>
      <Text as="p">{intro}</Text>
      <div className={styles.qrRow}>
        <img
          className={styles.qr}
          src={qrSource}
          alt={`QR code for your authenticator app. The same key as text: ${keyText}`}
        />
        <div className={styles.keyColumn}>
          <Text weight="semibold">Key</Text>
          <span className={styles.keyText}>{keyText}</span>
          <CopyButton value={keyText.replace(/\s/g, '')} label="Copy key" />
          <span className={styles.hint}>
            Works with Microsoft Authenticator, Google Authenticator, 1Password, Bitwarden or any TOTP app.
          </span>
        </div>
      </div>
      <CodeInput label={codeLabel} value={code} onChange={onCodeChange} error={error} />
    </>
  );
}

/** Saves a file the page made or received (recovery codes, the audit export) through a temporary link. */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.append(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}

/** The shown-once recovery codes (step 4, and new codes on the two-factor page). */
export function RecoveryCodes({
  codes,
  stored,
  onStoredChange,
  intro = 'Each one signs you in once if you lose your authenticator.',
}: {
  codes: string[];
  stored: boolean;
  onStoredChange: (stored: boolean) => void;
  intro?: string;
}) {
  const styles = useStyles();
  const text = `Goldmark recovery codes\nEach code works once.\n\n${codes.join('\n')}\n`;
  return (
    <>
      <MessageBar intent="warning">
        <MessageBarBody>
          <b>These codes are shown only once.</b> {intro}
        </MessageBarBody>
      </MessageBar>
      <ul className={styles.codes} aria-label="Recovery codes">
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ul>
      <div className={styles.row}>
        <CopyButton value={codes.join('\n')} label="Copy all" size="medium" />
        <Button
          icon={<ArrowDownloadRegular />}
          onClick={() => downloadBlob(new Blob([text], { type: 'text/plain' }), 'goldmark-recovery-codes.txt')}
        >
          Download .txt
        </Button>
      </div>
      <Checkbox
        label="I have stored these codes safely"
        checked={stored}
        onChange={(_, data) => onStoredChange(data.checked === true)}
      />
    </>
  );
}

/** The wizard's progress as an ordered list; the current step is marked, not only coloured. */
export function WizardProgress({ steps, current }: { steps: string[]; current: number }) {
  const styles = useStyles();
  return (
    <ol className={styles.progress} aria-label="Progress">
      {steps.map((label, index) => {
        const n = index + 1;
        return (
          <li
            key={label}
            aria-current={n === current ? 'step' : undefined}
            className={mergeClasses(styles.step, n < current && styles.stepDone, n === current && styles.stepCurrent)}
          >
            <span className={mergeClasses(styles.bar, n <= current && styles.barOn)} />
            {n < current ? `✓ ${label}` : label}
          </li>
        );
      })}
    </ol>
  );
}

export function useCommonStyles() {
  return useStyles();
}

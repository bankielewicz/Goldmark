import { Badge, Button, Field, Input, Link, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { useCallback, useEffect, useState, type FormEvent } from 'react';

import { api, ApiError, type SessionUser } from '../api';
import { followLink, navigate, useLocation } from '../router';
import { useSession } from '../session';
import {
  AuthenticatorSetup,
  CodeInput,
  CopyButton,
  ErrorBar,
  Notice,
  PageHeading,
  PasswordChecklist,
  PasswordInput,
  RecoveryCodes,
  UnauthenticatedLayout,
  WizardProgress,
} from '../ui/common';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  welcome: {
    display: 'flex',
    flexDirection: 'column',
    gap: tokens.spacingVerticalXS,
    paddingBottom: tokens.spacingVerticalM,
    borderBottom: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
  },
  roles: {
    display: 'flex',
    gap: tokens.spacingHorizontalXS,
    alignItems: 'center',
    flexWrap: 'wrap',
    color: tokens.colorNeutralForeground2,
  },
  command: {
    display: 'flex',
    alignItems: 'center',
    gap: tokens.spacingHorizontalS,
    padding: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXS} ${tokens.spacingVerticalXS} ${tokens.spacingHorizontalM}`,
    backgroundColor: tokens.colorNeutralBackground2,
    border: `${tokens.strokeWidthThin} solid ${tokens.colorNeutralStroke2}`,
    borderRadius: tokens.borderRadiusMedium,
  },
  code: {
    flexGrow: 1,
    fontFamily: tokens.fontFamilyMonospace,
    fontSize: tokens.fontSizeBase300,
    overflowWrap: 'anywhere',
  },
  hint: { fontSize: tokens.fontSizeBase200, color: tokens.colorNeutralForeground3 },
  mono: { fontFamily: tokens.fontFamilyMonospace, letterSpacing: '0.1em' },
  shortCode: { fontFamily: tokens.fontFamilyMonospace, letterSpacing: '0.1em', maxWidth: '180px' },
  recoveryCode: { fontFamily: tokens.fontFamilyMonospace, letterSpacing: '0.1em', maxWidth: '240px' },
});

/**
 * `reset` is the two-factor reset; `password` the password reset (confirmed with the person's own
 * authenticator); `both` the password-and-two-factor reset, which runs the invitation's steps (DEC-042).
 */
export type EnrolmentFlow = 'setup' | 'invite' | 'reset' | 'password' | 'both';
type Step = 1 | 2 | 3 | 4 | 'done';
type Terminal = 'expired' | 'invalid' | 'dead' | 'pending' | 'complete' | 'completed-elsewhere';

const labels: Record<EnrolmentFlow, string[]> = {
  setup: ['Setup token', 'Admin account', 'Two-factor', 'Recovery codes'],
  invite: ['Confirm code', 'Password', 'Two-factor', 'Recovery codes'],
  reset: ['Confirm code', 'Your password', 'Two-factor', 'Recovery codes'],
  password: ['Confirm code', 'New password', 'Authenticator'],
  both: ['Confirm code', 'New password', 'Two-factor', 'Recovery codes'],
};

const headings: Record<EnrolmentFlow, string[]> = {
  setup: [
    'Enter the setup token',
    'Create the admin account',
    'Set up two-factor authentication',
    'Save your recovery codes',
  ],
  invite: ['Confirm your code', 'Choose a password', 'Set up two-factor authentication', 'Save your recovery codes'],
  reset: [
    'Confirm your code',
    'Sign in with your password',
    'Set up two-factor authentication',
    'Save your recovery codes',
  ],
  password: ['Confirm your code', 'Choose a new password', 'Confirm with your authenticator'],
  both: ['Confirm your code', 'Choose a new password', 'Set up two-factor authentication', 'Save your recovery codes'],
};

const welcomes: Record<EnrolmentFlow, { title: string; text: string }> = {
  setup: { title: '', text: '' },
  invite: { title: 'Welcome, ', text: 'You’re invited to Goldmark as' },
  reset: { title: 'Set up two-factor again, ', text: 'An administrator reset your two-factor. Your roles:' },
  password: { title: 'Choose a new password, ', text: 'An administrator reset your password. Your roles:' },
  both: {
    title: 'Set up your account again, ',
    text: 'An administrator reset your password and two-factor. Your roles:',
  },
};

/** The link's purpose, as `/links/check` and `/links/redeem` name it, to its flow. */
function flowOf(purpose: string | undefined): EnrolmentFlow {
  switch (purpose) {
    case 'two-factor-reset':
      return 'reset';
    case 'password-reset':
      return 'password';
    case 'combined-reset':
      return 'both';
    default:
      return 'invite';
  }
}

interface Person {
  userName: string;
  displayName: string;
  roles: string[];
}

/**
 * Screen 2 (first-run setup, US-02) and screen 1b (accept an invitation, set up two-factor again after
 * a reset, or choose a new password after a password reset; DEC-038, DEC-042): one wizard. Nothing is
 * stored until the last step checks out (a code from the new authenticator, or for a password reset
 * a code from the person's own authenticator or a recovery code); then the person is signed in.
 */
export function EnrolmentPage({ route }: { route: 'setup' | 'link' }) {
  const styles = useStyles();
  const session = useSession();
  const { hash } = useLocation();
  const token = route === 'link' ? new URLSearchParams(hash.slice(1)).get('t') : null;

  const [flow, setFlow] = useState<EnrolmentFlow>(route === 'setup' ? 'setup' : 'invite');
  const [step, setStep] = useState<Step | null>(null);
  const [terminal, setTerminal] = useState<Terminal | null>(null);
  const [person, setPerson] = useState<Person | null>(null);
  const [busy, setBusy] = useState(false);
  const [failure, setFailure] = useState<{ error: ApiError; retry: () => void } | null>(null);

  const [setupToken, setSetupToken] = useState('');
  const [tokenError, setTokenError] = useState<string | null>(null);
  const [shortCode, setShortCode] = useState('');
  const [codeError, setCodeError] = useState<string | null>(null);
  const [userName, setUserName] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [commonPassword, setCommonPassword] = useState<boolean | null>(null);
  const [authenticatorKey, setAuthenticatorKey] = useState<string | null>(null);
  const [qrVersion, setQrVersion] = useState(0);
  const [totp, setTotp] = useState('');
  const [totpError, setTotpError] = useState<string | null>(null);
  const [useRecoveryCode, setUseRecoveryCode] = useState(false);
  const [recoveryCode, setRecoveryCode] = useState('');
  const [recoveryCodeError, setRecoveryCodeError] = useState<string | null>(null);
  const [recoveryCodesLeft, setRecoveryCodesLeft] = useState<number | null>(null);
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>([]);
  const [stored, setStored] = useState(false);
  const [signedInUser, setSignedInUser] = useState<SessionUser | null>(null);

  const newAuthenticator = useCallback(async () => {
    const { data } = await api<{ key: string }>('POST', '/api/v1/enrolment/authenticator', {}, { signedIn: false });
    setAuthenticatorKey(data.key);
    setQrVersion((v) => v + 1);
    setTotp('');
    setTotpError(null);
    setStep(3);
  }, []);

  // Where to start: the setup status or the link's state, then any enrolment already in progress.
  useEffect(() => {
    let cancelled = false;
    async function start() {
      try {
        if (route === 'setup') {
          const { data } = await api<{ status: string }>('GET', '/api/v1/setup', undefined, { signedIn: false });
          if (cancelled) return;
          if (data.status === 'complete') {
            setTerminal('complete');
            return;
          }
        } else {
          if (!token) {
            setTerminal('invalid');
            return;
          }
          const { data } = await api<{ status: string; purpose?: string }>(
            'POST',
            '/api/v1/links/check',
            { token },
            { signedIn: false },
          );
          if (cancelled) return;
          if (data.status !== 'valid') {
            // Opened before another Admin approved it (DEC-042): no code exists yet, so no code field.
            setTerminal(data.status === 'expired' ? 'expired' : data.status === 'pending' ? 'pending' : 'invalid');
            return;
          }
          setFlow(flowOf(data.purpose));
        }
        setStep(1);
      } catch (error) {
        if (!cancelled && error instanceof ApiError) {
          setFailure({ error, retry: () => window.location.reload() });
        }
      }
    }
    void start();
    return () => {
      cancelled = true;
    };
  }, [route, token]);

  /** Runs one step against the API; network and server errors show the error bar with Retry. */
  async function run(action: () => Promise<void>) {
    setBusy(true);
    setFailure(null);
    try {
      await action();
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      if (error.code === 'enrolment-expired') {
        // 30 minutes passed, or Goldmark restarted (setup) or the link changed: start again.
        setStep(1);
        setPerson(null);
        if (flow === 'setup') {
          setTokenError('This step has expired. Enter the newest setup token from the log.');
        } else {
          setCodeError('This step has expired. Enter the code again.');
        }
      } else if (error.code === 'setup-complete') {
        setTerminal('completed-elsewhere');
      } else if (error.code === 'link-expired') {
        setTerminal('expired');
      } else if (error.code === 'link-invalid') {
        setTerminal('invalid');
      } else if (error.code === 'link-invalidated') {
        setTerminal('dead');
      } else if (error.code === 'link-pending') {
        setTerminal('pending');
      } else if (
        error.isUnavailable ||
        error.status === 429 ||
        error.code === 'locked' ||
        error.code.startsWith('http-')
      ) {
        setFailure({ error, retry: () => void run(action) });
      } else {
        handleProblem(error);
      }
    } finally {
      setBusy(false);
    }
  }

  function handleProblem(error: ApiError) {
    switch (error.code) {
      case 'invalid-setup-token':
        setTokenError(
          'That token isn’t valid. Use the newest one in the log; a new token is printed whenever Goldmark restarts.',
        );
        return;
      case 'wrong-code': {
        const left = typeof error.body.triesLeft === 'number' ? error.body.triesLeft : null;
        setCodeError(
          left === null
            ? 'That code doesn’t match.'
            : `That code doesn’t match. ${left} ${left === 1 ? 'try' : 'tries'} left.`,
        );
        return;
      }
      case 'invalid-code':
        setTotpError('That code didn’t work. Codes change every 30 seconds.');
        return;
      case 'invalid-recovery-code':
        setRecoveryCodeError('That recovery code didn’t work, or it was already used.');
        return;
      case 'validation': {
        const errors: Record<string, string> = {};
        for (const [field, codes] of Object.entries(error.fieldErrors)) {
          errors[field] = describeFieldError(codes[0] ?? '');
        }
        setCommonPassword(error.hasField('password', 'PasswordCommon') ? true : commonPassword);
        setFieldErrors(errors);
        return;
      }
      default:
        setFailure({ error, retry: () => setFailure(null) });
    }
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (step === 1) {
      await run(async () => {
        if (flow === 'setup') {
          setTokenError(null);
          await api('POST', '/api/v1/setup/token', { token: setupToken.trim() }, { signedIn: false });
        } else {
          setCodeError(null);
          const { data } = await api<Person & { purpose: string }>(
            'POST',
            '/api/v1/links/redeem',
            { token, code: shortCode },
            { signedIn: false },
          );
          setPerson({ userName: data.userName, displayName: data.displayName, roles: data.roles });
          setFlow(flowOf(data.purpose));
        }
        setStep(2);
      });
    } else if (step === 2) {
      const clientErrors = checkPassword();
      setFieldErrors(clientErrors);
      if (Object.keys(clientErrors).length > 0) return;
      await run(async () => {
        if (flow === 'setup') {
          await api(
            'POST',
            '/api/v1/enrolment/account',
            { userName: userName.trim(), displayName: displayName.trim(), password },
            { signedIn: false },
          );
        } else {
          await api('POST', '/api/v1/enrolment/password', { password }, { signedIn: false });
        }
        setCommonPassword(false);
        setPassword('');
        setConfirmation('');
        if (flow === 'password') {
          // The person's own authenticator confirms the new password (DEC-042 item 3).
          setTotp('');
          setTotpError(null);
          setRecoveryCodeError(null);
          setStep(3);
        } else {
          await newAuthenticator();
        }
      });
    } else if (step === 3 && flow === 'password') {
      await confirmPasswordReset();
    } else if (step === 3) {
      if (totp.length !== 6) {
        setTotpError('Enter the 6-digit code from the app.');
        return;
      }
      await run(async () => {
        setTotpError(null);
        const { data } = await api<{ user: SessionUser; recoveryCodes: string[] }>(
          'POST',
          '/api/v1/enrolment/complete',
          { code: totp },
          { signedIn: false },
        );
        setSignedInUser(data.user);
        setRecoveryCodes(data.recoveryCodes);
        setStep(4);
      });
    } else if (step === 4) {
      setStep('done');
    } else if (step === 'done' && signedInUser) {
      session.signedIn(signedInUser);
      navigate('/', { replace: true });
    }
  }

  async function confirmPasswordReset() {
    if (!useRecoveryCode && totp.length !== 6) {
      setTotpError('Enter the 6-digit code from the app.');
      return;
    }
    if (useRecoveryCode && recoveryCode.trim().length === 0) {
      setRecoveryCodeError('Enter one of your recovery codes.');
      return;
    }
    await run(async () => {
      setTotpError(null);
      setRecoveryCodeError(null);
      const { data } = await api<{ user: SessionUser; recoveryCodesRemaining?: number | null; next?: string }>(
        'POST',
        '/api/v1/enrolment/confirm',
        useRecoveryCode ? { recoveryCode: recoveryCode.trim() } : { code: totp },
        { signedIn: false },
      );
      if (data.next === 'new-authenticator') {
        // Goldmark can no longer check the authenticator (its keys were lost, DEC-038 item 5): the
        // recovery code just used starts setting up a new one, as after such a sign-in.
        session.signedIn(data.user);
        navigate('/account/two-factor?authenticator=new', { replace: true });
        return;
      }
      setSignedInUser(data.user);
      setRecoveryCodesLeft(useRecoveryCode ? (data.recoveryCodesRemaining ?? null) : null);
      setStep('done');
    });
  }

  function checkPassword(): Record<string, string> {
    const errors: Record<string, string> = {};
    if (flow === 'setup') {
      if (userName.trim().length === 0) errors.userName = 'Enter a username.';
      if (displayName.trim().length === 0) errors.displayName = 'Enter a display name.';
    }
    if (flow !== 'reset') {
      if (password.length < 12 || password.length > 128) errors.password = 'Use 12 to 128 characters.';
      else if (password !== confirmation) errors.confirmation = 'The passwords don’t match.';
    } else if (password.length === 0) {
      errors.password = 'Enter your password.';
    }
    return errors;
  }

  const current = typeof step === 'number' ? step : 0;
  const newPassword = flow === 'password' || flow === 'both';
  const heading = terminal
    ? terminal === 'complete' || terminal === 'completed-elsewhere'
      ? 'Setup is already complete'
      : terminal === 'pending'
        ? 'This link isn’t active yet'
        : 'This link can’t be used'
    : step === 'done'
      ? flow === 'setup'
        ? 'Goldmark is ready'
        : flow === 'password'
          ? 'Your new password is set'
          : 'You’re all set'
      : current > 0
        ? headings[flow][current - 1]
        : flow === 'setup'
          ? 'First-run setup'
          : 'Your invitation';

  const buttonLabel =
    step === 'done'
      ? flow === 'setup'
        ? 'Go to the dashboard'
        : 'Go to Goldmark'
      : ((flow === 'password'
          ? ['Continue', 'Continue', 'Change password']
          : ['Continue', flow === 'setup' ? 'Create admin' : 'Continue', 'Verify', 'Finish'])[current - 1] ??
        'Continue');

  return (
    <UnauthenticatedLayout wide title={flow === 'setup' ? 'Goldmark setup' : 'Goldmark'}>
      {person && !terminal && step !== 'done' ? (
        <div className={styles.welcome}>
          <Text weight="semibold" size={400}>
            {welcomes[flow].title}
            {person.displayName}
          </Text>
          <div className={styles.roles}>
            <span>{welcomes[flow].text}</span>
            {person.roles.map((role) => (
              <Badge key={role} appearance="tint" color="subtle">
                {role}
              </Badge>
            ))}
          </div>
        </div>
      ) : null}
      {current > 0 && !terminal ? <WizardProgress steps={labels[flow]} current={current} /> : null}
      <PageHeading focusKey={`${flow}-${String(step)}-${terminal ?? ''}`}>{heading}</PageHeading>
      {failure ? (
        <ErrorBar error={failure.error} onRetry={failure.retry} message={failureMessage(failure.error, flow)} />
      ) : null}
      {terminal ? (
        <TerminalState terminal={terminal} />
      ) : step === null ? (
        failure ? null : (
          <Spinner label="Loading…" />
        )
      ) : (
        <form className={styles.form} onSubmit={(event) => void onSubmit(event)} noValidate>
          {step === 1 && flow === 'setup' ? (
            <>
              <Text as="p">Find the setup token in the Goldmark log:</Text>
              <div className={styles.command}>
                <code className={styles.code}>docker compose logs app | grep &quot;Setup token&quot;</code>
                <CopyButton value='docker compose logs app | grep "Setup token"' label="Copy" />
              </div>
              <span className={styles.hint}>
                Other installations: the Goldmark service log. A new token is printed each time Goldmark starts, until
                setup is complete.
              </span>
              <Field
                label="Setup token"
                required
                validationState={tokenError ? 'error' : 'none'}
                {...(tokenError ? { validationMessage: tokenError } : {})}
              >
                <Input
                  className={styles.mono}
                  autoComplete="off"
                  spellCheck={false}
                  value={setupToken}
                  onChange={(_, d) => setSetupToken(d.value)}
                />
              </Field>
            </>
          ) : null}
          {step === 1 && flow !== 'setup' ? (
            <>
              <Text as="p">Enter the code your administrator gave you.</Text>
              <Field
                label="Code"
                required
                hint="The code reaches you separately from this link, by phone or in person. Not case-sensitive."
                validationState={codeError ? 'error' : 'none'}
                {...(codeError ? { validationMessage: codeError } : {})}
              >
                <Input
                  className={styles.shortCode}
                  autoComplete="off"
                  spellCheck={false}
                  value={shortCode}
                  onChange={(_, d) => setShortCode(d.value)}
                />
              </Field>
            </>
          ) : null}
          {step === 2 && flow !== 'reset' ? (
            <>
              {flow === 'password' ? (
                <Text as="p">
                  Nothing changes yet: your new password takes effect when you confirm with your authenticator in the
                  next step.
                </Text>
              ) : null}
              <Field label="Username" required={flow === 'setup'} {...fieldProps(fieldErrors.userName)}>
                <Input
                  autoComplete="username"
                  value={flow === 'setup' ? userName : (person?.userName ?? '')}
                  readOnly={flow !== 'setup'}
                  onChange={(_, d) => setUserName(d.value)}
                />
              </Field>
              {flow === 'setup' ? (
                <Field label="Display name" required {...fieldProps(fieldErrors.displayName)}>
                  <Input autoComplete="name" value={displayName} onChange={(_, d) => setDisplayName(d.value)} />
                </Field>
              ) : null}
              <Field label={newPassword ? 'New password' : 'Password'} required {...fieldProps(fieldErrors.password)}>
                <PasswordInput
                  autoComplete="new-password"
                  value={password}
                  onChange={(_, d) => {
                    setPassword(d.value);
                    setCommonPassword(null);
                  }}
                />
              </Field>
              <Field
                label={newPassword ? 'Confirm new password' : 'Confirm password'}
                required
                {...fieldProps(fieldErrors.confirmation)}
              >
                <PasswordInput
                  autoComplete="new-password"
                  value={confirmation}
                  onChange={(_, d) => setConfirmation(d.value)}
                />
              </Field>
              <PasswordChecklist password={password} confirmation={confirmation} common={commonPassword} />
            </>
          ) : null}
          {step === 2 && flow === 'reset' ? (
            <>
              <Text as="p">Your password stays the same. Enter it to continue.</Text>
              <Field label="Password" required {...fieldProps(fieldErrors.password)}>
                <PasswordInput
                  autoComplete="current-password"
                  value={password}
                  onChange={(_, d) => setPassword(d.value)}
                />
              </Field>
            </>
          ) : null}
          {step === 3 && flow === 'password' ? (
            <>
              <Text as="p">
                {useRecoveryCode
                  ? 'Enter one of your recovery codes. Each code works once. Your new password takes effect when the code is checked.'
                  : 'Enter the 6-digit code from your authenticator app. Your new password takes effect when the code is checked.'}
              </Text>
              {useRecoveryCode ? (
                <Field
                  label="Recovery code"
                  validationState={recoveryCodeError ? 'error' : 'none'}
                  {...(recoveryCodeError ? { validationMessage: recoveryCodeError } : {})}
                >
                  <Input
                    className={styles.recoveryCode}
                    autoComplete="one-time-code"
                    spellCheck={false}
                    value={recoveryCode}
                    readOnly={busy}
                    onChange={(_, d) => setRecoveryCode(d.value)}
                  />
                </Field>
              ) : (
                <CodeInput label="6-digit code" value={totp} onChange={setTotp} error={totpError} />
              )}
              <div>
                <Link
                  as="button"
                  type="button"
                  onClick={() => {
                    setFailure(null);
                    setTotpError(null);
                    setRecoveryCodeError(null);
                    setUseRecoveryCode(!useRecoveryCode);
                  }}
                >
                  {useRecoveryCode ? 'Use your authenticator app instead' : 'Use a recovery code instead'}
                </Link>
              </div>
            </>
          ) : null}
          {step === 3 && flow !== 'password' && authenticatorKey ? (
            <AuthenticatorSetup
              keyText={authenticatorKey}
              qrSource={`/api/v1/enrolment/authenticator.svg?v=${qrVersion}`}
              code={totp}
              onCodeChange={setTotp}
              error={totpError}
            />
          ) : null}
          {step === 4 ? <RecoveryCodes codes={recoveryCodes} stored={stored} onStoredChange={setStored} /> : null}
          {step === 'done' ? (
            <Notice intent="success">
              {flow === 'setup'
                ? 'The admin account is ready, with two-factor authentication on. Add other people on the Users & roles page.'
                : flow === 'password'
                  ? 'You’re signed in.'
                  : flow === 'both'
                    ? 'Your new password and two-factor authentication are set up.'
                    : 'Your password and two-factor authentication are set up.'}
            </Notice>
          ) : null}
          {step === 'done' && recoveryCodesLeft !== null ? (
            <Notice intent="info">
              You used a recovery code; {recoveryCodesLeft} {recoveryCodesLeft === 1 ? 'is' : 'are'} left. You can
              generate new ones on your two-factor page.{' '}
              <Link
                href="/account/two-factor"
                onClick={(event) => {
                  event.preventDefault();
                  if (signedInUser) session.signedIn(signedInUser);
                  navigate('/account/two-factor', { replace: true });
                }}
              >
                Go to your two-factor page
              </Link>
            </Notice>
          ) : null}
          <Button
            type="submit"
            appearance="primary"
            disabled={busy || (step === 4 && !stored)}
            {...(busy ? { icon: <Spinner size="tiny" /> } : {})}
          >
            {busy ? 'Checking…' : buttonLabel}
          </Button>
          {step === 3 && flow === 'password' ? (
            <Button
              appearance="subtle"
              disabled={busy}
              onClick={() => {
                setFailure(null);
                setStep(2);
              }}
            >
              Back
            </Button>
          ) : null}
        </form>
      )}
    </UnauthenticatedLayout>
  );
}

function TerminalState({ terminal }: { terminal: Terminal }) {
  const signIn = (
    <Link href="/sign-in" onClick={(event) => followLink(event, '/sign-in')}>
      Go to sign-in
    </Link>
  );
  switch (terminal) {
    case 'complete':
      return (
        <>
          <Text as="p">An administrator already exists, so this page is closed for good.</Text>
          {signIn}
        </>
      );
    case 'completed-elsewhere':
      return <Notice intent="info">Setup was completed in another browser. {signIn}</Notice>;
    case 'pending':
      return (
        <Notice intent="info">
          You’ll get a code by phone or in person once another administrator approves it. Open the link again then.
        </Notice>
      );
    default:
      return (
        <>
          <Notice intent={terminal === 'dead' ? 'error' : 'warning'}>
            {terminal === 'expired'
              ? 'This link has expired. Ask your administrator for a new one.'
              : terminal === 'dead'
                ? 'This link no longer works. Ask your administrator for a new one.'
                : 'This link is no longer valid. Ask your administrator for a new one.'}
          </Notice>
          <Text as="p">
            Links work once and expire. Your administrator can create a new link and code on the Users &amp; roles page.
          </Text>
        </>
      );
  }
}

function fieldProps(error: string | undefined) {
  return error ? { validationState: 'error' as const, validationMessage: error } : {};
}

function failureMessage(error: ApiError, flow: EnrolmentFlow): string | undefined {
  if (error.code === 'locked' && flow === 'password') {
    const minutes = typeof error.body.minutes === 'number' ? error.body.minutes : 15;
    const attempts = typeof error.body.attempts === 'number' ? error.body.attempts : 5;
    return `This account is locked for ${minutes} minutes after ${attempts} failed attempts. Try again later or ask an administrator to unlock it. This link keeps working until it expires.`;
  }
  if (error.code === 'locked') {
    return 'This account is locked after too many wrong passwords. Try again later or ask an administrator to unlock it.';
  }
  if (error.status === 429) {
    return 'Too many attempts from this address. Wait a minute and try again.';
  }
  return error.status === 0 ? 'Couldn’t reach Goldmark. Nothing was saved; try again.' : undefined;
}

export function describeFieldError(code: string): string {
  switch (code) {
    case 'PasswordCommon':
      return 'This is a commonly used password. Choose another.';
    case 'PasswordTooShort':
      return 'Use at least 12 characters.';
    case 'PasswordTooLong':
      return 'Use at most 128 characters.';
    case 'PasswordIncorrect':
      return 'That password isn’t right.';
    case 'DuplicateUserName':
      return 'That username is already taken.';
    case 'InvalidUserName':
      return 'Use letters, digits, dots, hyphens or underscores.';
    case 'UserNameTooLong':
      return 'Use at most 64 characters.';
    case 'InvalidDisplayName':
      return 'Use 1 to 200 characters.';
    default:
      return 'This value isn’t valid.';
  }
}

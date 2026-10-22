import { Button, Field, Input, Link, Spinner, Text, makeStyles, tokens } from '@fluentui/react-components';
import { useEffect, useRef, useState, type FormEvent } from 'react';

import { api, ApiError, type SessionUser } from '../api';
import { followLink, navigate, safeReturnPath, useLocation } from '../router';
import { useSession } from '../session';
import { CodeInput, ErrorBar, Notice, PageHeading, PasswordInput, UnauthenticatedLayout } from '../ui/common';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  links: { display: 'flex', justifyContent: 'space-between', alignItems: 'center', gap: tokens.spacingHorizontalS },
  monoInput: { fontFamily: tokens.fontFamilyMonospace, letterSpacing: '0.1em' },
  caption: {
    margin: 0,
    fontSize: tokens.fontSizeBase200,
    lineHeight: tokens.lineHeightBase200,
    color: tokens.colorNeutralForeground3,
  },
});

type Step = 'credentials' | 'code' | 'recovery';

type Message =
  | { kind: 'problem'; error: ApiError }
  | { kind: 'failure'; error: ApiError }
  | { kind: 'expired' | 'signed-out' | 'setup' | 'pending-expired' };

/** Screen 1 (US-03): user name and password, then a code from the authenticator or a recovery code. */
export function SignInPage() {
  const styles = useStyles();
  const session = useSession();
  const { search } = useLocation();
  const next = safeReturnPath(search.get('next'));
  const [step, setStep] = useState<Step>('credentials');
  const [userName, setUserName] = useState('');
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<Message | null>(() =>
    session.notice ? { kind: session.notice } : session.setup === 'required' ? { kind: 'setup' } : null,
  );
  const userNameRef = useRef<HTMLInputElement>(null);
  const codeRef = useRef<HTMLInputElement>(null);

  // The notice is shown once: this page now holds it in its own state.
  const { clearNotice } = session;
  useEffect(() => {
    clearNotice();
  }, [clearNotice]);

  useEffect(() => {
    (step === 'credentials' ? userNameRef : codeRef).current?.focus();
  }, [step]);

  async function submitCredentials() {
    const { data } = await api<{ next: string }>(
      'POST',
      '/api/v1/auth/sign-in',
      { userName, password },
      { signedIn: false },
    );
    if (data.next === 'two-factor') {
      setPassword('');
      setStep('code');
    }
  }

  async function submitSecondFactor() {
    const body = step === 'code' ? { code } : { recoveryCode };
    const { data } = await api<{ user: SessionUser; next?: string }>('POST', '/api/v1/auth/sign-in/two-factor', body, {
      signedIn: false,
    });
    session.signedIn(data.user);
    // Goldmark can no longer check the authenticator (its keys were lost, DEC-038 item 5): set it up again now.
    navigate(data.next === 'new-authenticator' ? '/account/two-factor?authenticator=new' : next, { replace: true });
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setBusy(true);
    setMessage(null);
    try {
      await (step === 'credentials' ? submitCredentials() : submitSecondFactor());
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      if (error.code === 'sign-in-expired') {
        setStep('credentials');
        setMessage({ kind: 'pending-expired' });
      } else if (error.isUnavailable) {
        setMessage({ kind: 'failure', error });
      } else {
        if (error.code === 'locked') {
          setStep('credentials');
        }
        setMessage({ kind: 'problem', error });
      }
    } finally {
      setBusy(false);
    }
  }

  const heading =
    step === 'code' ? 'Two-factor authentication' : step === 'recovery' ? 'Use a recovery code' : 'Sign in';

  return (
    <UnauthenticatedLayout>
      <PageHeading>{heading}</PageHeading>
      {message ? <SignInMessage message={message} onRetry={() => setMessage(null)} /> : null}
      <form className={styles.form} onSubmit={(event) => void onSubmit(event)} noValidate>
        {step === 'credentials' ? (
          <>
            <Field label="Username" required>
              <Input
                input={{ ref: userNameRef }}
                autoComplete="username"
                value={userName}
                readOnly={busy}
                onChange={(_, d) => setUserName(d.value)}
              />
            </Field>
            <Field label="Password" required>
              <PasswordInput
                autoComplete="current-password"
                value={password}
                readOnly={busy}
                onChange={(_, d) => setPassword(d.value)}
              />
            </Field>
          </>
        ) : step === 'code' ? (
          <>
            <Text as="p">Enter the 6-digit code from your authenticator app.</Text>
            <CodeInput label="6-digit code" inputRef={codeRef} value={code} onChange={setCode} />
          </>
        ) : (
          <>
            <Text as="p">Enter one of your recovery codes. Each code works once.</Text>
            <Field label="Recovery code">
              <Input
                input={{ ref: codeRef }}
                className={styles.monoInput}
                autoComplete="one-time-code"
                value={recoveryCode}
                onChange={(_, d) => setRecoveryCode(d.value)}
              />
            </Field>
          </>
        )}
        <Button type="submit" appearance="primary" disabled={busy} {...(busy ? { icon: <Spinner size="tiny" /> } : {})}>
          {busy
            ? step === 'credentials'
              ? 'Signing in…'
              : 'Checking…'
            : step === 'credentials'
              ? 'Sign in'
              : 'Verify'}
        </Button>
        {step === 'credentials' ? (
          // Text, not a link: there is no self-service reset, and Goldmark sends no email (DEC-042).
          <p className={styles.caption}>Forgot your password? Ask an administrator to reset it.</p>
        ) : null}
        {step !== 'credentials' ? (
          <div className={styles.links}>
            <Link
              as="button"
              type="button"
              onClick={() => {
                setMessage(null);
                setStep(step === 'code' ? 'recovery' : 'code');
              }}
            >
              {step === 'code' ? 'Use a recovery code instead' : 'Use your authenticator app instead'}
            </Link>
            <Button
              appearance="subtle"
              onClick={() => {
                setMessage(null);
                setStep('credentials');
              }}
            >
              Back
            </Button>
          </div>
        ) : null}
      </form>
    </UnauthenticatedLayout>
  );
}

function SignInMessage({ message, onRetry }: { message: Message; onRetry: () => void }) {
  switch (message.kind) {
    case 'expired':
      return <Notice intent="info">Your session expired. Sign in again.</Notice>;
    case 'signed-out':
      return <Notice intent="info">You’re signed out.</Notice>;
    case 'pending-expired':
      return <Notice intent="info">That took too long. Sign in again.</Notice>;
    case 'setup':
      return (
        <Notice intent="info">
          Goldmark isn’t set up yet.{' '}
          <Link href="/setup" onClick={(event) => followLink(event, '/setup')}>
            Go to first-run setup
          </Link>
        </Notice>
      );
    case 'failure':
      return <ErrorBar error={message.error} onRetry={onRetry} />;
    case 'problem': {
      const { error } = message;
      if (error.code === 'locked') {
        const minutes = typeof error.body.minutes === 'number' ? error.body.minutes : 15;
        const attempts = typeof error.body.attempts === 'number' ? error.body.attempts : 5;
        return (
          <Notice intent="warning">
            This account is locked for {minutes} minutes after {attempts} failed attempts. Try again later or ask an
            administrator to unlock it.
          </Notice>
        );
      }
      if (error.code === 'locked-by-administrator') {
        return <Notice intent="warning">This account is locked. Ask an administrator to unlock it.</Notice>;
      }
      if (error.code === 'two-factor-reset') {
        return (
          <Notice intent="info">
            Two-factor was reset for this account. Open the link your administrator gave you.
          </Notice>
        );
      }
      if (error.code === 'invalid-code') {
        return <Notice intent="error">That code didn’t work. Codes change every 30 seconds.</Notice>;
      }
      if (error.code === 'invalid-recovery-code') {
        return <Notice intent="error">That recovery code didn’t work, or it was already used.</Notice>;
      }
      if (error.status === 429) {
        return <Notice intent="warning">Too many attempts from this address. Wait a minute and try again.</Notice>;
      }
      return <Notice intent="error">Username or password is incorrect.</Notice>;
    }
  }
}

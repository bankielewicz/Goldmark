import {
  Badge,
  Button,
  Card,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Input,
  Link,
  Skeleton,
  SkeletonItem,
  Spinner,
  Text,
  Toast,
  ToastTitle,
  Toaster,
  makeStyles,
  tokens,
  useId,
  useToastController,
} from '@fluentui/react-components';
import { CheckmarkCircleRegular } from '@fluentui/react-icons';
import { useCallback, useEffect, useState, type FormEvent } from 'react';

import { api, ApiError } from '../api';
import { formatUtc } from '../format';
import { PageHeader } from '../shell/PageHeader';
import { AuthenticatorSetup, CodeInput, ErrorBar, Notice, PasswordInput, RecoveryCodes } from '../ui/common';

const useStyles = makeStyles({
  status: { maxWidth: '960px', display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  statusTop: { display: 'flex', alignItems: 'center', gap: tokens.spacingHorizontalM, flexWrap: 'wrap' },
  h2: {
    margin: 0,
    fontSize: tokens.fontSizeBase500,
    lineHeight: tokens.lineHeightBase500,
    fontWeight: tokens.fontWeightSemibold,
  },
  facts: {
    display: 'grid',
    gridTemplateColumns: 'max-content 1fr',
    gap: `${tokens.spacingVerticalXS} ${tokens.spacingHorizontalXXL}`,
    margin: 0,
  },
  term: { color: tokens.colorNeutralForeground2 },
  value: { margin: 0 },
  actions: {
    maxWidth: '960px',
    display: 'grid',
    gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
    gap: tokens.spacingHorizontalL,
    '@media (max-width: 1099px)': { gridTemplateColumns: 'minmax(0, 1fr)' },
  },
  action: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalS, alignItems: 'flex-start' },
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  mono: { fontFamily: tokens.fontFamilyMonospace, letterSpacing: '0.06em' },
});

interface Status {
  enabled: boolean;
  setUpAt: string | null;
  recoveryCodesRemaining: number;
  recoveryCodesTotal: number;
}

type DialogState =
  | { kind: 'confirm-codes' }
  | { kind: 'new-codes'; codes: string[] }
  | { kind: 'confirm-authenticator' }
  | { kind: 'new-authenticator'; key: string; version: number };

/** The signed-in person's own two-factor page (DEC-038 item 9): status, new codes, a new authenticator. */
export function TwoFactorPage() {
  const styles = useStyles();
  const toasterId = useId('toaster');
  const { dispatchToast } = useToastController(toasterId);
  const [status, setStatus] = useState<Status | null>(null);
  const [failure, setFailure] = useState<ApiError | null>(null);
  const [dialog, setDialog] = useState<DialogState | null>(null);

  const load = useCallback(async () => {
    setFailure(null);
    try {
      setStatus((await api<Status>('GET', '/api/v1/account/two-factor')).data);
    } catch (error) {
      if (error instanceof ApiError) setFailure(error);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const toast = (text: string) =>
    dispatchToast(
      <Toast>
        <ToastTitle media={<CheckmarkCircleRegular />}>{text}</ToastTitle>
      </Toast>,
      { intent: 'success', politeness: 'polite' },
    );

  return (
    <>
      <Toaster toasterId={toasterId} position="bottom-end" />
      <PageHeader
        crumbs={[{ label: 'Account' }, { label: 'Two-factor authentication' }]}
        title="Two-factor authentication"
      />
      {failure ? <ErrorBar error={failure} onRetry={() => void load()} /> : null}
      {!status && !failure ? (
        <Skeleton aria-label="Loading">
          <SkeletonItem size={48} />
        </Skeleton>
      ) : null}
      {status ? (
        <>
          <Card appearance="outline" className={styles.status}>
            <div className={styles.statusTop}>
              <h2 className={styles.h2}>Two-factor authentication is on</h2>
              <Badge appearance="tint" color="success" icon={<CheckmarkCircleRegular />}>
                On
              </Badge>
            </div>
            <dl className={styles.facts}>
              <dt className={styles.term}>Authenticator app</dt>
              <dd className={styles.value}>
                {status.setUpAt ? `Set up on ${formatUtc(status.setUpAt).slice(0, 10)}` : 'Not set up'}
              </dd>
              <dt className={styles.term}>Recovery codes</dt>
              <dd className={styles.value}>
                {status.recoveryCodesRemaining} of {status.recoveryCodesTotal} left
              </dd>
            </dl>
            <Text>Two-factor authentication is required for every account and can’t be turned off.</Text>
          </Card>
          <div className={styles.actions}>
            <Card appearance="outline" className={styles.action}>
              <h2 className={styles.h2}>Recovery codes</h2>
              <Text>Make a new set if you’ve used some or think they were seen. The old codes stop working.</Text>
              <Button onClick={() => setDialog({ kind: 'confirm-codes' })}>Generate new recovery codes</Button>
            </Card>
            <Card appearance="outline" className={styles.action}>
              <h2 className={styles.h2}>Authenticator app</h2>
              <Text>Moving to a new phone? Set up the new app here; the old one stops working.</Text>
              <Button onClick={() => setDialog({ kind: 'confirm-authenticator' })}>Set up a new authenticator</Button>
            </Card>
          </div>
        </>
      ) : null}
      {dialog ? (
        <TwoFactorDialog
          state={dialog}
          remaining={status?.recoveryCodesRemaining ?? 0}
          onChange={setDialog}
          onClose={() => setDialog(null)}
          onDone={(message) => {
            setDialog(null);
            toast(message);
            void load();
          }}
        />
      ) : null}
    </>
  );
}

function TwoFactorDialog({
  state,
  remaining,
  onChange,
  onClose,
  onDone,
}: {
  state: DialogState;
  remaining: number;
  onChange: (state: DialogState) => void;
  onClose: () => void;
  onDone: (message: string) => void;
}) {
  const styles = useStyles();
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [recoveryCode, setRecoveryCode] = useState('');
  const [useRecovery, setUseRecovery] = useState(false);
  const [problem, setProblem] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);
  const [stored, setStored] = useState(false);

  const locked = problem?.code === 'locked';
  const wrong = problem?.code === 'validation';

  async function run(action: () => Promise<void>) {
    setBusy(true);
    setProblem(null);
    try {
      await action();
    } catch (error) {
      if (error instanceof ApiError) setProblem(error);
      else throw error;
    } finally {
      setBusy(false);
    }
  }

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    switch (state.kind) {
      case 'confirm-codes':
        await run(async () => {
          const { data } = await api<{ recoveryCodes: string[] }>('POST', '/api/v1/account/two-factor/recovery-codes', {
            password,
            code,
          });
          onChange({ kind: 'new-codes', codes: data.recoveryCodes });
        });
        return;
      case 'new-codes':
        onDone('New recovery codes are ready');
        return;
      case 'confirm-authenticator':
        await run(async () => {
          await api(
            'POST',
            '/api/v1/account/two-factor/authenticator',
            useRecovery ? { password, recoveryCode } : { password, code },
          );
          const { data } = await api<{ key: string }>(
            'POST',
            '/api/v1/enrolment/authenticator',
            {},
            {
              // An expired enrolment is this dialog's problem, not the end of the session.
              signedIn: false,
            },
          );
          setCode('');
          onChange({ kind: 'new-authenticator', key: data.key, version: Date.now() });
        });
        return;
      case 'new-authenticator':
        await run(async () => {
          await api('POST', '/api/v1/account/two-factor/authenticator/verify', { code });
          onDone('New authenticator set up. The old one no longer works.');
        });
        return;
    }
  }

  const title = {
    'confirm-codes': 'Generate new recovery codes',
    'new-codes': 'Your new recovery codes',
    'confirm-authenticator': 'Set up a new authenticator',
    'new-authenticator': 'Scan with your new authenticator',
  }[state.kind];
  const primary = {
    'confirm-codes': 'Generate',
    'new-codes': 'Done',
    'confirm-authenticator': 'Continue',
    'new-authenticator': 'Verify',
  }[state.kind];
  const confirming = state.kind === 'confirm-codes' || state.kind === 'confirm-authenticator';
  const wrongMessage = 'That code didn’t work, or the password is wrong.';

  return (
    <Dialog
      open
      modalType={state.kind === 'new-codes' ? 'alert' : 'modal'}
      onOpenChange={(_, data) => (data.open ? undefined : onClose())}
    >
      <DialogSurface>
        <form onSubmit={(event) => void onSubmit(event)} noValidate>
          <DialogBody>
            <DialogTitle>{title}</DialogTitle>
            <DialogContent className={styles.form}>
              {locked ? (
                <Notice intent="warning">
                  Too many wrong tries. Your account is locked for 15 minutes; you stay signed in.
                </Notice>
              ) : null}
              {problem && !locked && !wrong && problem.code !== 'invalid-code' ? <ErrorBar error={problem} /> : null}
              {confirming ? (
                <>
                  <Text as="p">
                    {state.kind === 'confirm-codes'
                      ? `Confirm it’s you. Your ${remaining} remaining codes stop working.`
                      : 'Confirm it’s you with your password and a code from your current app.'}
                  </Text>
                  <Field label="Password" required>
                    <PasswordInput
                      autoComplete="current-password"
                      value={password}
                      readOnly={locked}
                      onChange={(_, d) => setPassword(d.value)}
                    />
                  </Field>
                  {useRecovery ? (
                    <Field
                      label="Recovery code"
                      validationState={wrong ? 'error' : 'none'}
                      {...(wrong ? { validationMessage: wrongMessage } : {})}
                    >
                      <Input
                        className={styles.mono}
                        autoComplete="one-time-code"
                        value={recoveryCode}
                        onChange={(_, d) => setRecoveryCode(d.value)}
                      />
                    </Field>
                  ) : (
                    <CodeInput
                      label="Current 6-digit code"
                      value={code}
                      onChange={setCode}
                      error={wrong ? wrongMessage : null}
                      disabled={locked}
                    />
                  )}
                  {state.kind === 'confirm-authenticator' ? (
                    <Link as="button" type="button" onClick={() => setUseRecovery((v) => !v)}>
                      {useRecovery
                        ? 'Use a code from your current app instead'
                        : 'Lost the old phone? Use a recovery code instead'}
                    </Link>
                  ) : null}
                </>
              ) : null}
              {state.kind === 'new-codes' ? (
                <RecoveryCodes
                  codes={state.codes}
                  stored={stored}
                  onStoredChange={setStored}
                  intro="Your old codes no longer work."
                />
              ) : null}
              {state.kind === 'new-authenticator' ? (
                <AuthenticatorSetup
                  keyText={state.key}
                  qrSource={`/api/v1/enrolment/authenticator.svg?v=${state.version}`}
                  code={code}
                  onCodeChange={setCode}
                  error={
                    problem?.code === 'invalid-code' ? 'That code didn’t work. Codes change every 30 seconds.' : null
                  }
                  codeLabel="6-digit code from the new app"
                  intro="Scan with the new app, or enter the key. The old authenticator stops working once you verify."
                />
              ) : null}
            </DialogContent>
            <DialogActions>
              {state.kind !== 'new-codes' ? (
                <Button appearance="secondary" onClick={onClose}>
                  Cancel
                </Button>
              ) : null}
              <Button
                type="submit"
                appearance="primary"
                disabled={busy || locked || (state.kind === 'new-codes' && !stored)}
                {...(busy ? { icon: <Spinner size="tiny" /> } : {})}
              >
                {primary}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  );
}

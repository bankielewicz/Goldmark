import {
  Button,
  Dialog,
  DialogActions,
  DialogBody,
  DialogContent,
  DialogSurface,
  DialogTitle,
  Field,
  Spinner,
  Text,
  makeStyles,
  tokens,
} from '@fluentui/react-components';
import { useState, type FormEvent } from 'react';

import { api, ApiError } from '../api';
import { describeFieldError } from '../pages/EnrolmentPage';
import { ErrorBar, Notice, PasswordChecklist, PasswordInput } from '../ui/common';

const useStyles = makeStyles({
  form: { display: 'flex', flexDirection: 'column', gap: tokens.spacingVerticalM },
  surface: { maxWidth: '480px', '@media (max-width: 479px)': { maxWidth: '100%' } },
});

/**
 * Change password, from the account menu (DEC-042 item 5; ASVS 5.0 6.2.2, 6.2.3): the current
 * password, then a new one under the policy. This session continues; the others end. A wrong current
 * password counts toward lockout, and a lockout keeps the person signed in, as on the two-factor page.
 */
export function ChangePasswordDialog({ onClose, onChanged }: { onClose: () => void; onChanged: () => void }) {
  const styles = useStyles();
  const [current, setCurrent] = useState('');
  const [password, setPassword] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [common, setCommon] = useState<boolean | null>(null);
  const [problem, setProblem] = useState<ApiError | null>(null);
  const [busy, setBusy] = useState(false);

  const locked = problem?.code === 'locked';

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    const found: Record<string, string> = {};
    if (current.length === 0) found.currentPassword = 'Enter your current password.';
    if (password.length < 12 || password.length > 128) found.password = 'Use 12 to 128 characters.';
    else if (password !== confirmation) found.confirmation = 'The passwords don’t match.';
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setBusy(true);
    setProblem(null);
    try {
      await api('POST', '/api/v1/account/password', { currentPassword: current, newPassword: password });
      onChanged();
    } catch (error) {
      if (!(error instanceof ApiError)) throw error;
      if (error.code === 'validation') {
        const fields: Record<string, string> = {};
        if (error.hasField('currentPassword', 'PasswordIncorrect'))
          fields.currentPassword = 'That password is incorrect.';
        const policy = error.fieldErrors.password ?? error.fieldErrors.newPassword;
        if (policy?.[0]) fields.password = describeFieldError(policy[0]);
        setCommon(policy?.includes('PasswordCommon') ? true : common);
        setErrors(fields);
      } else {
        setProblem(error);
      }
    } finally {
      setBusy(false);
    }
  }

  const readOnly = busy || locked;
  return (
    <Dialog open onOpenChange={(_, data) => (data.open ? undefined : onClose())}>
      <DialogSurface className={styles.surface}>
        <form onSubmit={(event) => void onSubmit(event)} noValidate>
          <DialogBody>
            <DialogTitle>Change your password</DialogTitle>
            <DialogContent className={styles.form}>
              {locked ? (
                <Notice intent="warning">
                  Too many wrong tries. Your account is locked for 15 minutes; you stay signed in.
                </Notice>
              ) : null}
              {problem && !locked ? (
                <ErrorBar
                  error={problem}
                  {...(problem.status === 0
                    ? { message: 'Couldn’t reach Goldmark. Your password hasn’t changed; try again.' }
                    : {})}
                />
              ) : null}
              <Field label="Current password" required {...fieldProps(errors.currentPassword)}>
                <PasswordInput
                  autoComplete="current-password"
                  value={current}
                  readOnly={readOnly}
                  onChange={(_, d) => setCurrent(d.value)}
                />
              </Field>
              <Field label="New password" required {...fieldProps(errors.password)}>
                <PasswordInput
                  autoComplete="new-password"
                  value={password}
                  readOnly={readOnly}
                  onChange={(_, d) => {
                    setPassword(d.value);
                    setCommon(null);
                  }}
                />
              </Field>
              <Field label="Confirm new password" required {...fieldProps(errors.confirmation)}>
                <PasswordInput
                  autoComplete="new-password"
                  value={confirmation}
                  readOnly={readOnly}
                  onChange={(_, d) => setConfirmation(d.value)}
                />
              </Field>
              <PasswordChecklist password={password} confirmation={confirmation} common={common} checkedWhen="save" />
              <Text as="p">You stay signed in here. Goldmark signs you out on your other devices and browsers.</Text>
            </DialogContent>
            <DialogActions>
              <Button appearance="secondary" onClick={onClose}>
                Cancel
              </Button>
              <Button
                type="submit"
                appearance="primary"
                disabled={readOnly}
                {...(busy ? { icon: <Spinner size="tiny" /> } : {})}
              >
                {busy ? 'Changing…' : 'Change password'}
              </Button>
            </DialogActions>
          </DialogBody>
        </form>
      </DialogSurface>
    </Dialog>
  );
}

function fieldProps(error: string | undefined) {
  return error ? { validationState: 'error' as const, validationMessage: error } : {};
}

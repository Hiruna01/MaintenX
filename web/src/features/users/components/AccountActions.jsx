import { KeyRound, Power, PowerOff, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { deactivateUser, reactivateUser } from '../services/usersApi';
import styles from '../users.module.css';
import AccountStatusPill from './AccountStatusPill';
import ResetPasswordForm from './ResetPasswordForm';

/**
 * The account's switches, under the edit form in the manage panel: deactivate or reactivate,
 * and reset the password. Each change is two steps — choose, then confirm.
 *
 * Deactivating is what "delete" means here: DELETE /api/users/{id} switches the account off
 * and keeps the row, so every report, answer and work order that names this person still
 * does. It takes effect on their next request, not when their token runs out.
 *
 * Not offered on the Admin's OWN account — the API refuses that (409). A Technician with live
 * work is still offered it; the API's 409 says why not, and the count beside it warns first.
 *
 * `onChanged(message)` is called after a successful change, to reload the panel and the list.
 */
export function AccountActions({ user, isSelf, onChanged }) {
  const [confirming, setConfirming] = useState(false);
  const [resetting, setResetting] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [error, setError] = useState(null);

  async function handleToggle() {
    setIsSubmitting(true);
    setError(null);

    try {
      if (user.isActive) {
        await deactivateUser(user.id);
        onChanged(`${user.fullName} is deactivated and signed out. Their history is kept.`);
      } else {
        await reactivateUser(user.id);
        onChanged(`${user.fullName} can sign in again.`);
      }
    } catch (caught) {
      setError(caught.message);
      setIsSubmitting(false);
      setConfirming(false);
    }
  }

  return (
    <section className={styles.account} aria-labelledby="account-heading">
      <div className={styles.accountHead}>
        <h3 id="account-heading" className={styles.formLegend}>
          Account
        </h3>
        <AccountStatusPill isActive={user.isActive} />
      </div>

      {user.liveWorkOrderCount > 0 ? (
        <p className={styles.sectionNote}>
          {user.liveWorkOrderCount} unfinished {user.liveWorkOrderCount === 1 ? 'work order is' : 'work orders are'} assigned to
          them. Only the assigned technician can complete a job, so reassign {user.liveWorkOrderCount === 1 ? 'it' : 'them'}{' '}
          before deactivating them or changing their role.
        </p>
      ) : null}

      {error ? (
        <p className={form.submitError} role="alert">
          {error}
        </p>
      ) : null}

      <div className={styles.accountButtons}>
        {isSelf ? (
          <p className={styles.sectionNote}>This is your account — another Admin would have to deactivate it.</p>
        ) : confirming ? (
          <>
            <MxButton icon={X} onClick={() => setConfirming(false)} disabled={isSubmitting}>
              Keep as it is
            </MxButton>
            <MxButton
              variant={user.isActive ? 'danger' : 'primary'}
              icon={user.isActive ? PowerOff : Power}
              onClick={handleToggle}
              disabled={isSubmitting}
            >
              {isSubmitting
                ? 'Saving…'
                : user.isActive
                  ? 'Confirm deactivate — history is kept'
                  : 'Confirm reactivate'}
            </MxButton>
          </>
        ) : (
          <MxButton icon={user.isActive ? PowerOff : Power} onClick={() => setConfirming(true)}>
            {user.isActive ? 'Deactivate' : 'Reactivate'}
          </MxButton>
        )}

        {!resetting && !confirming ? (
          <MxButton icon={KeyRound} onClick={() => setResetting(true)}>
            Reset password
          </MxButton>
        ) : null}
      </div>

      {resetting ? (
        <ResetPasswordForm
          userId={user.id}
          onCancel={() => setResetting(false)}
          onDone={() => {
            setResetting(false);
            onChanged(`New password set for ${user.fullName}. Give it to them in person.`);
          }}
        />
      ) : null}
    </section>
  );
}

export default AccountActions;

import useRoutePanel from '../../../components/ui/useRoutePanel';
import UserForm from '../components/UserForm';
import UserSheet from '../components/UserSheet';
import { createUser } from '../services/usersApi';
import { EMPTY_USER_VALUES } from '../services/userValidation';

/**
 * Create an account — a slide-over on top of the user list (/users/new). Admin only, like the
 * whole /users route. On success it slides away and opens the new account's manage panel, and
 * the list behind reloads.
 */
export function UserCreatePage() {
  const panel = useRoutePanel('/users');

  async function handleSubmit(values) {
    const created = await createUser(values);
    panel.closeThen(`/users/${created.id}`, {
      state: { refresh: Date.now(), notice: `Account created for ${created.fullName}. It can sign in now.` },
    });
  }

  return (
    <UserSheet
      panel={panel}
      title="Create account"
      description="Staff accounts are made here. Reporters can also sign themselves up on the phone."
    >
      {({ onDirtyChange, onCancel }) => (
        <UserForm
          mode="create"
          initialValues={EMPTY_USER_VALUES}
          onSubmit={handleSubmit}
          onCancel={onCancel}
          onDirtyChange={onDirtyChange}
        />
      )}
    </UserSheet>
  );
}

export default UserCreatePage;

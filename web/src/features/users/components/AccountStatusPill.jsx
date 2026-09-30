import { Pill } from '../../../components/ui/Pill';

/** Whether the account can sign in — the API's `isActive`, coloured and nothing more. */
export function AccountStatusPill({ isActive }) {
  return isActive ? <Pill tone="green">Active</Pill> : <Pill tone="slate">Deactivated</Pill>;
}

export default AccountStatusPill;

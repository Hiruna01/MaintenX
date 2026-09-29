import { Monitor, Smartphone } from 'lucide-react';

import styles from '../landing.module.css';

// The four roles, and where each one works. A Reporter and a Technician are on the phone; the
// two who run the estate are on the web — the same split the clients were built for.
const ROLES = [
  {
    name: 'Reporter',
    where: 'Phone',
    body: 'Scan the sticker, say what’s wrong, answer two questions — then tell us if the fix held.',
  },
  {
    name: 'Technician',
    where: 'Phone',
    body: 'See the machine’s history and the diagnosis before you arrive. Close the job with a note it will remember.',
  },
  {
    name: 'Facilities Manager',
    where: 'Web',
    dark: true,
    body: 'Raise, approve, assign and book — with the diagnosis and the asset’s history right beside you.',
  },
  {
    name: 'Admin',
    where: 'Web',
    body: 'Keep the asset registry honest and print the QR stickers that start it all.',
  },
];

export function RolesSection() {
  return (
    <section id="roles" className={styles.section} aria-labelledby="roles-title">
      <div className={styles.centered}>
        <span className={styles.label}>For your role</span>
        <h2 id="roles-title" className={styles.h2}>
          Everyone does one part. Well.
        </h2>
      </div>
      <ul className={styles.roles}>
        {ROLES.map((role) => {
          const Icon = role.where === 'Phone' ? Smartphone : Monitor;
          return (
            <li key={role.name} className={`${styles.role} ${role.dark ? styles.roleDark : ''}`}>
              <div className={styles.roleTop}>
                <span className={styles.roleIcon} aria-hidden="true">
                  <Icon size={22} strokeWidth={1.8} />
                </span>
                <span className={styles.roleWhere}>{role.where}</span>
              </div>
              <div>
                <h3 className={styles.roleName}>{role.name}</h3>
                <p className={styles.roleBody}>{role.body}</p>
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}

export default RolesSection;

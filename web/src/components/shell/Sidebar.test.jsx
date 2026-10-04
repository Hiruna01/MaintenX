import { screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { ROLES } from '../../features/auth/services/roles';
import { renderWithAuth, signedInAs } from '../../test/auth';
import Sidebar from './Sidebar';

function linkLabels(role) {
  renderWithAuth(<Sidebar open={false} onNavigate={() => {}} />, signedInAs(role));
  const nav = screen.getByRole('complementary', { name: 'Sidebar' });
  return within(nav)
    .getAllByRole('link')
    .map((link) => link.textContent.trim())
    .filter(Boolean);
}

// Role-based navigation: a link a role would only be refused on is never offered.
describe('Sidebar', () => {
  it('offers a Reporter their own checks and the registry, and none of the staff pages', () => {
    const links = linkLabels(ROLES.Reporter);

    expect(links).toEqual(expect.arrayContaining(['Dashboard', 'Assets', 'Verification']));
    for (const hidden of ['Reports', 'Work orders', 'Approvals', 'Workflows', 'Metrics', 'Agent monitoring', 'Users']) {
      expect(links).not.toContain(hidden);
    }
  });

  it('offers a Technician work orders but not approvals or verification', () => {
    const links = linkLabels(ROLES.Technician);

    expect(links).toContain('Work orders');
    expect(links).not.toContain('Approvals');
    expect(links).not.toContain('Verification');
    expect(links).not.toContain('Agent monitoring');
  });

  it('offers a FacilitiesManager the approval queue and metrics, but not user management', () => {
    const links = linkLabels(ROLES.FacilitiesManager);

    expect(links).toEqual(expect.arrayContaining(['Approvals', 'Metrics', 'Agent monitoring', 'Reports', 'Workflows']));
    expect(links).not.toContain('Users');
  });

  it('offers an Admin users and buildings, but not the approval queue', () => {
    const links = linkLabels(ROLES.Admin);

    expect(links).toEqual(expect.arrayContaining(['Users', 'Buildings & rooms', 'Metrics', 'Agent monitoring']));
    expect(links).not.toContain('Approvals');
  });
});

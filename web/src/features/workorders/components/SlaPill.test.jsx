import { render, screen } from '@testing-library/react';
import { describe, expect, it } from 'vitest';

import { SLA_STATES } from '../services/workOrdersApi';
import SlaPill from './SlaPill';

const DUE = '2026-10-07T12:00:00Z';

// A presentational component: it shows the API's verdict and never decides one.
describe('SlaPill', () => {
  it('renders nothing for "None" on the board, so orders with no clock stay quiet', () => {
    const { container } = render(<SlaPill sla="None" dueAt={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it('says so when asked to show "None"', () => {
    render(<SlaPill sla="None" dueAt={null} showNone />);
    expect(screen.getByText('No SLA clock yet')).toBeInTheDocument();
  });

  it.each([
    ['OnTrack', /^On track · due /],
    ['Overdue', /^Overdue · was due /],
    ['Met', /^SLA met$/],
    ['Missed', /^SLA missed$/],
  ])('labels %s', (sla, label) => {
    render(<SlaPill sla={sla} dueAt={DUE} />);
    expect(screen.getByText(label)).toBeInTheDocument();
  });

  it('shows Overdue from the API even when the browser clock says the due time is years away', () => {
    // The verdict is SlaRules in C#; the pill must not second-guess it with Date.now().
    render(<SlaPill sla="Overdue" dueAt="2099-01-01T00:00:00Z" />);
    expect(screen.getByText(/^Overdue/)).toBeInTheDocument();
  });

  it('knows every SlaState the API sends', () => {
    expect(SLA_STATES).toEqual(['None', 'OnTrack', 'Overdue', 'Met', 'Missed']);
  });
});

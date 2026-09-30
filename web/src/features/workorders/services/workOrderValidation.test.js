import { describe, expect, it } from 'vitest';

import { EMPTY_COMPLETION_VALUES, validateCompletion, validateDecisionNote } from './workOrderValidation';

const NOTE = 'Replaced the lamp and cleaned the air filter.';

function complete(overrides) {
  return validateCompletion({ ...EMPTY_COMPLETION_VALUES, actualCost: '6500', outcome: 'Resolved', resolutionNote: NOTE, ...overrides });
}

// Form validation that mirrors CompleteWorkOrderDto — the same limits the API enforces.
describe('validateCompletion', () => {
  it('accepts a complete, well-formed completion', () => {
    expect(complete({})).toEqual({});
  });

  it('requires an actual cost: a missing one must never be recorded as a free job', () => {
    expect(complete({ actualCost: '  ' }).actualCost).toMatch(/actually cost/);
  });

  it.each(['12.345', '-5', '1e3', 'abc'])('refuses %s as money (rupees, at most two decimals)', (cost) => {
    expect(complete({ actualCost: cost }).actualCost).toBeDefined();
  });

  it('refuses an amount above Rs 10,000,000', () => {
    expect(complete({ actualCost: '10000000.01' }).actualCost).toMatch(/typo/);
    expect(complete({ actualCost: '10000000' }).actualCost).toBeUndefined();
  });

  it('has no default outcome — one left out is an error, not "Resolved"', () => {
    expect(complete({ outcome: '' }).outcome).toBe('Choose how the job ended.');
  });

  it('wants at least 20 characters of note after trimming', () => {
    expect(complete({ resolutionNote: `   ${'x'.repeat(19)}   ` }).resolutionNote).toMatch(/at least 20/);
    expect(complete({ resolutionNote: 'x'.repeat(20) }).resolutionNote).toBeUndefined();
  });

  it('accepts only an http(s) link for the photo', () => {
    expect(complete({ completionPhotoUrl: 'ftp://x/y.jpg' }).completionPhotoUrl).toMatch(/http/);
    expect(complete({ completionPhotoUrl: 'https://x/y.jpg' }).completionPhotoUrl).toBeUndefined();
  });
});

describe('validateDecisionNote', () => {
  it('requires a reason and caps it at 1000 characters', () => {
    expect(validateDecisionNote('   ', 'A reason')).toEqual({ note: 'A reason is required.' });
    expect(validateDecisionNote('x'.repeat(1001), 'A reason').note).toMatch(/1000/);
    expect(validateDecisionNote('Out of budget.', 'A reason')).toEqual({});
  });
});

import { Boxes, ClipboardPlus, RotateCcw } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { Panel } from '../../../components/ui/Panel';
import { Pill } from '../../../components/ui/Pill';
import SelectMenu from '../../../components/ui/SelectMenu';
import Skeleton from '../../../components/ui/Skeleton';
import { EmptyState, ErrorState } from '../../../components/ui/States';
import { enumLabel } from '../../assets/services/assetsApi';
import useRoomAssets from '../hooks/useRoomAssets';
import {
  initialRaiseValues,
  initialResubmitValues,
  PARTS_REQUIRED_MAX,
  validateRaiseWorkOrder,
} from '../services/workOrderValidation';
import {
  STRATEGIES,
  adviceLabel,
  createWorkOrder,
  formatMoney,
  getWorkOrder,
  resubmitWorkOrder,
  strategyLabel,
} from '../services/workOrdersApi';
import styles from '../workorders.module.css';
import StrategyPill from './StrategyPill';

const URGENCY_TONES = { high: 'red', medium: 'amber', low: 'slate' };

const STRATEGY_OPTIONS = STRATEGIES.map((strategy) => ({ value: strategy, label: strategyLabel(strategy) }));

/**
 * Where the agent's run hands over to a person: a Facilities Manager raises the work order.
 *
 * Rendered only when the API says an order can be raised now (`latestWorkflow
 * .canRaiseWorkOrder`, read off WorkflowTransitions in C#) and only for a FacilitiesManager,
 * the one role POST /api/workorders admits. Pre-filled from the strategist's proposal, which
 * is advice: the manager may change every field.
 *
 * Whether the order then needs a manager's decision is not decided here and not predicted
 * here — the API compares the estimate with its threshold and says where the order landed.
 *
 * With `revisionDraft` (ReportDetailDto.revisionDraft) it RESUBMITS that order instead: the
 * order a manager sent back, waiting in Draft. Same form, same gate on the API, but the same
 * order — its asset is fixed, and it starts from the values that were sent back. The API
 * refuses a second order while the Draft exists, so this is the only control offered then.
 */
export function RaiseWorkOrderPanel({ report, revisionDraft = null, onRaised }) {
  const [values, setValues] = useState(() =>
    revisionDraft ? initialResubmitValues(revisionDraft) : initialRaiseValues(report),
  );
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const assets = useRoomAssets(report.room.id);

  function update(field, value) {
    setValues((current) => ({ ...current, [field]: value }));
    setErrors((current) => {
      if (!current[field]) return current;
      const next = { ...current };
      delete next[field];
      return next;
    });
  }

  async function handleSubmit(event) {
    event.preventDefault();
    const found = validateRaiseWorkOrder(values);
    setErrors(found);
    setSubmitError(null);
    if (Object.keys(found).length > 0) return;

    setIsSubmitting(true);
    try {
      if (revisionDraft) {
        await resubmitWorkOrder(revisionDraft.workOrderId, values);
        onRaised(await readBack(revisionDraft.workOrderId));
      } else {
        onRaised(await createWorkOrder(report.id, values));
      }
    } catch (err) {
      // A 409 is the report having moved on since this page loaded — said as the API said it.
      setSubmitError(err.message);
      setIsSubmitting(false);
    }
  }

  const failedRun = report.latestWorkflow?.state === 'Failed';

  return (
    <Panel
      eyebrow={revisionDraft ? 'Resubmit the revised order' : 'Raise a work order'}
      actions={<span className={styles.source}>Facilities Manager</span>}
    >
      {revisionDraft ? (
        <RevisionLead draft={revisionDraft} />
      ) : (
        <>
          <p className={styles.raiseLead}>
            {failedRun
              ? 'The agent run for this report failed, so there is no proposal to start from. Raise the order from the report as written.'
              : 'The agents have finished with this report. Their proposal is filled in below as a starting point — change anything before raising the order.'}
          </p>

          <ProposalSummary proposal={report.proposal} />
        </>
      )}

      <form className={styles.raiseForm} onSubmit={handleSubmit} noValidate>
        <div className={form.grid}>
          <div className={`${form.field} ${form.wide}`}>
            <label htmlFor="raise-asset" className={form.label}>
              Equipment
            </label>
            {revisionDraft ? (
              // The order's own asset, and not a choice: a revision re-plans the job, it does
              // not move it to other equipment. ResubmitWorkOrderDto has no field for it.
              <p id="raise-asset" className="mx-mono">
                {revisionDraft.assetTag}
              </p>
            ) : (
              <AssetPicker
                state={assets}
                value={values.assetId}
                onChange={(value) => update('assetId', value)}
                disabled={isSubmitting}
                error={errors.assetId}
              />
            )}
            {errors.assetId ? (
              <p className={form.error} id="raise-asset-error" role="alert">
                {errors.assetId}
              </p>
            ) : null}
          </div>

          <div className={form.field}>
            <label htmlFor="raise-strategy" className={form.label}>
              Strategy
            </label>
            <SelectMenu
              id="raise-strategy"
              block
              value={values.strategy}
              onChange={(value) => update('strategy', value)}
              options={STRATEGY_OPTIONS}
              placeholder="Choose a strategy…"
              disabled={isSubmitting}
              invalid={Boolean(errors.strategy)}
              describedBy={errors.strategy ? 'raise-strategy-error' : undefined}
            />
            {errors.strategy ? (
              <p className={form.error} id="raise-strategy-error" role="alert">
                {errors.strategy}
              </p>
            ) : null}
          </div>

          <div className={form.field}>
            <label htmlFor="raise-cost" className={form.label}>
              Estimated cost (Rs)
            </label>
            <input
              id="raise-cost"
              className={`${form.input} ${form.mono}`}
              inputMode="decimal"
              autoComplete="off"
              value={values.estimatedCost}
              onChange={(event) => update('estimatedCost', event.target.value)}
              disabled={isSubmitting}
              aria-invalid={errors.estimatedCost ? 'true' : 'false'}
              aria-describedby={errors.estimatedCost ? 'raise-cost-error' : undefined}
            />
            {errors.estimatedCost ? (
              <p className={form.error} id="raise-cost-error" role="alert">
                {errors.estimatedCost}
              </p>
            ) : null}
          </div>

          <div className={`${form.field} ${form.wide}`}>
            <label htmlFor="raise-parts" className={form.label}>
              Parts required <span className={form.optional}>optional</span>
            </label>
            <textarea
              id="raise-parts"
              className={form.textarea}
              rows={2}
              maxLength={PARTS_REQUIRED_MAX}
              value={values.partsRequired}
              onChange={(event) => update('partsRequired', event.target.value)}
              disabled={isSubmitting}
              aria-invalid={errors.partsRequired ? 'true' : 'false'}
              aria-describedby={errors.partsRequired ? 'raise-parts-error' : undefined}
            />
            {errors.partsRequired ? (
              <p className={form.error} id="raise-parts-error" role="alert">
                {errors.partsRequired}
              </p>
            ) : null}
          </div>
        </div>

        {submitError ? (
          <p className={form.submitError} role="alert">
            {submitError}
          </p>
        ) : null}

        <div className={form.actions}>
          <span className={styles.raiseHint}>Whether it needs approval is decided by the API from the estimate.</span>
          {revisionDraft ? (
            <MxButton type="submit" variant="primary" icon={RotateCcw} disabled={isSubmitting}>
              {isSubmitting ? 'Resubmitting…' : `Resubmit work order #${revisionDraft.workOrderId}`}
            </MxButton>
          ) : (
            <MxButton type="submit" variant="primary" icon={ClipboardPlus} disabled={isSubmitting}>
              {isSubmitting ? 'Raising…' : 'Raise work order'}
            </MxButton>
          )}
        </div>
      </form>
    </Panel>
  );
}

/**
 * The order read back after a 204, for the notice to say which side of the gate it landed
 * on. The resubmit itself has already succeeded; if the read fails, the notice says so
 * without a status rather than showing the resubmit as failed.
 */
async function readBack(workOrderId) {
  try {
    return { ...(await getWorkOrder(workOrderId)), resubmitted: true };
  } catch {
    return { id: workOrderId, status: null, resubmitted: true };
  }
}

/** What the manager sent back, verbatim, and who acts next. */
function RevisionLead({ draft }) {
  return (
    <>
      <p className={styles.raiseLead}>
        Work order #{draft.workOrderId} was sent back for revision and is waiting in Draft. The strategist re-plans it
        with the note below, and the runner resubmits it through the approval gate itself when it has a usable proposal
        — the reasoning panel shows what it said. Resubmit it by hand if it could not, or if you will not wait.
      </p>
      <div className={styles.raiseProposal}>
        <span className={styles.miniLabel}>Revision note</span>
        {/* Verbatim — it is what the order is being re-planned against. */}
        <p className={styles.raiseProposalText}>{draft.revisionNote}</p>
      </div>
    </>
  );
}

/** The strategist's proposal in one block, labelled advice. Null and failed say different things. */
function ProposalSummary({ proposal }) {
  if (!proposal) return null;

  if (proposal.validationResult !== 'Ok' || !proposal.outputReadable) {
    return (
      <div className={styles.raiseProposal}>
        <span className={styles.miniLabel}>Agent proposal</span>
        <p className={styles.raiseProposalText}>
          The strategist could not produce a usable proposal
          {proposal.errorMessage ? `: ${proposal.errorMessage}` : '.'}
        </p>
      </div>
    );
  }

  return (
    <div className={styles.raiseProposal}>
      <div className={styles.raiseProposalHead}>
        <span className={styles.miniLabel}>Agent proposed · advice</span>
        <StrategyPill strategy={proposal.strategy} />
        <span className="mx-mono">{formatMoney(proposal.estimatedCost)}</span>
        {proposal.urgency ? (
          <Pill tone={URGENCY_TONES[proposal.urgency] ?? 'slate'}>{adviceLabel(proposal.urgency)} urgency</Pill>
        ) : null}
      </div>
      {proposal.justification ? <p className={styles.raiseProposalText}>{proposal.justification}</p> : null}
    </div>
  );
}

/** The assets registered in the report's room — all three request states, each its own. */
function AssetPicker({ state, value, onChange, disabled, error }) {
  const { data, isLoading, error: loadError } = state;

  if (isLoading) return <Skeleton height={42} radius={12} />;
  if (loadError) return <ErrorState compact title="Could not load this room's equipment" message={loadError.message} />;
  if (data.items.length === 0) {
    return (
      <EmptyState
        compact
        icon={Boxes}
        title="No equipment is registered in this room"
        body="A work order needs an asset. An Admin registers it in the asset registry first."
      />
    );
  }

  const options = data.items.map((asset) => ({
    value: asset.id,
    label: `${asset.assetTag} · ${asset.name}${asset.status === 'Active' ? '' : ` (${enumLabel(asset.status)})`}`,
  }));

  return (
    <SelectMenu
      id="raise-asset"
      block
      value={value}
      onChange={onChange}
      options={options}
      placeholder="Choose the equipment…"
      disabled={disabled}
      invalid={Boolean(error)}
      describedBy={error ? 'raise-asset-error' : undefined}
    />
  );
}

export default RaiseWorkOrderPanel;

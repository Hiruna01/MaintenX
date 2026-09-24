import { describeApprovalBasis } from '../services/workOrdersApi';

/**
 * "Rs 45,000 — above the Rs 15,000 approval threshold", stated plainly.
 *
 * Reads `approvalBasis` off the order, which the API computes with the same C# function that
 * routed it. This component compares nothing: the threshold is the API's configured figure
 * and "above" is the API's answer, so the sentence cannot disagree with what the gate did.
 */
export function ApprovalBasisStatement({ estimatedCost, basis, compact = false }) {
  const { headline, detail } = describeApprovalBasis(estimatedCost, basis);
  const tone = basis.requiresApproval ? 'needs-decision' : 'clear';

  return (
    <div className={`approval-basis approval-basis--${tone} ${compact ? 'approval-basis--compact' : ''}`.trim()}>
      <p className="approval-basis__headline">{headline}</p>
      <p className="approval-basis__detail">{detail}</p>
    </div>
  );
}

export default ApprovalBasisStatement;

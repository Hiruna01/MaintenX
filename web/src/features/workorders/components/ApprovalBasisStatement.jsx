import clsx from 'clsx';
import { CircleCheck, Scale } from 'lucide-react';

import { describeApprovalBasis } from '../services/workOrdersApi';
import styles from '../workorders.module.css';

/**
 * "Rs 45,000 — above the Rs 15,000 approval threshold", stated plainly.
 *
 * Reads `approvalBasis` off the order, which the API computes with the same C# function that
 * routed it. This component compares nothing: the threshold is the API's configured figure
 * and "above" is the API's answer, so the sentence cannot disagree with what the gate did.
 */
export function ApprovalBasisStatement({ estimatedCost, basis }) {
  const { headline, detail } = describeApprovalBasis(estimatedCost, basis);
  const Icon = basis.requiresApproval ? Scale : CircleCheck;

  return (
    <div className={clsx(styles.basis, basis.requiresApproval ? styles.basisDecision : styles.basisClear)}>
      <span className={styles.basisIcon} aria-hidden="true">
        <Icon strokeWidth={1.8} />
      </span>
      <div>
        <p className={styles.basisHeadline}>{headline}</p>
        <p className={styles.basisDetail}>{detail}</p>
      </div>
    </div>
  );
}

export default ApprovalBasisStatement;

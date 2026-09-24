import useTechnicians from '../hooks/useTechnicians';

/**
 * A <select> over every Technician, fetched from GET /api/users?role=Technician.
 *
 * Only ever rendered for a FacilitiesManager — the endpoint is theirs alone — and it renders
 * its own loading and error states rather than an empty list, since "no technicians" and
 * "could not load them" are different facts.
 */
export function TechnicianSelect({ id, value, onChange, emptyLabel, disabled, invalid }) {
  const { data, isLoading, error } = useTechnicians();

  if (isLoading) {
    return (
      <select id={id} disabled>
        <option>Loading technicians…</option>
      </select>
    );
  }

  if (error) {
    return (
      <select id={id} disabled aria-invalid="true">
        <option>Technicians unavailable — {error.message}</option>
      </select>
    );
  }

  return (
    <select
      id={id}
      value={value}
      onChange={(event) => onChange(event.target.value)}
      disabled={disabled}
      aria-invalid={invalid ? 'true' : undefined}
    >
      <option value="">{data.length === 0 ? 'No technicians registered' : emptyLabel}</option>
      {data.map((technician) => (
        <option key={technician.id} value={technician.id}>
          {technician.fullName}
        </option>
      ))}
    </select>
  );
}

export default TechnicianSelect;

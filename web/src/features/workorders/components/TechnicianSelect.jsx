import SelectMenu from '../../../components/ui/SelectMenu';
import useTechnicians from '../hooks/useTechnicians';

/**
 * A picker over every Technician, fetched from GET /api/users?role=Technician.
 *
 * Only ever rendered for a FacilitiesManager — the endpoint is theirs alone — and it shows
 * its own loading and error states rather than an empty list, since "no technicians" and
 * "could not load them" are different facts.
 *
 * `emptyLabel` is the "no choice" entry ("Anyone" on a filter); `placeholder` is shown when
 * nothing is chosen and there is no such entry (a form that needs a real choice).
 */
export function TechnicianSelect({ id, value, onChange, emptyLabel, placeholder, inlineLabel, block, disabled, invalid, describedBy }) {
  const { data, isLoading, error } = useTechnicians();

  let options = [];
  let shownPlaceholder = placeholder;
  if (isLoading) shownPlaceholder = 'Loading technicians…';
  else if (error) shownPlaceholder = `Technicians unavailable — ${error.message}`;
  else {
    options = data.map((technician) => ({ value: technician.id, label: technician.fullName }));
    if (options.length === 0) shownPlaceholder = 'No technicians registered';
  }

  return (
    <SelectMenu
      id={id}
      ariaLabel={inlineLabel ? `Filter by ${inlineLabel.toLowerCase()}` : undefined}
      inlineLabel={inlineLabel}
      block={block}
      value={value}
      onChange={onChange}
      options={options}
      emptyLabel={isLoading || error ? undefined : emptyLabel}
      placeholder={shownPlaceholder}
      disabled={disabled || isLoading || Boolean(error)}
      invalid={invalid || Boolean(error)}
      describedBy={describedBy}
    />
  );
}

export default TechnicianSelect;

import clsx from 'clsx';
import { Pencil, Plus, Trash2, X } from 'lucide-react';
import { useState } from 'react';

import MxButton from '../../../components/ui/Button';
import form from '../../../components/ui/form.module.css';
import { SelectMenu } from '../../../components/ui/SelectMenu';
import { EmptyState } from '../../../components/ui/States';
import styles from '../estate.module.css';

/**
 * One list of estate records — buildings, rooms or asset categories — with an inline form to
 * add one or edit one, and (when `onDelete` is given) a two-step delete: choose, then confirm.
 *
 * Presentational: the parent supplies the rows, the fields, `validate()` and the three calls,
 * and is told when something changed so it can re-read the lists. Every refusal from the API —
 * a code already in use, a room that still has assets — is shown as the API worded it.
 *
 * @param {object[]} fields  `{ name, label, type: 'text' | 'number' | 'select', options?, mono?, hint? }`
 */
export function EditableList({
  noun,
  items,
  fields,
  emptyValues,
  toValues,
  validate,
  describe,
  onCreate,
  onUpdate,
  onDelete,
  onChanged,
  emptyIcon,
  emptyBody,
}) {
  // null — just the list; { id: null } — adding one; { id } — editing that one.
  const [editing, setEditing] = useState(null);
  const [values, setValues] = useState(emptyValues);
  const [errors, setErrors] = useState({});
  const [submitError, setSubmitError] = useState(null);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [confirmDeleteId, setConfirmDeleteId] = useState(null);
  const [deleteError, setDeleteError] = useState(null);

  function open(item) {
    setEditing({ id: item ? item.id : null });
    setValues(item ? toValues(item) : emptyValues);
    setErrors({});
    setSubmitError(null);
    setConfirmDeleteId(null);
  }

  function setField(name, value) {
    setValues((current) => ({ ...current, [name]: value }));
    setErrors((current) => ({ ...current, [name]: undefined }));
  }

  async function handleSubmit(event) {
    event.preventDefault();

    const found = validate(values);
    setErrors(found);
    if (Object.keys(found).length > 0) return;

    setIsSubmitting(true);
    setSubmitError(null);

    try {
      if (editing.id === null) {
        await onCreate(values);
        onChanged(`${noun} added.`);
      } else {
        await onUpdate(editing.id, values);
        onChanged(`${noun} saved.`);
      }
    } catch (error) {
      setSubmitError(error.message);
      setIsSubmitting(false);
    }
  }

  async function handleDelete(id) {
    setIsSubmitting(true);
    setDeleteError(null);

    try {
      await onDelete(id);
      onChanged(`${noun} deleted.`);
    } catch (error) {
      setDeleteError({ id, message: error.message });
      setIsSubmitting(false);
    }
  }

  const formId = `${noun.toLowerCase().replace(/\s+/g, '-')}-form`;

  return (
    <div className={styles.list}>
      {editing ? (
        <form className={styles.editor} onSubmit={handleSubmit} noValidate aria-labelledby={`${formId}-title`}>
          <h3 id={`${formId}-title`} className={styles.editorTitle}>
            {editing.id === null ? `New ${noun.toLowerCase()}` : `Edit ${noun.toLowerCase()}`}
          </h3>
          <div className={form.grid}>
            {fields.map((field) => {
              const id = `${formId}-${field.name}`;
              const error = errors[field.name];
              return (
                <div key={field.name} className={form.field}>
                  <label htmlFor={id} className={form.label}>
                    {field.label}
                  </label>
                  {field.type === 'select' ? (
                    <SelectMenu
                      id={id}
                      block
                      value={values[field.name]}
                      onChange={(value) => setField(field.name, value)}
                      options={field.options}
                      placeholder={`Choose a ${field.label.toLowerCase()}…`}
                      invalid={Boolean(error)}
                      describedBy={error ? `${id}-error` : undefined}
                    />
                  ) : (
                    <input
                      id={id}
                      className={clsx(form.input, field.mono && form.mono)}
                      type={field.type === 'number' ? 'number' : 'text'}
                      inputMode={field.type === 'number' ? 'numeric' : undefined}
                      value={values[field.name]}
                      onChange={(event) => setField(field.name, event.target.value)}
                      aria-invalid={error ? 'true' : 'false'}
                      aria-describedby={error ? `${id}-error` : field.hint ? `${id}-hint` : undefined}
                    />
                  )}
                  {field.hint && !error ? (
                    <p id={`${id}-hint`} className={form.hint}>
                      {field.hint}
                    </p>
                  ) : null}
                  {error ? (
                    <p id={`${id}-error`} className={form.error} role="alert">
                      {error}
                    </p>
                  ) : null}
                </div>
              );
            })}
          </div>
          {submitError ? (
            <p className={form.submitError} role="alert">
              {submitError}
            </p>
          ) : null}
          <div className={form.actions}>
            <MxButton onClick={() => setEditing(null)} disabled={isSubmitting}>
              Cancel
            </MxButton>
            <MxButton type="submit" variant="primary" disabled={isSubmitting}>
              {isSubmitting ? 'Saving…' : editing.id === null ? `Add ${noun.toLowerCase()}` : 'Save'}
            </MxButton>
          </div>
        </form>
      ) : (
        <div className={styles.listActions}>
          <MxButton icon={Plus} onClick={() => open(null)}>
            Add {noun.toLowerCase()}
          </MxButton>
        </div>
      )}

      {items.length === 0 ? (
        <EmptyState compact icon={emptyIcon} title={`No ${noun.toLowerCase()}s yet`} body={emptyBody} />
      ) : (
        <ul className={styles.rows}>
          {items.map((item) => {
            const { primary, secondary } = describe(item);
            const confirming = confirmDeleteId === item.id;
            return (
              <li key={item.id} className={styles.row}>
                <div className={styles.rowText}>
                  <span className={styles.rowPrimary}>{primary}</span>
                  {secondary ? <span className={styles.rowSecondary}>{secondary}</span> : null}
                  {deleteError?.id === item.id ? (
                    <span className={form.error} role="alert">
                      {deleteError.message}
                    </span>
                  ) : null}
                </div>
                <div className={styles.rowButtons}>
                  {confirming ? (
                    <>
                      <MxButton size="sm" icon={X} onClick={() => setConfirmDeleteId(null)} disabled={isSubmitting}>
                        Keep
                      </MxButton>
                      <MxButton
                        size="sm"
                        variant="danger"
                        icon={Trash2}
                        onClick={() => handleDelete(item.id)}
                        disabled={isSubmitting}
                      >
                        Confirm delete
                      </MxButton>
                    </>
                  ) : (
                    <>
                      <MxButton size="sm" icon={Pencil} onClick={() => open(item)} aria-label={`Edit ${primary}`}>
                        Edit
                      </MxButton>
                      {onDelete ? (
                        <MxButton
                          size="sm"
                          icon={Trash2}
                          onClick={() => {
                            setConfirmDeleteId(item.id);
                            setDeleteError(null);
                          }}
                          aria-label={`Delete ${primary}`}
                        >
                          Delete
                        </MxButton>
                      ) : null}
                    </>
                  )}
                </div>
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

export default EditableList;

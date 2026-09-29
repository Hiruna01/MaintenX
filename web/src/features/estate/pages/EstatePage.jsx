import { Boxes, Building2, DoorOpen } from 'lucide-react';
import { useState } from 'react';

import { Notice } from '../../../components/ui/Notice';
import PageHeader from '../../../components/ui/PageHeader';
import { Panel } from '../../../components/ui/Panel';
import Skeleton from '../../../components/ui/Skeleton';
import { ErrorState } from '../../../components/ui/States';
import useFetch from '../../../hooks/useFetch';
import EditableList from '../components/EditableList';
import styles from '../estate.module.css';
import {
  BUILDINGS_PATH,
  CATEGORIES_PATH,
  ROOMS_PATH,
  createBuilding,
  createCategory,
  createRoom,
  deleteBuilding,
  deleteRoom,
  updateBuilding,
  updateCategory,
  updateRoom,
} from '../services/estateApi';
import { validateBuilding, validateCategory, validateRoom } from '../services/estateValidation';

/** One panel's loading and error states around its list — each list fails on its own. */
function Section({ eyebrow, count, fetched, children }) {
  return (
    <Panel eyebrow={eyebrow} count={fetched.data ? count : null}>
      {fetched.isLoading ? (
        <div role="status" aria-label={`Loading ${eyebrow.toLowerCase()}`} className={styles.skeleton}>
          <Skeleton height={44} radius={12} />
          <Skeleton height={44} radius={12} />
          <Skeleton height={44} radius={12} />
        </div>
      ) : fetched.error ? (
        <ErrorState compact title={`Could not load ${eyebrow.toLowerCase()}`} message={fetched.error.message} />
      ) : (
        children
      )}
    </Panel>
  );
}

/** The three lists. Re-mounted with a new `key` after every write, so each re-reads the API. */
function EstateLists({ onChanged }) {
  const buildings = useFetch(BUILDINGS_PATH);
  const rooms = useFetch(ROOMS_PATH);
  const categories = useFetch(CATEGORIES_PATH);

  const buildingList = buildings.data ?? [];
  const buildingCode = new Map(buildingList.map((b) => [b.id, b.code]));
  const buildingOptions = buildingList.map((b) => ({ value: String(b.id), label: `${b.code} · ${b.name}` }));

  return (
    <div className={styles.grid}>
      <Section eyebrow="Buildings" count={buildingList.length} fetched={buildings}>
        <EditableList
          noun="Building"
          items={buildingList}
          emptyIcon={Building2}
          emptyBody="Add the campus buildings first; every room belongs to one."
          fields={[
            { name: 'name', label: 'Name', type: 'text' },
            { name: 'code', label: 'Code', type: 'text', mono: true, hint: 'Unique across the campus.' },
          ]}
          emptyValues={{ name: '', code: '' }}
          toValues={(b) => ({ name: b.name, code: b.code })}
          validate={validateBuilding}
          describe={(b) => ({ primary: `${b.code} · ${b.name}`, secondary: null })}
          onCreate={createBuilding}
          onUpdate={updateBuilding}
          onDelete={deleteBuilding}
          onChanged={onChanged}
        />
      </Section>

      <Section eyebrow="Rooms" count={(rooms.data ?? []).length} fetched={rooms}>
        <EditableList
          noun="Room"
          items={rooms.data ?? []}
          emptyIcon={DoorOpen}
          emptyBody="Rooms are where reports are filed and assets stand."
          fields={[
            { name: 'buildingId', label: 'Building', type: 'select', options: buildingOptions },
            { name: 'name', label: 'Name', type: 'text' },
            {
              name: 'code',
              label: 'Code',
              type: 'text',
              mono: true,
              hint: "The timetable finds a room by this code in a class's location.",
            },
            { name: 'floor', label: 'Floor', type: 'number' },
          ]}
          emptyValues={{ buildingId: '', name: '', code: '', floor: '0' }}
          toValues={(r) => ({ buildingId: String(r.buildingId), name: r.name, code: r.code, floor: String(r.floor) })}
          validate={validateRoom}
          describe={(r) => ({
            primary: `${r.code} · ${r.name}`,
            secondary: `${buildingCode.get(r.buildingId) ?? `Building #${r.buildingId}`} · floor ${r.floor}`,
          })}
          onCreate={createRoom}
          onUpdate={updateRoom}
          onDelete={deleteRoom}
          onChanged={onChanged}
        />
      </Section>

      <Section eyebrow="Asset categories" count={(categories.data ?? []).length} fetched={categories}>
        <EditableList
          noun="Category"
          items={categories.data ?? []}
          emptyIcon={Boxes}
          emptyBody="Every asset is filed under a category."
          fields={[
            { name: 'name', label: 'Name', type: 'text' },
            {
              name: 'defaultWarrantyMonths',
              label: 'Default warranty (months)',
              type: 'number',
              hint: 'A default for new assets only — no existing asset changes.',
            },
          ]}
          emptyValues={{ name: '', defaultWarrantyMonths: '12' }}
          toValues={(c) => ({ name: c.name, defaultWarrantyMonths: String(c.defaultWarrantyMonths) })}
          validate={validateCategory}
          describe={(c) => ({
            primary: c.name,
            secondary: `${c.defaultWarrantyMonths} ${c.defaultWarrantyMonths === 1 ? 'month' : 'months'} default warranty`,
          })}
          onCreate={createCategory}
          onUpdate={updateCategory}
          // Categories have no delete on the API: an asset filed under one would block it at
          // the database, and an empty one is not worth an endpoint.
          onDelete={null}
          onChanged={onChanged}
        />
      </Section>
    </div>
  );
}

/**
 * The estate the registry and the reports sit on: buildings, rooms and asset categories.
 * Admin only — the same split as registering assets, and exactly what the API's Admin policy
 * on those writes allows. A delete the API refuses (a building with rooms, a room with assets
 * or history) is shown on its row as the API worded it.
 */
export function EstatePage() {
  const [version, setVersion] = useState(0);
  const [notice, setNotice] = useState(null);

  function handleChanged(message) {
    setNotice(message);
    setVersion((current) => current + 1);
  }

  return (
    <section className={styles.page}>
      <PageHeader
        crumbs={[{ label: 'Estate' }]}
        title="Estate"
        lead="The buildings, rooms and asset categories everything else is filed against."
      />
      <Notice key={`notice-${version}`}>{notice}</Notice>
      <EstateLists key={version} onChanged={handleChanged} />
    </section>
  );
}

export default EstatePage;

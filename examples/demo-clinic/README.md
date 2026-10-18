# Demo clinic

Fictional demo content for the game's milestone M7, generated from the game's fixture values. It is not medical knowledge: cases, readings, units and routing are invented for play and labelled as such in every provenance record. See [ADR 0018](../../docs/architecture/0018-demo-clinic.md).

Do not edit the generated files. Change `source/game-fixtures.json` or the generator, then run:

```sh
python scripts/generate-demo-clinic.py
```

| File | Content |
| --- | --- |
| `source/game-fixtures.json` | Input as supplied by the game (read only). |
| `content.json` | Content package `demo_clinic:content` (requires library 0.14.0). |
| `en.json`, `de.json` | Localization catalogs for namespace `demo_clinic`. |

## Mapping

| Game fixture | SDK |
| --- | --- |
| `demo:<key>` | `demo_clinic:<key>`, hyphens become underscores; the game key stays in `provenance.externalId`. |
| Room type `MinimumCells`, `RequiresDoor` | `room` with `minimumCells`, `requiresDoor`. |
| Department `Capacity` | `medicalDepartment` plus gameplay `capacity`. `ServiceOnly` stays in the game. |
| Staff role `Physician`, `Nurse`, `Technician` | `staffRole` `role_physician`, `role_nurse`, `role_technician`. |
| Examination | `examination` with `examinationObservation`, `examinationRoom`, `examinationStaffRole`, `examinationEquipment`, `examinationQualification`; `durationMinutes × 60` as `examinationDurationSeconds`. |
| Observation `demo:<key>` | `observation` `observation_<key>`: numeric, categorical (`clear`, `marked`, `cloudy`) or text (complaint). |
| Case | `disease` with simulation-only readings in `diagnosticSimulation.observationDistributions` (`reading_<case>_<observation>`). |
| Preferred and allowed departments | `diseaseDepartment` with `priority` 1 for the preferred department and 2, 3 … for the others in fixture order. |
| Furniture | `furniture` with footprint and a `modelGlb` catalog asset. |
| Furniture capabilities | `furnitureEquipment` to `medicalEquipment` `equipment_analyzer` or `equipment_scanner`. |
| Transport `Seated`, `Lying` | `mobility: pushable`, `transport` for one patient, placeholder `seat`/`lying` and `handle` points marked `placeholder: true`. |
| Room furniture requirement | `roomFurniture` with `requirement`. |
| Patient names | Name pools `demo_clinic:patients.given` and `demo_clinic:patients.family` (country `DE`, gender unknown); no name format, so installed formats are not contested. |
| Patient mobility | Not content; stays in the game. |

Units: `demo:C` → `Cel`, `demo:beats-per-minute` → `/min`, `demo:fictional-pressure` → `{fictional_pressure}`, `demo:fictional-count` → `{fictional_count}`, `demo:fictional-marker` → `{fictional_marker}`.

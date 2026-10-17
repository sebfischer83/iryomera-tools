"""Generate versioned name importer staging contracts; strict, independent of the additive SDK content format."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
out = root / 'schemas/names/v1'
out.mkdir(parents=True, exist_ok=True)
sdk = json.loads((root / 'schemas/v1/contracts.schema.json').read_text(encoding='utf-8'))['$defs']
text = {'type': 'string', 'minLength': 1, 'pattern': r'\S'}
id_schema = sdk['contentRelation']['properties']['id']
namespace_schema = sdk['contentPackageManifest']['properties']['namespace']
version_schema = sdk['contentRelation']['properties']['version']
name_text = sdk['nameEntry']['properties']['localized']
country = sdk['namePool']['properties']['country']
region = sdk['namePool']['properties']['region']
date = {'type': 'string', 'format': 'date-time'}
digest = {'type': 'string', 'pattern': r'^[0-9a-f]{64}$(?![\s\S])'}
batch_id = {'type': 'string', 'pattern': r'^batch_[0-9a-f]{64}$(?![\s\S])'}
year = {'anyOf': [{'type': 'integer', 'minimum': 1000, 'maximum': 9999}, {'type': 'null'}]}
def ref(name): return {'$ref': '#/$defs/' + name}
def array(item): return {'type': 'array', 'items': item}
def nullable(item): return {'anyOf': [item, {'type': 'null'}]}
def obj(properties, required):
    return {'type': 'object', 'properties': properties, 'required': required, 'additionalProperties': False}

defs = {}
defs['nameImportOptions'] = obj({
    'source': {'enum': ['us-ssa', 'names-sigpwned']}, 'sourceVersion': text, 'sourceUrl': {'type': 'string', 'format': 'uri', 'pattern': r'^https?://'},
    'license': text, 'licenseText': text, 'attribution': text, 'importedAt': date, 'namespace': namespace_schema,
    'birthYear': year, 'maxInputBytes': {'type': 'integer', 'minimum': 1}
}, ['source', 'sourceVersion', 'sourceUrl', 'license', 'licenseText', 'attribution', 'importedAt'])
defs['nameRecord'] = obj({
    'id': id_schema, 'country': country, 'region': region, 'part': {'enum': ['given', 'family']}, 'localized': name_text,
    'romanized': nullable(name_text), 'romanizationOrigin': nullable(text), 'gender': nullable({'enum': ['female', 'male', 'unisex', 'unknown']}),
    'rank': nullable({'type': 'integer', 'minimum': 1}), 'count': nullable({'type': 'integer', 'minimum': 0}),
    'proportion': nullable({'type': 'number', 'minimum': 0, 'maximum': 1}), 'birthYearFrom': year, 'birthYearTo': year,
    'externalId': nullable(text), 'sourceFields': {'type': 'object', 'additionalProperties': True}
}, ['id', 'country', 'part', 'localized'])
defs['nameDiagnostic'] = obj({'severity': {'enum': ['information', 'warning', 'error']}, 'code': text, 'recordId': {'type': 'string'}, 'message': text}, ['severity', 'code', 'recordId', 'message'])
defs['nameBatch'] = obj({
    'id': batch_id, 'inputSha256': digest, 'inputBytes': {'type': 'integer', 'minimum': 1}, 'options': ref('nameImportOptions'),
    'adapterVersion': version_schema, 'detectedFormat': {'enum': ['csv']}, 'records': array(ref('nameRecord')), 'diagnostics': array(ref('nameDiagnostic'))
}, ['id', 'inputSha256', 'inputBytes', 'options', 'adapterVersion', 'detectedFormat'])
defs['nameReviewDecision'] = obj({
    'batchId': batch_id, 'recordId': nullable(id_schema), 'status': {'enum': ['unreviewed', 'inReview', 'approved', 'rejected']},
    'reviewer': text, 'at': date, 'note': text
}, ['batchId', 'status', 'reviewer', 'at', 'note'])
defs['nameRightsDecision'] = obj({
    'batchId': batch_id, 'decision': {'enum': ['undecided', 'permitted', 'blocked']}, 'permissionEvidence': nullable(text),
    'reviewer': text, 'at': date, 'note': text
}, ['batchId', 'decision', 'reviewer', 'at', 'note'])
defs['nameRightsDecision']['allOf'] = [{'if': {'properties': {'decision': {'const': 'permitted'}}, 'required': ['decision']}, 'then': {'required': ['permissionEvidence'], 'properties': {'permissionEvidence': text}}}]
defs['nameWorkspace'] = obj({
    'schemaVersion': {'const': '1.0.0'}, 'batches': array(ref('nameBatch')), 'reviews': array(ref('nameReviewDecision')), 'rights': array(ref('nameRightsDecision'))
}, ['schemaVersion'])
defs['nameReleaseBatch'] = obj({'id': batch_id, 'source': text, 'sourceVersion': text, 'license': text, 'inputSha256': digest, 'exportedRecords': {'type': 'integer', 'minimum': 0}},
                               ['id', 'source', 'sourceVersion', 'license', 'inputSha256', 'exportedRecords'])
defs['nameReleasePool'] = obj({'id': id_schema, 'country': country, 'region': region, 'part': {'enum': ['given', 'family']}, 'entries': {'type': 'integer', 'minimum': 1}},
                              ['id', 'country', 'part', 'entries'])
defs['nameReleaseReport'] = obj({'schemaVersion': {'const': '1.0.0'}, 'packageId': id_schema, 'version': version_schema,
                                 'batches': array(ref('nameReleaseBatch')), 'pools': array(ref('nameReleasePool'))}, ['schemaVersion', 'packageId', 'version'])

def dump(path, value): path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8', newline='\n')
dump(out / 'contracts.schema.json', {'$schema': 'https://json-schema.org/draft/2020-12/schema', '$id': 'https://schemas.iryomera.org/names/v1/contracts.schema.json', 'title': 'Iryomera name importer staging contracts v1', '$defs': defs})
for name in defs:
    dump(out / (name + '.schema.json'), {'$schema': 'https://json-schema.org/draft/2020-12/schema', '$id': 'https://schemas.iryomera.org/names/v1/' + name + '.schema.json', '$ref': 'contracts.schema.json#/$defs/' + name})

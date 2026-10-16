"""Generate versioned importer contracts; no runtime network or third-party Python dependencies."""
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
out = root / 'schemas/importer/v1'
out.mkdir(parents=True, exist_ok=True)
sdk = json.loads((root / 'schemas/v1/contracts.schema.json').read_text())['$defs']
text = {'type': 'string', 'minLength': 1, 'pattern': r'\S'}
id_schema = sdk['contentRelation']['properties']['id']
namespace_schema = sdk['contentPackageManifest']['properties']['namespace']
date = {'type': 'string', 'format': 'date-time'}
digest = {'type': 'string', 'pattern': r'^[0-9a-f]{64}$(?![\s\S])'}
raw_fields = {'type': 'object', 'additionalProperties': True}
def ref(name): return {'$ref': '#/$defs/' + name}
def array(item): return {'type': 'array', 'items': item}
def nullable(item): return {'anyOf': [item, {'type': 'null'}]}
def obj(properties, required):
    return {'type': 'object', 'properties': properties, 'required': required, 'additionalProperties': False}

defs = {'provenance': sdk['provenance'], 'evidenceReference': sdk['evidenceReference']}
defs['matchCandidate'] = obj({'leftId':id_schema,'rightId':id_schema,'language':text,'normalizedText':text}, ['leftId','rightId','language','normalizedText'])
defs['importDiagnostic'] = obj({'severity': {'enum': ['information','warning','error']}, 'code': text, 'recordId': {'type':'string'}, 'message': text}, ['severity','code','recordId','message'])
defs['importOptions'] = obj({
    'source': {'enum':['hpo','hpo-annotations','maxo','maxo-annotations','icd-10-gm','loinc','ops']},
    'sourceVersion':text, 'sourceUrl':{'type':'string','format':'uri','pattern':r'^https?://'},
    'license':text,'licenseText':text,'attribution':text,'importedAt':date,'namespace':namespace_schema,
    'format':{'enum':['auto','json','obo','tsv','csv','xml']},'maxInputBytes':{'type':'integer','minimum':1}
}, ['source','sourceVersion','sourceUrl','license','licenseText','attribution','importedAt'])
defs['importedTerm'] = obj({
    'id':id_schema,'externalId':text,'kind':{'enum':['disease','finding','examination','medicalProcedure']},
    'label':text,'language':text,'description':nullable({'type':'string'}),'synonyms':array({'type':'string'}),
    'alternateIds':array(text),'obsolete':{'type':'boolean'},'provenance':ref('provenance'),'sourceFields':raw_fields
}, ['id','externalId','kind','label','language','provenance'])
defs['sourceAssertion'] = obj({
    'id':id_schema,'kind':{'enum':['hpoAssociation','maxoAnnotation','ontologyHierarchy','crossReference']},
    'predicate':text,'subjectExternalId':text,'objectExternalId':text,'diseaseExternalId':nullable(text),
    'extensionExternalId':nullable(text),'provenance':ref('provenance'),'sourceFields':raw_fields
}, ['id','kind','predicate','subjectExternalId','objectExternalId','provenance'])
defs['importBatch'] = obj({
    'id':text,'inputSha256':digest,'inputBytes':{'type':'integer','minimum':1},'options':ref('importOptions'),
    'adapterVersion':{'const':'1.0.0'},'detectedFormat':{'enum':['json','obo','tsv','csv','xml']},
    'observedSourceVersion':nullable({'type':'string'}),'sourceMetadata':raw_fields,
    'terms':array(ref('importedTerm')),'assertions':array(ref('sourceAssertion')),'diagnostics':array(ref('importDiagnostic'))
}, ['id','inputSha256','inputBytes','options','detectedFormat'])
defs['reviewDecision'] = obj({'batchId':text,'recordId':id_schema,'status':{'enum':['unreviewed','inReview','approved','rejected']},'reviewer':text,'at':date,'note':text}, ['batchId','recordId','status','reviewer','at','note'])
defs['rightsDecision'] = obj({'batchId':text,'decision':{'enum':['undecided','permitted','blocked']},'reviewer':text,'at':date,'note':text}, ['batchId','decision','reviewer','at','note'])
defs['importWorkspace'] = obj({'schemaVersion':{'const':'1.0.0'},'batches':array(ref('importBatch')),'reviews':array(ref('reviewDecision')),'rights':array(ref('rightsDecision'))}, ['schemaVersion'])
defs['resolvedSourceAssertion'] = obj({'source':ref('sourceAssertion'),'subjectId':id_schema,'objectId':id_schema,'diseaseId':nullable(id_schema)}, ['source','subjectId','objectId'])
defs['sourceAssertionDocument'] = obj({'schemaVersion':{'const':'1.0.0'},'assertions':array(ref('resolvedSourceAssertion'))}, ['schemaVersion'])
defs['releaseImportSummary'] = obj({'batchId':text,'inputSha256':digest,'inputBytes':{'type':'integer','minimum':1},'adapterVersion':{'const':'1.0.0'},'options':ref('importOptions'),'observedSourceVersion':nullable({'type':'string'})}, ['batchId','inputSha256','inputBytes','adapterVersion','options'])
defs['releaseReport'] = obj({
    'schemaVersion':{'const':'1.0.0'},'selectedDefinitionIds':array(id_schema),'selectedAssertionIds':array(id_schema),
    'omittedUnapprovedRecords':{'type':'integer','minimum':0},'imports':array(ref('releaseImportSummary')),
    'reviews':array(ref('reviewDecision')),'rights':array(ref('rightsDecision')),
    'diagnosticCounts':{'type':'object','additionalProperties':{'type':'integer','minimum':0}}
}, ['schemaVersion'])

def write(path, value): path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n')
base = 'https://schemas.iryomera.org/importer/v1/'
write(out/'contracts.schema.json', {'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+'contracts.schema.json','$defs':defs})
for name in defs:
    write(out/(name+'.schema.json'), {'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+name+'.schema.json','$ref':'contracts.schema.json#/$defs/'+name})

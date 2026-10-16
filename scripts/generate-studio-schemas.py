"""Public Studio review report; SQLite authoring tables are private implementation details."""
from pathlib import Path
import json
root=Path(__file__).resolve().parents[1]
sdk=json.loads((root/'schemas/v1/contracts.schema.json').read_text())['$defs']
out=root/'schemas/studio/v1';out.mkdir(parents=True,exist_ok=True)
def ref(n):return {'$ref':'#/$defs/'+n}
def obj(p,r):return {'type':'object','properties':p,'required':r,'additionalProperties':False}
text={'type':'string','minLength':1,'pattern':r'\S'}
id=sdk['contentRelation']['properties']['id'];date={'type':'string','format':'date-time'}
defs={'provenance':sdk['provenance'],'evidenceReference':sdk['evidenceReference']}
defs['studioReviewedRecord']=obj({'id':id,'provenance':ref('provenance')},['id','provenance'])
defs['studioPermissionNotice']=obj({'recordId':id,'digest':{'type':'string','pattern':'^[0-9a-f]{64}$(?![\\s\\S])'},'permitted':{'type':'boolean'},'reviewer':text,'evidence':text,'atUtc':date},['recordId','digest','permitted','reviewer','evidence','atUtc'])
defs['studioReleaseReport']=obj({'schemaVersion':{'const':'1.0.0'},'projectId':{'type':'string','format':'uuid'},'revision':{'type':'integer','minimum':1},'rightsReviewer':text,'rightsNote':text,'approvedRecords':{'type':'array','items':ref('studioReviewedRecord')},'permissions':{'type':'array','items':ref('studioPermissionNotice')}},['schemaVersion','projectId','revision','rightsReviewer','rightsNote'])
base='https://schemas.iryomera.org/studio/v1/'
(out/'contracts.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+'contracts.schema.json','$defs':defs},indent=2)+'\n')
for name in defs:
 (out/(name+'.schema.json')).write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+name+'.schema.json','$ref':'contracts.schema.json#/$defs/'+name},indent=2)+'\n')

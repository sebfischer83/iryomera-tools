"""Generate the separate offline ModKit v1 authoring/integrity schema bundle."""
import json
from pathlib import Path
root=Path(__file__).resolve().parents[1]
sdk=json.loads((root/'schemas/v1/contracts.schema.json').read_text())['$defs']
def ref(n): return {'$ref':'#/$defs/'+n}
def obj(p,r): return {'type':'object','properties':p,'required':r,'additionalProperties':True}
text={'type':'string','minLength':1,'pattern':r'\S'}
path={'type':'string','minLength':1,'maxLength':200}
defs={k:sdk[k] for k in ['modManifest','contentDependency']}
defs['modFile']=obj({'path':path,'kind':{'enum':['content','localization','modelGlb','texture','icon','notice']},'license':text,'attribution':{'type':['string','null']}},['path','kind','license'])
defs['modProject']=obj({'schemaVersion':{'const':'1.0.0'},'manifest':ref('modManifest'),'files':{'type':'array','minItems':1,'items':ref('modFile')}},['schemaVersion','manifest','files'])
defs['modFileDigest']=obj({'path':path,'sizeBytes':{'type':'integer','minimum':0},'sha256':{'type':'string','pattern':r'^[0-9a-f]{64}$(?![\s\S])'}},['path','sizeBytes','sha256'])
defs['modArchiveIndex']=obj({'schemaVersion':{'const':'1.0.0'},'files':{'type':'array','minItems':1,'items':ref('modFileDigest')}},['schemaVersion','files'])
out=root/'schemas/modkit/v1';out.mkdir(parents=True,exist_ok=True)
base='https://schemas.iryomera.org/modkit/v1/'
(out/'contracts.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+'contracts.schema.json','$defs':defs},indent=2)+'\n',encoding='utf-8',newline='\n')
for name in ['modProject','modFile','modFileDigest','modArchiveIndex']:
 (out/(name+'.schema.json')).write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+name+'.schema.json','$ref':'contracts.schema.json#/$defs/'+name},indent=2)+'\n',encoding='utf-8',newline='\n')

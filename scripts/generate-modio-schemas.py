"""Generate independent v1 host installation receipts; no credentials or signed URLs."""
import json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
sdk = json.loads((root/'schemas/v1/contracts.schema.json').read_text())['$defs']['modManifest']['properties']
base = 'https://schemas.iryomera.org/modio/v1/'
origin = {'type':'object','properties':{'provider':{'enum':['local','modio']}, **{k:{'type':'integer','minimum':1} for k in ['gameId','modId','fileId']}}, 'required':['provider'], 'additionalProperties':True,
 'allOf':[{'if':{'properties':{'provider':{'const':'modio'}}},'then':{'required':['gameId','modId','fileId']},'else':{'not':{'anyOf':[{'required':[k]} for k in ['gameId','modId','fileId']]}}}]}
receipt = {'type':'object','properties':{'schemaVersion':{'const':'1.0.0'},'packageId':sdk['id'],'namespace':sdk['namespace'],'version':sdk['version'],'archiveSha256':{'type':'string','pattern':r'^[0-9a-f]{64}$(?![\s\S])'},'origin':{'$ref':'#/$defs/modOrigin'}},'required':['schemaVersion','packageId','namespace','version','archiveSha256','origin'],'additionalProperties':True}
out=root/'schemas/modio/v1';out.mkdir(parents=True,exist_ok=True)
(out/'contracts.schema.json').write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+'contracts.schema.json','$defs':{'modOrigin':origin,'modInstallReceipt':receipt}},indent=2)+'\n',encoding='utf-8',newline='\n')
for name in ['modOrigin','modInstallReceipt']:
 (out/(name+'.schema.json')).write_text(json.dumps({'$schema':'https://json-schema.org/draft/2020-12/schema','$id':base+name+'.schema.json','$ref':'contracts.schema.json#/$defs/'+name},indent=2)+'\n',encoding='utf-8',newline='\n')

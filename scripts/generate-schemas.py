from pathlib import Path
import json
root=Path(__file__).resolve().parents[1]
s=root/'schemas/v1';s.mkdir(parents=True,exist_ok=True)
idpattern=r'^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*:[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$'
nspattern=r'^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)*$'
keypattern=r'^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$'
versionpattern=r'^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$'
text={'type':'string','minLength':1,'pattern':r'\S'}
id={'type':'string','pattern':idpattern}
ns={'type':'string','pattern':nspattern}
key={'type':'string','pattern':keypattern}
version={'type':'string','pattern':versionpattern}
def ref(name):return {'$ref':'#/$defs/'+name}
def array(item):return {'type':'array','items':item}
def obj(props,required=()):return {'type':'object','properties':props,'required':list(required),'additionalProperties':True}
def nullable(schema):return {'anyOf':[schema,{'type':'null'}]}
def dump(path,value):
    def anchor(item):
        if isinstance(item,dict):
            return {k:(v[:-1]+r'$(?![\s\S])' if k=='pattern' and isinstance(v,str) and v.endswith('$') else anchor(v)) for k,v in item.items()}
        if isinstance(item,list):return [anchor(x) for x in item]
        return item
    path.write_text(json.dumps(anchor(value),indent=2,ensure_ascii=False)+'\n')
kinds=['disease','symptom','finding','examination','treatment','medicalProcedure','medicalDepartment','medicalEquipment','staffQualification','room','furniture','staffRole']
rels=['diseaseSymptom','diseaseFinding','examinationFinding','diseaseExamination','diseaseTreatment','examinationEquipment','treatmentQualification','roomFurniture']
defs={}
defs['evidenceReference']=obj({'citation':text,'uri':nullable({'type':'string','format':'uri'}),'evidenceCode':nullable(text)},['citation'])
defs['provenance']=obj({'source':text,'externalId':text,'sourceVersion':text,'license':text,'importedAt':{'type':'string','format':'date-time'},'reviewStatus':{'enum':['unreviewed','inReview','approved','rejected']},'reviewedBy':nullable(text),'reviewedAt':nullable({'type':'string','format':'date-time'}),'evidence':array(ref('evidenceReference'))},['source','externalId','sourceVersion','license','importedAt'])
defs['provenance']['allOf']=[{'if':{'properties':{'reviewStatus':{'enum':['approved','rejected']}},'required':['reviewStatus']},'then':{'required':['reviewedBy','reviewedAt'],'properties':{'reviewedBy':text,'reviewedAt':{'type':'string','format':'date-time'}}}}]
defs['externalIdentifier']=obj({'system':text,'value':text,'provenance':ref('provenance')},['system','value','provenance'])
base={'id':id,'nameKey':key,'descriptionKey':nullable(key),'version':version,'provenance':ref('provenance'),'classifications':array(ref('externalIdentifier')),'synonymKeys':array(key)}
for kind in kinds:defs[kind+'Definition']=obj({'kind':{'const':kind},**base},['kind','id','nameKey','version','provenance'])
defs['contentDefinition']={'oneOf':[ref(k+'Definition') for k in kinds]}
defs['contentRelation']=obj({'id':id,'version':version,'kind':{'enum':rels},'fromId':id,'toId':id,'provenance':ref('provenance')},['id','version','kind','fromId','toId','provenance'])
defs['contentDependency']=obj({'packageId':id,'versionRange':text},['packageId','versionRange'])
manifest={'schemaVersion':{'const':'1.0.0'},'id':id,'namespace':ns,'version':version,'license':text,'dependencies':array(ref('contentDependency'))}
defs['contentPackageManifest']=obj(manifest,['schemaVersion','id','namespace','version','license'])
defs['modManifest']=obj({**manifest,'nameKey':key,'author':text,'compatibleGameVersions':text,'contentFiles':array({'type':'string','minLength':1,'pattern':r'^(?!/)(?!.*\\)(?!.*:)(?!.*(?:^|/)\.\.?(/|$)).+$'}),'supportedLanguages':array({'type':'string','pattern':r'^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$'})},['schemaVersion','id','namespace','nameKey','author','version','compatibleGameVersions','license'])
bal={'definitionId':id}
for name in ['rarity','severity','diagnosticDifficulty','simulatedSuccessProbability']:bal[name]=nullable({'type':'number','minimum':0,'maximum':1})
for name in ['treatmentDurationSeconds','examinationCost','treatmentCost']:bal[name]=nullable({'type':'number','minimum':0})
bal['requiredResources']={'type':'object','propertyNames':text,'additionalProperties':{'type':'number','minimum':0}}
defs['gameplayBalance']=obj(bal,['definitionId'])
defs['contentPackage']=obj({'schemaVersion':{'const':'1.0.0'},'manifest':ref('contentPackageManifest'),'definitions':array(ref('contentDefinition')),'relations':array(ref('contentRelation')),'gameplay':array(ref('gameplayBalance'))},['schemaVersion','manifest'])
defs['localizationDefinition']=obj({'schemaVersion':{'const':'1.0.0'},'namespace':ns,'language':{'type':'string','pattern':r'^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$'},'entries':{'type':'object','propertyNames':{'pattern':keypattern},'additionalProperties':text}},['schemaVersion','namespace','language'])
# A single bundled file supports every public exchange contract; small entry files work offline.
dump(s/'contracts.schema.json',{'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'https://schemas.iryomera.org/v1/contracts.schema.json','title':'Iryomera v1 exchange contracts','$defs':defs})
for name in defs:
    dump(s/(name+'.schema.json'),{'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'https://schemas.iryomera.org/v1/'+name+'.schema.json','$ref':'contracts.schema.json#/$defs/'+name})

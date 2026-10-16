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
kinds=['disease','symptom','finding','examination','observation','treatment','medicalProcedure','medicalDepartment','medicalEquipment','staffQualification','room','furniture','staffRole']
rels=['diseaseSymptom','diseaseFinding','examinationFinding','diseaseExamination','diseaseTreatment','examinationEquipment','treatmentQualification','roomFurniture','examinationObservation','examinationRoom','examinationQualification','examinationPrerequisite']
defs={}
defs['evidenceReference']=obj({'citation':text,'uri':nullable({'type':'string','format':'uri'}),'evidenceCode':nullable(text)},['citation'])
defs['provenance']=obj({'source':text,'externalId':text,'sourceVersion':text,'license':text,'importedAt':{'type':'string','format':'date-time'},'reviewStatus':{'enum':['unreviewed','inReview','approved','rejected']},'reviewedBy':nullable(text),'reviewedAt':nullable({'type':'string','format':'date-time'}),'evidence':array(ref('evidenceReference'))},['source','externalId','sourceVersion','license','importedAt'])
defs['provenance']['allOf']=[{'if':{'properties':{'reviewStatus':{'enum':['approved','rejected']}},'required':['reviewStatus']},'then':{'required':['reviewedBy','reviewedAt'],'properties':{'reviewedBy':text,'reviewedAt':{'type':'string','format':'date-time'}}}}]
defs['externalIdentifier']=obj({'system':text,'value':text,'provenance':ref('provenance')},['system','value','provenance'])
defs['assetReference']=obj({'path':text,'kind':{'enum':['modelGlb','texture','icon']},'license':text,'attribution':nullable(text)},['path','kind','license'])
defs['footprintCell']=obj({'x':{'type':'integer','minimum':0},'z':{'type':'integer','minimum':0}},['x','z'])
defs['furnitureFootprint']=obj({'width':{'type':'integer','minimum':1},'depth':{'type':'integer','minimum':1},'cellSizeMeters':{'type':'number','exclusiveMinimum':0},'occupiedCells':array(ref('footprintCell'))},['width','depth'])
defs['interactionPoint']=obj({'key':ns,'x':{'type':'number'},'y':{'type':'number'},'z':{'type':'number'},'headingDegrees':nullable({'type':'number','minimum':0,'exclusiveMaximum':360}),'requiredQualificationId':nullable(id)},['key','x','y','z'])
defs['furniturePlacement']=obj({'footprint':ref('furnitureFootprint'),'interactionPoints':array(ref('interactionPoint'))},['footprint'])
base={'id':id,'nameKey':key,'descriptionKey':nullable(key),'version':version,'provenance':ref('provenance'),'classifications':array(ref('externalIdentifier')),'synonymKeys':array(key),'assets':nullable(array(ref('assetReference')))}
for kind in kinds:defs[kind+'Definition']=obj({'kind':{'const':kind},**base},['kind','id','nameKey','version','provenance'])
defs['furnitureDefinition']['properties']['placement']=nullable(ref('furniturePlacement'))
defs['observationCategory']=obj({'key':ns,'nameKey':key,'findingId':nullable(id)},['key','nameKey'])
defs['observationResultDefinition']=obj({'valueKind':{'enum':['numeric','categorical','boolean','text']},'unitCode':nullable(text),'categories':array(ref('observationCategory')),'patientContextKeys':array(key)},['valueKind'])
defs['observationDefinition']['properties']['result']=nullable(ref('observationResultDefinition'))
defs['patientContextCriterion']=obj({'factorKey':key,'minimum':nullable({'type':'number'}),'maximum':nullable({'type':'number'}),'unitCode':nullable(text),'category':nullable(text),'booleanValue':{'type':['boolean','null']}},['factorKey'])
defs['patientContextCriterion']['oneOf']=[
    {'anyOf':[{'required':['minimum']},{'required':['maximum']}],'properties':{'minimum':{'type':'number'},'maximum':{'type':'number'},'category':{'type':'null'},'booleanValue':{'type':'null'}}},
    {'required':['category'],'properties':{'category':text,'minimum':{'type':'null'},'maximum':{'type':'null'},'booleanValue':{'type':'null'},'unitCode':{'type':'null'}}},
    {'required':['booleanValue'],'properties':{'booleanValue':{'type':'boolean'},'minimum':{'type':'null'},'maximum':{'type':'null'},'category':{'type':'null'},'unitCode':{'type':'null'}}}
]
versioned={'schemaVersion':{'const':'1.0.0'},'id':id,'version':version}
defs['observationReferenceRange']=obj({**versioned,'observationId':id,'unitCode':text,'minimum':nullable({'type':'number'}),'maximum':nullable({'type':'number'}),'appliesWhen':array(ref('patientContextCriterion')),'provenance':ref('provenance')},['schemaVersion','id','version','observationId','unitCode','provenance'])
defs['diagnosticRecommendation']=obj({**versioned,'diseaseId':id,'examinationId':id,'contextDefinitionIds':array(id),'appliesWhen':array(ref('patientContextCriterion')),'rationaleKey':nullable(key),'provenance':ref('provenance')},['schemaVersion','id','version','diseaseId','examinationId','provenance'])
for sourced in ['observationReferenceRange','diagnosticRecommendation']:
    defs[sourced]['allOf']=[{'properties':{'provenance':{'required':['evidence'],'properties':{'evidence':{'type':'array','minItems':1,'items':ref('evidenceReference')}}}}}]
defs['simulatedObservationDistribution']=obj({**versioned,'simulationOnly':{'const':True},'diseaseId':id,'observationId':id,'numericMinimum':nullable({'type':'number'}),'numericMaximum':nullable({'type':'number'}),'categoryWeights':{'type':'object','propertyNames':ns,'additionalProperties':{'type':'number','minimum':0}},'trueProbability':nullable({'type':'number','minimum':0,'maximum':1}),'textKey':nullable(key),'appliesWhen':array(ref('patientContextCriterion'))},['schemaVersion','simulationOnly','id','version','diseaseId','observationId'])
defs['simulatedManifestation']=obj({**versioned,'simulationOnly':{'const':True},'diseaseId':id,'manifestationId':id,'probability':{'type':'number','minimum':0,'maximum':1},'visibility':{'enum':['initiallyVisible','afterExamination','hidden']}},['schemaVersion','simulationOnly','id','version','diseaseId','manifestationId','probability'])
defs['examinationResultPolicy']=obj({**versioned,'simulationOnly':{'const':True},'examinationId':id,'visibility':{'enum':['initiallyVisible','afterExamination','hidden']},'repeatPolicy':{'enum':['untilPatientStateChanges','afterInterval']},'repeatIntervalSeconds':nullable({'type':'number','exclusiveMinimum':0})},['schemaVersion','simulationOnly','id','version','examinationId'])
defs['diagnosticSimulationSettings']=obj({'simulationOnly':{'const':True},'observationDistributions':array(ref('simulatedObservationDistribution')),'manifestations':array(ref('simulatedManifestation')),'examinationPolicies':array(ref('examinationResultPolicy'))},['simulationOnly'])
defs['contentDefinition']={'oneOf':[ref(k+'Definition') for k in kinds]}
defs['contentRelation']=obj({'id':id,'version':version,'kind':{'enum':rels},'fromId':id,'toId':id,'provenance':ref('provenance')},['id','version','kind','fromId','toId','provenance'])
defs['annotationReference']=obj({'externalId':text,'contentId':nullable(id)},['externalId'])
defs['observationFrequency']=obj({'sourceValue':text,'term':nullable(ref('annotationReference'))},['sourceValue'])
defs['medicalAnnotation']=obj({
    'schemaVersion':{'const':'1.0.0'},'id':id,'version':version,
    'kind':{'enum':['diseasePhenotype','medicalAction']},'predicate':text,
    'subject':ref('annotationReference'),'object':ref('annotationReference'),
    'diseaseContext':nullable(ref('annotationReference')),'extensionContext':nullable(ref('annotationReference')),
    'isNegated':{'type':['boolean','null']},'frequency':nullable(ref('observationFrequency')),
    'onset':nullable(ref('annotationReference')),'sex':nullable({'type':'string'}),'aspect':nullable({'type':'string'}),
    'modifiers':array(ref('annotationReference')),'qualifiers':array(text),'provenance':ref('provenance'),
    'sourceFields':{'type':'object','additionalProperties':True}
},['schemaVersion','id','version','kind','predicate','subject','object','provenance'])
defs['contentDependency']=obj({'packageId':id,'versionRange':text},['packageId','versionRange'])
manifest={'schemaVersion':{'const':'1.0.0'},'id':id,'namespace':ns,'version':version,'license':text,'dependencies':array(ref('contentDependency'))}
defs['contentPackageManifest']=obj(manifest,['schemaVersion','id','namespace','version','license'])
defs['modManifest']=obj({**manifest,'nameKey':key,'author':text,'compatibleGameVersions':text,'contentFiles':array({'type':'string','minLength':1,'pattern':r'^(?!/)(?!.*\\)(?!.*:)(?!.*(?:^|/)\.\.?(/|$)).+$'}),'supportedLanguages':array({'type':'string','pattern':r'^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$'})},['schemaVersion','id','namespace','nameKey','author','version','compatibleGameVersions','license'])
bal={'definitionId':id}
for name in ['rarity','severity','diagnosticDifficulty','simulatedSuccessProbability']:bal[name]=nullable({'type':'number','minimum':0,'maximum':1})
for name in ['treatmentDurationSeconds','examinationDurationSeconds','examinationCost','treatmentCost']:bal[name]=nullable({'type':'number','minimum':0})
bal['requiredResources']={'type':'object','propertyNames':text,'additionalProperties':{'type':'number','minimum':0}}
defs['gameplayBalance']=obj(bal,['definitionId'])
defs['contentPackage']=obj({'schemaVersion':{'const':'1.0.0'},'manifest':ref('contentPackageManifest'),'definitions':array(ref('contentDefinition')),'relations':array(ref('contentRelation')),'medicalAnnotations':array(ref('medicalAnnotation')),'referenceRanges':array(ref('observationReferenceRange')),'diagnosticRecommendations':array(ref('diagnosticRecommendation')),'diagnosticSimulation':nullable(ref('diagnosticSimulationSettings')),'gameplay':array(ref('gameplayBalance'))},['schemaVersion','manifest'])
defs['localizationDefinition']=obj({'schemaVersion':{'const':'1.0.0'},'namespace':ns,'language':{'type':'string','pattern':r'^[a-z]{2,3}(-[A-Za-z0-9]{2,8})*$'},'entries':{'type':'object','propertyNames':{'pattern':keypattern},'additionalProperties':text}},['schemaVersion','namespace','language'])
# A single bundled file supports every public exchange contract; small entry files work offline.
dump(s/'contracts.schema.json',{'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'https://schemas.iryomera.org/v1/contracts.schema.json','title':'Iryomera v1 exchange contracts','$defs':defs})
for name in defs:
    dump(s/(name+'.schema.json'),{'$schema':'https://json-schema.org/draft/2020-12/schema','$id':'https://schemas.iryomera.org/v1/'+name+'.schema.json','$ref':'contracts.schema.json#/$defs/'+name})

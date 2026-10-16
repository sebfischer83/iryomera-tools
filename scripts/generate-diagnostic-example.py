"""Own fictional definition-only vertical slice. No clinical values or patient simulation."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / 'examples/synthetic'
NS = 'diagnostics_demo'

def ident(name): return NS + ':' + name

def source(name):
    return {'source':'Iryomera own fictional diagnostic software fixtures; not medical knowledge', 'externalId':name, 'sourceVersion':'synthetic-1', 'license':'MIT', 'importedAt':'2026-01-01T00:00:00Z', 'reviewStatus':'unreviewed', 'evidence':[{'citation':'Own synthetic test fixture only; no medical reference interval or recommendation'}]}

catalogs = {language:{'schemaVersion':'1.0.0','namespace':NS,'language':language,'entries':{}} for language in ('en','de')}

def text(key, en, de):
    full = NS + '.' + key
    catalogs['en']['entries'][full] = en
    catalogs['de']['entries'][full] = de
    return full

package = {'schemaVersion':'1.0.0', 'manifest':{'schemaVersion':'1.0.0','id':ident('content'),'namespace':NS,'version':'1.0.0','license':'MIT','dependencies':[]},'definitions':[],'relations':[],'medicalAnnotations':[],'referenceRanges':[],'diagnosticRecommendations':[],'gameplay':[], 'diagnosticSimulation':{'simulationOnly':True,'observationDistributions':[],'manifestations':[],'examinationPolicies':[]}, 'examplePurpose':'Fictional software fixtures, not clinical facts; no patient runtime is implemented', 'privateGameExampleSeed':2026}

def definition(kind,name,en,de,**extra):
    value={'kind':kind,'id':ident(name),'nameKey':text(name+'.name',en,de),'version':'1.0.0','provenance':source(name),**extra}
    package['definitions'].append(value)
    return value

def relation(kind,left,right):
    name='link_'+kind.lower()+'_'+left+'_'+right
    package['relations'].append({'id':ident(name),'version':'1.0.0','kind':kind,'fromId':ident(left),'toId':ident(right),'provenance':source(name)})

for color,de in [('amber','Bernstein'),('azure','Azur'),('coral','Koralle')]: definition('disease',color+'_condition','Fictional '+color+' condition','Fiktiver '+de+'zustand')
definition('symptom','reported_discomfort','Fictional reported discomfort','Fiktiv berichtetes Unbehagen')
for name,en,de in [('pressure_response','Fictional pressure response','Fiktive Druckreaktion'),('amber_pattern','Fictional amber image pattern','Fiktives Bernsteinbildmuster'),('azure_pattern','Fictional azure rhythm pattern','Fiktives Azurrhythmusmuster')]: definition('finding',name,en,de)
definition('room','consultation_room','Synthetic examination room','Synthetischer Untersuchungsraum')
definition('medicalEquipment','demo_device','Synthetic test device','Synthetisches Testgerät')
definition('staffQualification','demo_operator','Synthetic operator qualification','Synthetische Bedienqualifikation')
definition('treatment','demo_support','Fictional supportive action','Fiktive unterstützende Maßnahme')
exams=[('history','Fictional interview','Fiktive Befragung','narrative','text'),('pressure_check','Fictional pressure examination','Fiktive Druckuntersuchung','pressure_present','boolean'),('sample_measurement','Fictional sample analysis','Fiktive Probenanalyse','sample_index','numeric'),('image_scan','Fictional image examination','Fiktive Bilduntersuchung','image_pattern','categorical'),('rhythm_trace','Fictional trace examination','Fiktive Kurvenuntersuchung','rhythm_pattern','categorical')]
for i,(exam,en,de,observation,kind) in enumerate(exams):
    definition('examination',exam,en,de)
    result={'valueKind':kind,'categories':[],'patientContextKeys':[]}
    if kind=='numeric': result.update(unitCode='1',patientContextKeys=[NS+'.patient.group'])
    if kind=='categorical':
        finding='amber_pattern' if exam=='image_scan' else 'azure_pattern'
        result['categories']=[{'key':'clear','nameKey':text(observation+'.clear','Fictional clear pattern','Fiktiv unauffälliges Muster')},{'key':'pattern','nameKey':text(observation+'.pattern','Fictional marked pattern','Fiktiv markiertes Muster'),'findingId':ident(finding)}]
    definition('observation',observation,'Fictional '+observation.replace('_',' '),'Fiktives Ergebnis: '+de,result=result)
    relation('examinationObservation',exam,observation)
    relation('examinationRoom',exam,'consultation_room')
    relation('examinationQualification',exam,'demo_operator')
    if i>=2: relation('examinationEquipment',exam,'demo_device')
    package['gameplay'].append({'definitionId':ident(exam),'examinationCost':10+i*5,'examinationDurationSeconds':15+i*10,'diagnosticDifficulty':0.2,'requiredResources':{ident('consultation_room'):1}})
    package['diagnosticSimulation']['examinationPolicies'].append({'schemaVersion':'1.0.0','simulationOnly':True,'id':ident('policy_'+exam),'version':'1.0.0','examinationId':ident(exam),'visibility':'afterExamination','repeatPolicy':'untilPatientStateChanges'})
relation('examinationPrerequisite','image_scan','history')
for index,color in enumerate(['amber','azure','coral']):
    disease=color+'_condition'
    relation('diseaseSymptom',disease,'reported_discomfort')
    relation('diseaseFinding',disease,'pressure_response')
    relation('diseaseTreatment',disease,'demo_support')
    for exam,*_,observation,kind in exams:
        relation('diseaseExamination',disease,exam)
        distribution={'schemaVersion':'1.0.0','simulationOnly':True,'id':ident('distribution_'+color+'_'+observation),'version':'1.0.0','diseaseId':ident(disease),'observationId':ident(observation),'categoryWeights':[]}
        distribution['categoryWeights']={}
        if kind=='numeric': distribution.update(numericMinimum=10+index*5,numericMaximum=14+index*5)
        elif kind=='boolean': distribution['trueProbability']=0.2+index*0.2
        elif kind=='categorical': distribution['categoryWeights']={'clear':3-index,'pattern':1+index}
        else: distribution['textKey']=text('response_'+color,'Fictional '+color+' interview response','Fiktive Antwort: '+color)
        package['diagnosticSimulation']['observationDistributions'].append(distribution)
    package['diagnosticSimulation']['manifestations'].append({'schemaVersion':'1.0.0','simulationOnly':True,'id':ident('manifestation_'+color),'version':'1.0.0','diseaseId':ident(disease),'manifestationId':ident('reported_discomfort'),'probability':0.5,'visibility':'initiallyVisible'})
package['referenceRanges']=[{'schemaVersion':'1.0.0','id':ident('fictional_reference_range'),'version':'1.0.0','observationId':ident('sample_index'),'unitCode':'1','minimum':0,'maximum':5,'appliesWhen':[{'factorKey':NS+'.patient.group','category':'illustrative'}],'provenance':source('fictional_reference_range')}]
package['diagnosticRecommendations']=[{'schemaVersion':'1.0.0','id':ident('fictional_recommendation'),'version':'1.0.0','diseaseId':ident('amber_condition'),'examinationId':ident('sample_measurement'),'contextDefinitionIds':[ident('reported_discomfort')],'rationaleKey':text('recommendation.rationale','Fictional authoring example, not medical advice','Fiktives Redaktionsbeispiel, keine medizinische Empfehlung'),'provenance':source('fictional_recommendation')}]
for name,value in [('diagnostics',package),('diagnostics.en',catalogs['en']),('diagnostics.de',catalogs['de'])]:
    (ROOT/(name+'.json')).write_text(json.dumps(value,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')

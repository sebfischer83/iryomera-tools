"""Demo clinic content package generated from the private game's demo fixture values (ADR 0018).

Everything is fictional game content for a playable demo, not medical knowledge. Readings are deterministic
simulation-only distributions; interaction points of transport items are placeholders until the game supplies geometry.
"""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1] / 'examples/demo-clinic'
NS = 'demo_clinic'
SOURCE = 'Iryomera demo clinic fixtures from the private game; fictional demo content, not medical knowledge'
IMPORTED_AT = '2026-10-18T00:00:00Z'
# Game units are opaque demo codes; the SDK prefers UCUM, so fictional scales use UCUM annotations without a dimension.
UNITS = {'demo:C':'Cel', 'demo:beats-per-minute':'/min', 'demo:fictional-pressure':'{fictional_pressure}', 'demo:fictional-count':'{fictional_count}', 'demo:fictional-marker':'{fictional_marker}'}
ROLES = {'Physician':('Physician','Ärztin oder Arzt'), 'Nurse':('Nurse','Pflegekraft'), 'Technician':('Technician','Technische Fachkraft')}
QUALIFICATIONS = {'demo:qualification_imaging':('Imaging qualification','Qualifikation Bildgebung')}
EQUIPMENT = {'demo:equipment_analyzer':('Laboratory analysis','Laboranalyse'), 'demo:equipment_scanner':('Imaging scan','Bildgebender Scan')}
OBSERVATIONS = {
    'demo:complaint':('Complaint','Beschwerden'), 'demo:temperature':('Temperature','Temperatur'), 'demo:pulse':('Pulse','Puls'),
    'demo:pressure':('Pressure (fictional scale)','Druck (fiktive Skala)'), 'demo:count':('Count (fictional scale)','Zählwert (fiktive Skala)'),
    'demo:marker':('Marker (fictional scale)','Marker (fiktive Skala)'), 'demo:urine':('Urine result','Urinbefund'), 'demo:image':('Image result','Bildbefund')}
CATEGORIES = {'clear':('Clear','Unauffällig'), 'marked':('Marked','Auffällig'), 'cloudy':('Cloudy','Verschattet')}
# Placeholder geometry (owner decision): centred seat/lying point and a handle at the back edge, all facing +Z.
POINTS = {
    'Seated':[{'key':'seat','kind':'seat','x':0.5,'y':0.5,'z':0.5,'headingDegrees':0}, {'key':'handle','kind':'handle','x':0.5,'y':0,'z':0,'headingDegrees':0}],
    'Lying':[{'key':'lying','kind':'lying','x':0.5,'y':0.8,'z':1.0,'headingDegrees':0}, {'key':'handle','kind':'handle','x':0.5,'y':0,'z':0,'headingDegrees':0}]}

fixtures = json.loads((ROOT/'source/game-fixtures.json').read_text(encoding='utf-8'))

def local(game_key, prefix=''):
    """demo:blood-pressure -> blood_pressure; the game key itself stays in provenance.externalId."""
    return prefix + game_key.split(':', 1)[1].replace('-', '_').replace(':', '_').lower()

def ident(name): return NS + ':' + name

def source(external_id):
    return {'source':SOURCE, 'externalId':external_id, 'sourceVersion':'game-fixtures-1', 'license':'MIT', 'importedAt':IMPORTED_AT, 'reviewStatus':'unreviewed',
            'evidence':[{'citation':'Own synthetic game fixture; not a medical reference, range or recommendation'}]}

catalogs = {language:{'schemaVersion':'1.0.0', 'namespace':NS, 'language':language, 'entries':{}} for language in ('en', 'de')}

def text(key, en, de):
    full = NS + '.' + key
    catalogs['en']['entries'][full] = en
    catalogs['de']['entries'][full] = de
    return full

package = {'schemaVersion':'1.0.0', 'manifest':{'schemaVersion':'1.0.0', 'id':ident('content'), 'namespace':NS, 'version':'1.0.0', 'license':'MIT', 'dependencies':[]},
           'definitions':[], 'relations':[], 'medicalAnnotations':[], 'referenceRanges':[], 'diagnosticRecommendations':[], 'gameplay':[],
           'diagnosticSimulation':{'simulationOnly':True, 'observationDistributions':[], 'manifestations':[], 'examinationPolicies':[]},
           'namePools':[], 'examplePurpose':'Fictional demo clinic generated from game fixtures; not clinical facts'}

def definition(kind, name, external_id, en, de, **extra):
    value = {'kind':kind, 'id':ident(name), 'nameKey':text(name + '.name', en, de), 'version':'1.0.0', 'provenance':source(external_id), **extra}
    package['definitions'].append(value)
    return value['id']

def relation(kind, left, right, external_id, **extra):
    name = 'link_' + kind.lower() + '_' + left.split(':')[1] + '_' + right.split(':')[1]
    package['relations'].append({'id':ident(name), 'version':'1.0.0', 'kind':kind, 'fromId':left, 'toId':right, 'provenance':source(external_id), **extra})

def balance(definition_id, **values): package['gameplay'].append({'definitionId':definition_id, **values, 'requiredResources':{}})

rooms = {r['key']:definition('room', local(r['key']), r['key'], r['nameEn'], r['nameDe'], minimumCells=r['MinimumCells'], requiresDoor=r['RequiresDoor']) for r in fixtures['roomTypes']}
departments = {}
for d in fixtures['departments']:
    departments[d['key']] = definition('medicalDepartment', local(d['key']), d['key'], d['NameEn'], d['NameDe'])
    balance(departments[d['key']], capacity=d['Capacity'])
roles = {role:definition('staffRole', 'role_' + role.lower(), 'staffRole:' + role, *names) for role, names in ROLES.items()}
qualifications = {key:definition('staffQualification', local(key), key, *names) for key, names in QUALIFICATIONS.items()}
equipment = {key:definition('medicalEquipment', local(key), key, *names) for key, names in EQUIPMENT.items()}

readings = {}
for case in fixtures['cases']:
    for reading in case['readings']: readings.setdefault(reading['ObservationId'], []).append(reading)
observations = {}
for key, (en, de) in OBSERVATIONS.items():
    sample = readings[key][0]
    if sample['Number'] is not None:
        result = {'valueKind':'numeric', 'unitCode':UNITS[sample['Unit']], 'categories':[], 'patientContextKeys':[]}
    elif key == 'demo:complaint':
        result = {'valueKind':'text', 'categories':[], 'patientContextKeys':[]}
    else:
        values = list(dict.fromkeys(r['Qualitative'].split('-', 1)[1] for r in readings[key]))
        order = [c for c in CATEGORIES if c in values]
        result = {'valueKind':'categorical', 'categories':[{'key':c, 'nameKey':text(local(key, 'observation_') + '.' + c, *CATEGORIES[c])} for c in order], 'patientContextKeys':[]}
    observations[key] = definition('observation', local(key, 'observation_'), key, en, de, result=result)

for exam in fixtures['examinations']:
    exam_id = definition('examination', local(exam['key']), exam['key'], exam['NameEn'], exam['NameDe'])
    relation('examinationObservation', exam_id, observations[exam['observation']], exam['key'])
    for room in exam['roomTypes']: relation('examinationRoom', exam_id, rooms[room], exam['key'])
    relation('examinationStaffRole', exam_id, roles[exam['staffRole']], exam['key'])
    for item in exam['equipment']: relation('examinationEquipment', exam_id, equipment[item], exam['key'])
    for item in exam['qualifications']: relation('examinationQualification', exam_id, qualifications[item], exam['key'])
    balance(exam_id, examinationDurationSeconds=exam['durationMinutes'] * 60)

diseases = {}
for case in fixtures['cases']:
    disease = diseases[case['key']] = definition('disease', local(case['key']), case['key'], case['NameEn'], case['NameDe'])
    for reading in case['readings']:
        distribution = {'schemaVersion':'1.0.0', 'simulationOnly':True, 'id':ident('reading_' + local(case['key']) + '_' + local(reading['ObservationId'])), 'version':'1.0.0',
                        'diseaseId':disease, 'observationId':observations[reading['ObservationId']], 'categoryWeights':{}}
        if reading['Number'] is not None: distribution.update(numericMinimum=reading['Number'], numericMaximum=reading['Number'])
        elif reading['ObservationId'] == 'demo:complaint': distribution['textKey'] = text(local(case['key']) + '.complaint', case['ComplaintEn'], case['ComplaintDe'])
        else: distribution['categoryWeights'] = {reading['Qualitative'].split('-', 1)[1]:1}
        package['diagnosticSimulation']['observationDistributions'].append(distribution)
for route in fixtures['diseaseDepartments']:
    allowed = [route['preferred']] + [d for d in route['allowed'] if d != route['preferred']]
    for priority, department in enumerate(allowed, 1):
        relation('diseaseDepartment', diseases[route['disease']], departments[department], route['disease'], priority=priority)

furniture = {}
for item in fixtures['furniture']:
    placement = {'footprint':{'width':item['Width'], 'depth':item['Depth'], 'cellSizeMeters':1, 'occupiedCells':[]}, 'interactionPoints':[]}
    extra = {'catalogAssets':[{'catalogId':item['asset'], 'kind':'modelGlb'}], 'placement':placement}
    if item['transport']:
        placement['interactionPoints'] = [{**point, 'placeholder':True} for point in POINTS[item['transport']]]
        extra.update(mobility='pushable', transport={'patientCapacity':1, 'posture':item['transport'].lower()})
    furniture[item['key']] = definition('furniture', local(item['key']), item['key'], item['NameEn'], item['NameDe'], **extra)
    for capability in item['capabilities']: relation('furnitureEquipment', furniture[item['key']], equipment[capability], item['key'])
for link in fixtures['roomFurniture']:
    relation('roomFurniture', rooms[link['room']], furniture[link['item']], link['room'], requirement=link['requirement'].lower())

for part, index in (('given', 0), ('family', 1)):
    entries = []
    for i, full in enumerate(fixtures['patientNames']):
        value = full.split(' ')[index]
        entries.append({'key':value.lower().replace('ä', 'ae').replace('ö', 'oe').replace('ü', 'ue').replace('ß', 'ss'), 'localized':value, 'gender':'unknown',
                        'attestations':[{'sourceKey':'game_fixture', 'externalId':'patientNames[%d]' % i}]})
    package['namePools'].append({'schemaVersion':'1.0.0', 'id':ident('patients.' + part), 'version':'1.0.0', 'nameKey':text('pool.patients_' + part + '.name', 'Demo patient ' + part + ' names', 'Demo-Patienten: ' + ('Vornamen' if part == 'given' else 'Nachnamen')),
                                 'country':'DE', 'part':part, 'provenance':source('patientNames'), 'sources':[{'key':'game_fixture', 'provenance':source('patientNames')}], 'entries':entries})

for name, value in [('content', package), ('en', catalogs['en']), ('de', catalogs['de'])]:
    (ROOT/(name + '.json')).write_text(json.dumps(value, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')

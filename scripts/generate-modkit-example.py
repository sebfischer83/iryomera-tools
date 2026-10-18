"""Own synthetic MIT data/mesh/images only. No clinical dataset or purchased asset."""
import json, struct, zlib, binascii
from pathlib import Path
root=Path(__file__).resolve().parents[1]/'examples/modkit/example-mod'
(root/'assets').mkdir(parents=True,exist_ok=True);(root/'localization').mkdir(exist_ok=True)
ns='example_mod'
def ident(key): return ns+':'+key
provenance={'source':'Own fictional ModKit software fixture, not medical knowledge','externalId':'synthetic-demo','sourceVersion':'synthetic-1','license':'MIT','importedAt':'2026-01-01T00:00:00Z','reviewStatus':'unreviewed'}
manifest={'schemaVersion':'1.0.0','id':ident('mod'),'namespace':ns,'version':'1.0.0','license':'MIT','dependencies':[]}
content={'schemaVersion':'1.0.0','manifest':manifest,'definitions':[],'relations':[],'gameplay':[],'examplePurpose':'Own fictional data-only mod; not medical advice or reviewed content'}
catalogs={language:{'schemaVersion':'1.0.0','namespace':ns,'language':language,'entries':{ns+'.mod.name':name}} for language,name in [('en','Synthetic example mod'),('de','Synthetische Beispiel-Mod')]}
rows=[('disease','demo_disease','Fictional amber condition','Fiktiver Bernsteinzustand'),('symptom','demo_symptom','Fictional reported discomfort','Fiktiv berichtetes Unbehagen'),('finding','demo_finding','Fictional visible pattern','Fiktives sichtbares Muster'),('examination','demo_examination','Fictional shared test','Fiktive gemeinsame Untersuchung'),('observation','demo_observation','Fictional numeric index','Fiktiver Zahlenindex'),('treatment','demo_treatment','Fictional supportive action','Fiktive unterstützende Maßnahme'),('medicalProcedure','demo_procedure','Fictional procedure','Fiktive Prozedur'),('medicalDepartment','demo_department','Synthetic department','Synthetische Abteilung'),('medicalEquipment','demo_equipment','Synthetic equipment','Synthetisches Gerät'),('staffQualification','demo_qualification','Synthetic qualification','Synthetische Qualifikation'),('room','demo_room','Synthetic examination room','Synthetischer Untersuchungsraum'),('furniture','demo_furniture','Synthetic unit cube furniture','Synthetisches Würfelmöbel'),('staffRole','demo_role','Synthetic staff role','Synthetische Personalrolle'),('furniture','demo_notice_board','Synthetic wall notice board','Synthetische Wand-Infotafel'),('furniture','demo_wheelchair','Synthetic wheelchair','Synthetischer Rollstuhl')]
for kind,key,en,de in rows:
 name=ns+'.'+key+'.name'; d={'kind':kind,'id':ident(key),'nameKey':name,'version':'1.0.0','provenance':provenance.copy()}
 for language,label in [('en',en),('de',de)]:catalogs[language]['entries'][name]=label
 if kind=='observation':d['result']={'valueKind':'numeric','unitCode':'1'}
 if kind=='room':d.update({'minimumCells':6,'requiresDoor':True})
 if key=='demo_wheelchair':
  # Own fictional pushable transport furniture: one seated patient, a handle for the pushing staff and an entry side.
  d['mobility']='pushable'; d['transport']={'patientCapacity':1,'posture':'seated'}
  d['placement']={'footprint':{'width':1,'depth':1,'cellSizeMeters':1},'interactionPoints':[{'key':'seat','kind':'seat','x':0.5,'y':0.45,'z':0.5,'headingDegrees':0},{'key':'handle','kind':'handle','x':0.5,'y':0,'z':0,'headingDegrees':0},{'key':'entry','kind':'entry','x':0.5,'y':0,'z':1.2,'headingDegrees':180}]}
 elif key=='demo_notice_board':
  # Example catalog ID resolved by the game catalog; nothing is shipped. Unknown IDs fall back to a procedural model.
  d['catalogAssets']=[{'catalogId':'iryomera.assets:wall.notice_board_example','kind':'modelGlb'}]
  d['placement']={'footprint':{'width':1,'depth':1,'cellSizeMeters':1},'mount':'wall','mountHeightMeters':1.5}
 elif kind=='furniture':
  d['assets']=[{'path':'assets/cube.glb','kind':'modelGlb','license':'MIT','attribution':'Iryomera own synthetic mesh'},{'path':'assets/texture.png','kind':'texture','license':'MIT','attribution':'Iryomera own synthetic image'},{'path':'assets/icon.png','kind':'icon','license':'MIT','attribution':'Iryomera own synthetic image'}]
  d['placement']={'footprint':{'width':2,'depth':1,'cellSizeMeters':1,'occupiedCells':[{'x':0,'z':0},{'x':1,'z':0}]},'interactionPoints':[{'key':'service','kind':'operator','x':1,'y':0,'z':-0.5,'headingDegrees':0,'requiredQualificationId':ident('demo_qualification')}],'surfaces':[{'key':'top','heightMeters':1,'x':0,'z':0,'width':2,'depth':1,'accepts':['decoration']}]}
 content['definitions'].append(d)
for i,(kind,left,right) in enumerate([('diseaseSymptom','demo_disease','demo_symptom'),('diseaseFinding','demo_disease','demo_finding'),('diseaseExamination','demo_disease','demo_examination'),('diseaseTreatment','demo_disease','demo_treatment'),('examinationObservation','demo_examination','demo_observation'),('examinationEquipment','demo_examination','demo_equipment'),('examinationRoom','demo_examination','demo_room'),('examinationQualification','demo_examination','demo_qualification'),('treatmentQualification','demo_treatment','demo_qualification'),('roomFurniture','demo_room','demo_furniture'),('treatmentEquipment','demo_treatment','demo_equipment'),('roomEquipment','demo_room','demo_equipment'),('examinationStaffRole','demo_examination','demo_role'),('diseaseDepartment','demo_disease','demo_department')]):
 content['relations'].append({'id':ident('relation_'+str(i)),'version':'1.0.0','kind':kind,'fromId':ident(left),'toId':ident(right),'provenance':provenance.copy(),**({'requirement':'required'} if kind=='roomEquipment' else {}),**({'priority':1} if kind=='diseaseDepartment' else {})})
content['gameplay']=[{'definitionId':ident('demo_examination'),'examinationDurationSeconds':12,'examinationCost':8,'diagnosticDifficulty':0.2,'requiredResources':{ident('demo_room'):1}},{'definitionId':ident('demo_disease'),'rarity':0.3,'severity':0.2},{'definitionId':ident('demo_department'),'capacity':4}]
files=[{'path':'content.json','kind':'content','license':'MIT'},{'path':'localization/en.json','kind':'localization','license':'MIT'},{'path':'localization/de.json','kind':'localization','license':'MIT'},{'path':'assets/cube.glb','kind':'modelGlb','license':'MIT','attribution':'Iryomera own synthetic mesh'},{'path':'assets/texture.png','kind':'texture','license':'MIT','attribution':'Iryomera own synthetic image'},{'path':'assets/icon.png','kind':'icon','license':'MIT','attribution':'Iryomera own synthetic image'},{'path':'LICENSE.txt','kind':'notice','license':'MIT'}]
mod={'schemaVersion':'1.0.0','manifest':{**manifest,'nameKey':ns+'.mod.name','author':'Iryomera contributors','compatibleGameVersions':'[0.1.0,1.0.0)','contentFiles':['content.json','localization/en.json','localization/de.json'],'supportedLanguages':['en','de']},'files':files}
for name,data in [('mod.json',mod),('content.json',content),*[(f'localization/{l}.json',c) for l,c in catalogs.items()]]:
 (root/name).write_text(json.dumps(data,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
(root/'LICENSE.txt').write_text((root.parents[2]/'LICENSE').read_text(encoding='utf-8'),encoding='utf-8',newline='\n')
positions=[(x,y,z) for x,y,z in [(0,0,0),(1,0,0),(1,1,0),(0,1,0),(0,0,1),(1,0,1),(1,1,1),(0,1,1)]]
indices=[0,2,1,0,3,2,4,5,6,4,6,7,0,1,5,0,5,4,3,7,6,3,6,2,0,4,7,0,7,3,1,2,6,1,6,5]
binary=b''.join(struct.pack('<fff',*v) for v in positions)+struct.pack('<36H',*indices)
gltf={'asset':{'version':'2.0','generator':'Iryomera own synthetic mesh generator'},'scene':0,'scenes':[{'nodes':[0]}],'nodes':[{'mesh':0}],'meshes':[{'primitives':[{'attributes':{'POSITION':0},'indices':1}]}],'buffers':[{'byteLength':len(binary)}],'bufferViews':[{'buffer':0,'byteOffset':0,'byteLength':96,'target':34962},{'buffer':0,'byteOffset':96,'byteLength':72,'target':34963}],'accessors':[{'bufferView':0,'componentType':5126,'count':8,'type':'VEC3','min':[0,0,0],'max':[1,1,1]},{'bufferView':1,'componentType':5123,'count':36,'type':'SCALAR'}]}
encoded=json.dumps(gltf,separators=(',',':')).encode('utf-8');encoded+=b' '*((-len(encoded))%4)
(root/'assets/cube.glb').write_bytes(struct.pack('<III',0x46546c67,2,28+len(encoded)+len(binary))+struct.pack('<II',len(encoded),0x4e4f534a)+encoded+struct.pack('<II',len(binary),0x004e4942)+binary)
def chunk(kind,value):return struct.pack('>I',len(value))+kind+value+struct.pack('>I',binascii.crc32(kind+value)&0xffffffff)
png=b'\x89PNG\r\n\x1a\n'+chunk(b'IHDR',struct.pack('>IIBBBBB',1,1,8,6,0,0,0))+chunk(b'IDAT',zlib.compress(bytes([0,32,128,180,255]), level=0))+chunk(b'IEND',b'')
for name in ['texture','icon']:(root/f'assets/{name}.png').write_bytes(png)

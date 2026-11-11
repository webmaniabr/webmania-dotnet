#!/usr/bin/env python3
"""Build synthetic socket fixtures from pinned public schemas and their synthetic schema cases.
Run with jsonschema installed. This is test preparation, never a Webmania API call.
"""
from pathlib import Path
import json,re,copy,subprocess
from jsonschema import Draft202012Validator
import sys
ROOT=Path(__file__).resolve().parents[1]
def option(name,default):return sys.argv[sys.argv.index(name)+1] if name in sys.argv else default
# The webmania-openapi checkout whose vendored modules match contracts.lock.json (contract_digest).
SOURCE=Path(option('--source',str(ROOT.parent/'webmania-openapi'))); REVISION=option('--revision','HEAD')
def gitjson(p):return json.loads(subprocess.check_output(['git','-C',str(SOURCE),'show',REVISION+':'+p]))
samples={}
schema_cases=[]
for p in ['tests/api2/cases.json','tests/api2/b2b-cases.json','contracts/api1/semantic-cases.json','tests/api2/text-size-cases.json']:
 for c in gitjson(p)['cases']:
  mod=c.get('module','api1/fiscal.openapi.json');mod=mod if mod.startswith('api') else 'api2/'+mod
  schema_cases.append(dict(c,module='contracts/'+mod,source=p))
  if c.get('valid') and c.get('schema'):
   mod=c.get('module','api1/fiscal.openapi.json');mod=mod if mod.startswith('api') else 'api2/'+mod
   samples.setdefault((mod,c['schema']),c['instance'])
U='00000000-0000-4000-8000-000000000001'
def validator(s,d):return Draft202012Validator(dict(s,components=d['components']))
def valid(s,v,d):return validator(s,d).is_valid(v)
def make(s,d,mod,depth=0):
 if depth>30:return {}
 if '$ref' in s:
  n=s['$ref'].split('/')[-1]
  if (mod,n) in samples:return copy.deepcopy(samples[mod,n])
  return make(d['components']['schemas'][n],d,mod,depth+1)
 if 'const'in s:return s['const']
 if 'enum'in s:return s['enum'][0]
 t=s.get('type');t=t[0] if isinstance(t,list) else t
 if t=='object' or 'properties'in s:
  v={};props=s.get('properties',{})
  for k in s.get('required',[]):v[k]=make(props.get(k,{}),d,mod,depth+1)
  def repair(branch):
   for key in ('anyOf','oneOf'):
    if key in branch and not any(valid(b,v,d) for b in branch[key]):repair(branch[key][0])
   if '$ref'in branch:
    b=make(branch,d,mod,depth+1)
    if isinstance(b,dict):v.update(b)
   for k in branch.get('required',[]):v.setdefault(k,make(props.get(k,branch.get('properties',{}).get(k,{})),d,mod,depth+1))
   for k,bs in branch.get('properties',{}).items():
    if k in v and not valid(bs,v[k],d):v[k]=make(bs,d,mod,depth+1)
  for _ in range(5):
   for k in ('anyOf','oneOf'):
    if k in s and not any(valid(b,v,d) for b in s[k]):repair(s[k][0])
   for b in s.get('allOf',[]):
    if 'if' in b:
     if valid(b['if'],v,d):repair(b.get('then',{}))
    else:repair(b)
   for k,ds in s.get('dependentRequired',{}).items():
    if k in v:
     for dep in ds:v.setdefault(dep,make(props.get(dep,{}),d,mod,depth+1))
  return v
 if 'anyOf'in s or 'oneOf'in s:
  return make(s.get('anyOf',s.get('oneOf'))[0],d,mod,depth+1)
 if 'allOf'in s:
  v={}
  for b in s['allOf']:
   bv=make(b,d,mod,depth+1)
   if isinstance(bv,dict):v.update(bv)
  return v
 if t=='array':return [make(s.get('items',{}),d,mod,depth+1) for _ in range(int(s.get('minItems',0)))]
 if t in ('number','integer'):return max(s.get('minimum',0),0)
 if t=='boolean':return False
 if t=='null':return None
 if t=='string':
  if s.get('format')=='uuid':return U
  if s.get('format')=='date':return '2026-10-25'
  if s.get('format')=='date-time':return '2026-10-25T12:00:00Z'
  if s.get('format')=='uri':return 'https://example.invalid/synthetic'
  mn=s.get('minLength',1);mx=s.get('maxLength',1000);ln=int(min(max(mn,1),mx))
  candidates=['0'*ln,'x'*ln,U,'0'*44,'0'*50,'0'*14,'0'*11,'0'*8,'0'*7,'0000.00.00','000.000.000-00','00.000.000/0000-00','01001-000','SP','0000000','01','2026','1','0','2026-10-25','2026-10-25 12:00:00','synthetic@example.invalid']
  for c in candidates:
   if len(c)>=mn and len(c)<=mx and ('pattern' not in s or re.search(s['pattern'],c)):return c
  return 'x'*ln
 return {}
fixtures=[]
for op in json.loads((ROOT/'src/Webmania.Sdk/operations.json').read_text(encoding='utf-8')):
 d=json.loads((ROOT/op['module']).read_text());mod=op['module'].removeprefix('contracts/')
 params=make(op['schema'],d,mod)
 for parameter in op['parameters']:
  if parameter['in']=='query':params[parameter['name']]=make(parameter['schema'],d,mod)
 if op['id']=='nfeGetDocument':params['uuid']=U
 if op['id']=='emitterIssueDce':
  params['body']['cliente']={'cpf':'00000000000','nome_completo':'Synthetic recipient'}
  params['body']['emitente']={'cpf':'00000000000','nome_completo':'Synthetic issuer'}

 if op['id']=='emitterReplaceNfse':
  issue=copy.deepcopy(samples['api2/nfse-issuance.openapi.json','EmitterIssueNfsePayload'])
  for key in ('rps','servico','cliente'):
   if key in issue:params['body'][key]=issue[key]
  params['body']['rps'][0]['servico']['codigo_servico']='0101'
 # Successful schema branches must be represented by their required fields; future enums are tested separately.
 response=make(op['responseSchema'],d,mod) if op['responseSchema'] else {}
 errors=list(validator(op['schema'],d).iter_errors(params))
 rerrors=list(validator(op['responseSchema'],d).iter_errors(response)) if op['responseSchema'] and not op['binary'] else []
 if errors:print('REQUEST',op['id'],[(list(e.path),e.message[:250]) for e in errors[:3]])
 if rerrors:print('RESPONSE',op['id'],[(list(e.path),e.message[:250]) for e in rerrors[:3]])
 fixtures.append({'id':op['id'],'input':params,'response':response,'requestValid':not errors,'responseValid':not rerrors})
(ROOT/'tests/SocketTests/fixtures.json').write_bytes((json.dumps(fixtures,ensure_ascii=False,indent=2)+'\n').encode('utf-8'))
print('fixtures',len(fixtures),'invalid request',sum(not x['requestValid'] for x in fixtures),'invalid response',sum(not x['responseValid'] for x in fixtures))

(ROOT/'tests/SocketTests/schema-cases.json').write_bytes((json.dumps(schema_cases,ensure_ascii=False,indent=2)+'\n').encode('utf-8'))

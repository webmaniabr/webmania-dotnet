#!/usr/bin/env python3
"""Inspect package payload and embedded portable PDB without executing package content."""
from pathlib import Path
import hashlib,json,re,struct,sys,zipfile,zlib
p=Path(sys.argv[1]); issues=[];files=[]
forbidden=[b'/Users/',b'/home/',b'PRIVATE KEY',b'synthetic-monitor',b'synthetic-secret']
def scan(data,label):
 for term in forbidden:
  if term in data or term.decode().encode('utf-16le') in data:issues.append(label+': '+term.decode())
def embedded_pdb(data):
 off=struct.unpack_from('<I',data,0x3c)[0];nsections=struct.unpack_from('<H',data,off+6)[0];optlen=struct.unpack_from('<H',data,off+20)[0];opt=off+24
 magic=struct.unpack_from('<H',data,opt)[0];dirs=opt+(112 if magic==0x20b else 96);debugrva,debuglen=struct.unpack_from('<II',data,dirs+6*8)
 secs=opt+optlen
 def ptr(rva):
  for n in range(nsections):
   size,va,rawsize,rawptr=struct.unpack_from('<IIII',data,secs+n*40+8)
   if va<=rva<va+max(size,rawsize):return rawptr+rva-va
  raise ValueError('RVA outside section')
 debug=ptr(debugrva)
 for entry in range(0,debuglen,28):
  dtype,size,_,pos=struct.unpack_from('<IIII',data,debug+entry+12)
  if dtype==17:
   block=data[pos:pos+size]
   if block[:4]!=b'MPDB':raise SystemExit('Embedded PDB entry is malformed.')
   return zlib.decompress(block[8:],-15)
 return b''
with zipfile.ZipFile(p) as package:
 for name in package.namelist():
  data=package.read(name);scan(data,name);files.append({'path':name,'sha256':hashlib.sha256(data).hexdigest(),'bytes':len(data)})
  if name.endswith('.dll'):
   pdb=embedded_pdb(data);scan(pdb,name+':embedded-pdb')
  if name.endswith('.nuspec'):
   text=data.decode()
   if '<license type="file">LICENSE</license>' not in text:issues.append({'path':name,'rule':'nuspec-license'})
  if any(x in name.split('/') for x in ('bin','obj','tests','artifacts','.git')):issues.append({'path':name,'rule':'build-or-test-path'})
 # Explicit checks (not assert) so the audit also fails under python -O.
 for required in ('LICENSE','README.md','contracts.lock.json','lib/net10.0/Webmania.Sdk.dll'):
  if required not in package.namelist():issues.append({'path':required,'rule':'missing-package-file'})
result={'status':'pass' if not issues else 'fail','packageSha256':hashlib.sha256(p.read_bytes()).hexdigest(),'files':files,'issues':issues}
print(json.dumps(result,indent=2))
raise SystemExit(bool(issues))

import json, base64, hashlib, sys
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
d = json.load(open(sys.argv[1])); pw = sys.argv[2]
k = hashlib.pbkdf2_hmac('sha256', pw.encode(), base64.b64decode(d['kdf']['salt']), d['kdf']['iterations'], 32)
try:
    pt = AESGCM(k).decrypt(base64.b64decode(d['cipher']['iv']), base64.b64decode(d['data']), None)
    j = json.loads(pt); print('OK déchiffré, format interne :', j['format'], '| contient Lefebvre :', 'Lefebvre' in pt.decode())
except Exception as e: print('ÉCHEC', type(e).__name__)

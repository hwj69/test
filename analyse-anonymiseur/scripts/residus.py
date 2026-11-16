import sys, zipfile, re, html
SENS = ["Sophie","Lefebvre","Jean Dupont","Dupont","Pierre Durand","Durand","Paul Bernard","Bernard","Marie","Curie",
 "gmail","orange.fr","free.fr","laposte","acme-conseil","ACME","République","69002","Lyon",
 "06 12 34 56 78","0612345679","07 98 76 54 32","FR76","3000 6000","FR14","2004 1010","300060000112",
 "185057800608491","1 85 05 78","285057800608441","552 100 554","AB-123-CD"]
def scan_zip(path):
    z = zipfile.ZipFile(path)
    for n in z.namelist():
        if not re.search(r'\.(xml|rels|vml)$', n): continue
        t = z.read(n).decode('utf8','ignore')
        txt = html.unescape(re.sub(r'<[^>]+>', ' ', t))
        txt = re.sub(r'\s+',' ',txt)
        hits = [s for s in SENS if s in txt or s in t]
        if hits: print(f"  {n}: {hits}")
for p in sys.argv[1:]:
    print("==", p.split('/')[-1]); scan_zip(p)

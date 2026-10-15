import json,sys
D=sys.argv[1]
data={"economy":json.load(open(D+'/economy.json')),"crops":json.load(open(D+'/crop_profiles.json')),"missions":json.load(open(D+'/missions.json'))}
for k in data: data[k].pop('notes',None)
s=open('src.html').read().replace('__DATA__',json.dumps(data,ensure_ascii=False,separators=(',',':')))
open('index.html','w').write(s)
print(len(s))

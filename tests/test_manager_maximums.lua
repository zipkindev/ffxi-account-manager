local root=assert(arg[1], 'Supply a temporary directory ending in /')
local addon=arg[2] or 'windower_addons/FFXIManager/FFXIManager.lua'
local now=1900000000
os.time=function() return now end
local handlers,commands={},{}
local player={name='Nanakia',id=123,main_job='DRK',main_job_id=8,main_job_level=99,sub_job='WAR',sub_job_level=58,status=0,
 vitals={hp=3214,max_hp=2731,hpp=100,mp=830,max_mp=1074,mpp=77}}
local info={logged_in=true,server=1,zone=272,time=1,moon=50}
local cache={
 [0x00A]={Player=123,Zone=272,['Main Job']=8,['Max HP']=3214,['Max MP']=1074},
 [0x01B]={['Main Job']=8,['Maximum HP']=3214,['Maximum MP']=1074},
 [0x061]={['Main Job']=8,['Maximum HP']=2731,['Maximum MP']=1074,['Master Level']=46}}
local cache_reads={}
package.loaded.resources={servers={[1]={en='Asura'}},zones={[272]={en='Dho Gates'},[273]={en='Fixture zone'}}}
package.loaded.packets={parse=function(_,data)return data end}
_addon={}
windower={addon_path=root,packets={last_incoming=function(id)cache_reads[id]=(cache_reads[id]or 0)+1;return cache[id]end},
 ffxi={get_player=function()return player end,get_info=function()return info end},
 register_event=function(name,fn)handlers[name]=fn end,send_command=function(c)commands[#commands+1]=c end}
local function rows()
 local f=assert(io.open(root..'data/asura_nanakia.state','rb'));local s=f:read('*a');f:close()
 local r={};for l in s:gmatch('([^\n]*)\n')do r[#r+1]=l end;return r,s
end
local function expect(hp,maxhp,mp,maxmp,message)
 local r=rows();assert(tonumber(r[6])==hp and tonumber(r[7])==maxhp and tonumber(r[8])==mp and tonumber(r[9])==maxmp,message)
end
local function report()now=now+60;handlers.prerender()end
local function packet(id,hp,mp,injected,blocked,job)
 handlers['incoming chunk'](id,{['Main Job']=job or 8,['Maximum HP']=hp,['Maximum MP']=mp},nil,injected or false,blocked or false)
end
dofile(addon)
expect(3214,3214,830,1074,'Nanakia live mismatch uses explicit matching actor/zone/job cache maxima, not the stale API total')
assert(player.vitals.max_hp==2731,'report correction must not mutate Windower player data')
local reads=cache_reads[0x01B];report();assert(cache_reads[0x01B]==reads,'cache bootstrap does not add repeated packet-cache reads')
local before=select(2,rows());handlers['incoming chunk'](0x050,'',nil,false,false);assert(select(2,rows())==before,'gear changes invalidate evidence without extra stats writes')
player.vitals={hp=2400,max_hp=2000,hpp=80,mp=490,max_mp=500,mpp=70}
report();expect(2400,2000,490,500,'old gear cache is not reused after an equipment change')
packet(0x01B,3000,700);report();expect(2400,3000,490,700,'fresh job information after gear change restores explicit current HP/MP maxima')
player.vitals={hp=1500,max_hp=3000,hpp=75,mp=350,max_mp=700,mpp=50}
packet(0x01B,2000,700);report();expect(1500,2000,350,700,'current packet maximum wins even when a stale larger API maximum exceeds current HP')
player.vitals.hp,player.vitals.hpp=1501,76;report();expect(1501,2000,350,700,'percentage rounding checks a provided maximum without estimating one')
player.vitals={hp=3600,max_hp=3000,hpp=90,mp=810,max_mp=700,mpp=90}
packet(0x061,4000,900);report();expect(3600,4000,810,900,'fresh character stats supersede the previous explicit maximum')
packet(0x061,4001,901,true,false);report();expect(3600,4000,810,900,'injected stats cannot change a maximum')
packet(0x01B,4001,901,false,true);report();expect(3600,4000,810,900,'blocked stats cannot change a maximum')
packet(0x01B,4001,901,false,false,7);report();expect(3600,4000,810,900,'another job cannot change current maxima')
packet(0x01B,math.huge,901);report();expect(3600,4000,810,900,'invalid packet maxima cannot replace valid evidence')
info.zone=273;player.vitals={hp=6000,max_hp=6000,hpp=100,mp=100,max_mp=100,mpp=100};handlers['zone change']()
expect(6000,6000,100,100,'zone change invalidates the old maximum and rejects the old zone cache')
info.zone=272;player.id=456;player.vitals={hp=3214,max_hp=2731,hpp=100,mp=830,max_mp=1074,mpp=77};handlers.login()
expect(3214,2731,830,1074,'a cached zone packet for another actor cannot bootstrap a maximum')
player.id=123;cache[0x01B]['Main Job']=7;handlers.login();expect(3214,2731,830,1074,'a cached job packet for another job cannot bootstrap a maximum')
cache[0x01B]['Main Job']=8;cache[0x01B]['Maximum HP']=4000;handlers.login();expect(3214,2731,830,1074,'disagreeing startup caches cannot bootstrap a maximum')
cache[0x01B]['Maximum HP']=3214;player.vitals.hpp=nil;handlers.login();expect(3214,2731,830,1074,'missing percentage cannot authorize a cached maximum or a guess')
assert(#commands==0,'maximum reporting never sends game commands or server requests')
print('PASS: explicit maximum HP/MP evidence, gear/zone/job/actor invalidation, percentage coherence, rounding, cache cost and no game commands')

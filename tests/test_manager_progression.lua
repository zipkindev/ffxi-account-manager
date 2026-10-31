local root=assert(arg[1], 'Supply a temporary directory ending in /')
local mode=arg[2] or 'missing'
local now=1900000000
os.time=function()return now end
local events,requests={},{}
local player={name='Tester',main_job='WAR',main_job_id=1,main_job_level=99,sub_job='SAM',sub_job_level=49,status=0,
 vitals={hp=100,max_hp=100,mp=0,max_mp=0},job_points={war={cp=1234,jp=123},whm={cp=12,jp=1}}}
local stats={['Main Job']=1,['Master Level']=45,['Current EXP']=55999,['Required EXP']=56000,
 ['Current Exemplar Points']=123456,['Required Exemplar Points']=1500000}
package.loaded.resources={servers={[1]={en='Odin'}},zones={[100]={en='Field'}}}
package.loaded.packets={
 parse=function(_,data)return data end,
 new=function(direction,id,values)assert(direction=='outgoing' and id==0x061 and values._unknown1==string.rep('\0',4));return {id=id}end,
 inject=function(packet)requests[#requests+1]=packet.id;if mode=='failed' then error('fixture transport failure')end end}
_addon={}
windower={addon_path=root,packets={last_incoming=function(id)
 if id==0x061 then return stats end
 if id==0x063 then return mode=='cached' and {Order=2,['Merit Points']=75,['Max Merit Points']=75,['Limit Points']=9999} or {Order=5} end
 end},
 ffxi={get_player=function()return player end,get_info=function()return {logged_in=true,server=1,zone=100}end},
 register_event=function(name,fn)events[name]=fn end,send_command=function()error('Progression never issues game commands')end}
local function read()
 local f=assert(io.open(root..'data/odin_tester.points','rb'));local text=f:read('*a');f:close()
 local rows={};for row in text:gmatch('([^\n]*)\n')do rows[#rows+1]=row end;return rows,text
end
local function tick()now=now+60;events.prerender()end
local function incoming(id,packet,injected,blocked)events['incoming chunk'](id,packet,nil,injected or false,blocked or false)end
dofile('windower_addons/FFXIManager/FFXIManager.lua')
assert(#requests==(mode=='cached' and 0 or 1),'missing merit subtype triggers one ordinary stats refresh; cached merit subtype skips refresh')
local rows=read();assert(rows[12]=='123456' and rows[13]=='1500000','EP and next-level requirement come from authoritative character stats')
for n=1,3 do tick()end
events.load();events['zone change']();tick()
assert(#requests==(mode=='cached' and 0 or 1),'timer, load event and zoning never repeat a startup refresh, even if it failed')
incoming(0x063,{Order=2,['Merit Points']=75,['Max Merit Points']=75,['Limit Points']=9999});tick()
rows=read();assert(rows[8]=='75' and rows[9]=='75','real merit response replaces missing values with current/maximum')
incoming(0x02D,{Message=809,['Param 1']=500});tick();rows=read()
assert(rows[12]=='123956','EP reward messages advance progress from an authoritative baseline')
incoming(0x02D,{Message=810,['Param 1']=1000},true);tick();rows=read();assert(rows[12]=='123956','injected EP rewards are ignored')
incoming(0x061,{['Main Job']=1,['Master Level']=45,['Current Exemplar Points']=999,['Required Exemplar Points']=1000},false,true)
tick();rows=read();assert(rows[12]=='123956','blocked character stats cannot replace EP')
incoming(0x02D,{Message=809,['Param 1']=1500000});tick();rows=read()
assert(rows[12]=='-1' and rows[13]=='-1','level crossing waits for a new authoritative EP threshold instead of fabricating it')
stats={['Main Job']=1,['Master Level']=46,['Current Exemplar Points']=5,['Required Exemplar Points']=2000000}
incoming(0x061,stats);tick();rows=read();assert(rows[12]=='5' and rows[13]=='2000000','new master-level stats restore EP progress')
player.main_job,player.main_job_id='WHM',3;tick();rows=read()
assert(rows[12]=='-1' and rows[13]=='-1','job switch cannot inherit another job’s EP')
print('PASS: master-level progress and bounded merit refresh ('..mode..')')

local root = assert(arg[1], 'Supply a temporary test directory ending in /')
local now = 1900000000
local old_time = os.time
os.time = function() return now end
local events, commands = {}, {}
package.loaded.resources = {servers = {[1] = {en = 'Odin'}}, zones = {[100] = {en = 'West Ronfaure'}, [280] = {en = 'Mog Garden'}}, weather = {[4] = {en = 'Rain'}}, days = {[1] = {en = 'Firesday'}}, moon_phases = {[2] = {en = 'Waxing Crescent'}}}
package.loaded.packets = {parse = function(_, original) return original end}
_addon = {}
local logged_in, zone, mog_house = true, 100, false
local player = {main_job = 'DRK', main_job_id = 8, main_job_level = 99, sub_job = 'WAR', sub_job_id = 1, sub_job_level = 57, name = 'Hauplibond', status = 0, vitals = {hp = 1200, max_hp = 1500, mp = 200, max_mp = 500}}
windower = {
    addon_path = root,
    packets = {last_incoming = function() return {['Main Job'] = 8, ['Master Level'] = 46} end},
    ffxi = {get_info = function() return {logged_in = logged_in, server = 1, zone = zone, mog_house = mog_house, time = 1184, weather = 4, day = 1, moon_phase = 2, moon = 25} end,
            get_player = function() return logged_in and player or nil end},
    register_event = function(name, handler) events[name] = handler end,
    send_command = function(command) commands[#commands + 1] = command end,
}
local function write(path, value)
    local file = assert(io.open(root .. 'data/' .. path, 'wb'))
    file:write(value); file:close()
end
local function read(path)
    local file = io.open(root .. 'data/' .. path, 'rb')
    if not file then return nil end
    local value = file:read('*a'); file:close(); return value
end
write('control.token', string.rep('a', 64))
dofile('windower_addons/FFXIManager/FFXIManager.lua')
assert(read('odin_hauplibond.state'), 'addon publishes immediately when loaded in an online character')
events.prerender()
local status = assert(read('odin_hauplibond.status'))
local session = status:match('^([^\n]+)')
assert(read('odin_hauplibond.connection'):match('\n0\n$'), 'render heartbeat alone must not claim server traffic')
events['incoming chunk'](0x00A, '', nil, true, false)
now = now + 1; events.prerender()
assert(read('odin_hauplibond.connection'):match('\n0\n$'), 'injected packets cannot refresh server liveness')
events['incoming chunk'](0x00A, '', nil, false, false)
local incoming_at = now
now = now + 1; events.prerender()
assert(read('odin_hauplibond.connection'):match('\n' .. incoming_at .. '\n$'), 'real incoming packets refresh separate connection liveness')
assert(status:match('\nHauplibond\nOdin\n'), 'heartbeat must carry character and world')
assert(read('odin_hauplibond.environment'):find('\n100\nRain\n1184\nFiresday\nWaxing Crescent\n25\n', 1, true), 'zone environment reports game weather, time, day and moon')
assert(read('odin_hauplibond.jobs'):find('\n46\nDRK\n99\nWAR\n57\n', 1, true), 'jobs report includes cached master level and actual subjob level')
local nonce = string.rep('b', 32)
local function request(token, target, operation, expiry, request_nonce)
    write('odin_hauplibond.request', token .. '\n' .. (request_nonce or nonce) .. '\n' .. target .. '\n' .. operation .. '\n' .. expiry .. '\n')
    now = now + 1
    events.prerender()
end
request(string.rep('c', 64), session, 'shutdown', now + 15)
assert(#commands == 0, 'bad authentication must not dispatch')
request(string.rep('a', 64), 'old_session', 'shutdown', now + 15)
assert(#commands == 0, 'old session must not dispatch')
request(string.rep('a', 64), session, 'shutdown', now - 1)
assert(#commands == 0, 'expired shutdown must not dispatch')
request(string.rep('a', 64), session, 'shell', now + 15)
assert(#commands == 0, 'arbitrary commands must not dispatch')
request(string.rep('a', 64), session, 'shutdown', now + 15, string.rep('d', 32))
assert(#commands == 1 and commands[1] == 'input /shutdown', 'valid request must dispatch exactly shutdown')
assert(read('odin_hauplibond.receipt'):match('dispatched\n$'), 'receipt reports dispatch, not exit')
request(string.rep('a', 64), session, 'shutdown', now + 15, string.rep('d', 32))
assert(#commands == 1, 'replayed nonce must not dispatch twice')
request(string.rep('a', 64), session, 'shutdown', now + 15, string.rep('e', 32))
assert(#commands == 2, 'a separate authenticated request is independent')
request(string.rep('a', 64), session, 'shutdown', now + 15, string.rep('d', 32))
assert(#commands == 2, 'nonconsecutive replay must not dispatch')
request(string.rep('a', 64), session, 'cancel_shutdown', now + 15, string.rep('f', 32))
assert(#commands == 3 and commands[3] == 'input /shutdown off', 'authenticated cancellation cancels the shutdown countdown in the local session')
request(string.rep('a', 64), session, 'cancel_shutdown', now + 15, string.rep('f', 32))
assert(#commands == 3, 'replayed cancellation cannot send another key')
request(string.rep('a', 64), session, 'cancel_shutdown', now + 15, string.rep('9', 32))
assert(#commands == 3, 'cancellation without an active manager shutdown cannot inject input')
local warning = {Mode = 6, Message = 'Emergency maintenance will begin shortly. Please log out.'}
events['incoming chunk'](0x017, {Mode = 3, Message = warning.Message}, nil, false)
assert(read('odin_hauplibond.alert') == nil, 'player messages must not create holds')
events['incoming chunk'](0x017, warning, nil, true)
assert(read('odin_hauplibond.alert') == nil, 'injected packets must not create holds')
events['incoming chunk'](0x017, {Mode = 7, Message = 'Maintenance has completed.'}, nil, false)
assert(read('odin_hauplibond.alert') == nil, 'completion must not create a new hold')
events['incoming chunk'](0x017, warning, nil, false)
assert(read('odin_hauplibond.alert'):find('incoming-017-system\n' .. warning.Message, 1, true), 'system warning preserves evidence')
assert(read('odin_hauplibond.state'):find('1200\n1500\n200\n500\n0\n100\nWest Ronfaure\n0\n', 1, true), 'live snapshot includes stats and zone')
player.status, player.vitals.hp, zone = 3, 0, 101
local before_report = read('odin_hauplibond.state')
local before_jobs = read('odin_hauplibond.jobs')
now = now + 1
events.prerender()
assert(read('odin_hauplibond.jobs') == before_jobs, 'job reports retain the one-minute cadence')
assert(read('odin_hauplibond.state') == before_report, 'display reports are not rewritten between minute samples')
assert(read('odin_hauplibond.status') ~= status, 'health heartbeat remains independent of display reporting')
now = now + 60
events.prerender()
local final_state = read('odin_hauplibond.state')
assert(final_state:find('0\n1500\n200\n500\n1\n101\nZone 101\n0\n', 1, true), 'death and new zone reported with fallback name')
events['incoming chunk'](0x061, {['Main Job'] = 8, ['Master Level'] = 47}, nil, true)
now = now + 60
events.prerender()
assert(read('odin_hauplibond.jobs'):find('\n46\nDRK\n', 1, true), 'injected packets cannot change master level')
events['incoming chunk'](0x061, {['Main Job'] = 8, ['Master Level'] = 47}, nil, false)
now = now + 60
events.prerender()
assert(read('odin_hauplibond.jobs'):find('\n47\nDRK\n', 1, true), 'passive server stats refresh master level')
player.main_job, player.main_job_id = 'PLD', 7
now = now + 60
events.prerender()
assert(read('odin_hauplibond.jobs'):find('\n-1\nPLD\n', 1, true), 'job change cannot inherit another job master level or cached packet')
player.main_job, player.main_job_id = 'DRK', 8
now = now + 60
events.prerender()
final_state = read('odin_hauplibond.state')
zone = 0
now = now + 1
events.prerender()
assert(read('odin_hauplibond.state') == final_state, 'incomplete zoning preserves last stats')
logged_in = false
events.logout()
assert(read('odin_hauplibond.state') == final_state:sub(1, -3) .. now .. '\n', 'dead logout state persists without player data')
assert(read('odin_hauplibond.status') == nil, 'logout clears own heartbeat')
assert(read('odin_hauplibond.alert') ~= nil, 'warning evidence survives logout')
logged_in, zone = true, 100
player.status, player.vitals.hp = 0, 1500
now = now + 1
events.prerender()
assert(read('odin_hauplibond.state'):match('\n0\n$'), 'new login clears logout time')
assert(read('odin_hauplibond.status'):match('^([^\n]+)') ~= session, 'new login has new session identity')
local before_unload = read('odin_hauplibond.state')
events.unload()
assert(read('odin_hauplibond.state') == before_unload, 'unload preserves snapshot without claiming logout')
-- Startup zeros default to alive and are refreshed at fifteen seconds.
player.status, player.vitals.hp, player.vitals.mp = 0, 0, 0
now = now + 1
events.login()
assert(read('odin_hauplibond.state'):find('0\n1500\n0\n500\n0\n100\nWest Ronfaure', 1, true), 'zero startup HP without dead status is not death')
local startup_state = read('odin_hauplibond.state')
player.vitals.hp, player.vitals.mp = 1500, 500
now = now + 14
events.prerender()
assert(read('odin_hauplibond.state') == startup_state, 'startup follow-up does not increase routine sampling to once per second')
now = now + 1
events.prerender()
assert(read('odin_hauplibond.state'):find('1500\n1500\n500\n500\n0\n', 1, true), 'startup vitals refresh at fifteen seconds')
local refreshed = read('odin_hauplibond.state')
now = now + 59
events.prerender()
assert(read('odin_hauplibond.state') == refreshed, 'routine updates remain once per minute after startup follow-up')
now = now + 1
events.prerender()
assert(read('odin_hauplibond.state') ~= refreshed, 'minute reporting resumes after startup follow-up')
player.status, player.vitals.hp, zone = 2, 0, 280
now = now + 1
events['zone change']()
assert(read('odin_hauplibond.state'):find('0\n1500\n500\n500\n0\n280\nMog Garden', 1, true), 'Mog Garden cannot report death from transient vitals/status')
zone, mog_house = 100, true
now = now + 1
events['zone change']()
assert(read('odin_hauplibond.state'):find('0\n1500\n500\n500\n0\n100\n', 1, true), 'Mog House flag suppresses transient death')
mog_house = false
now = now + 1
events['status change']()
assert(read('odin_hauplibond.state'):find('0\n1500\n500\n500\n1\n100\n', 1, true), 'confirmed dead status and zero HP update immediately outside safe areas')
player.status, player.vitals.hp = 0, 1500
now = now + 1
events['status change']()
assert(read('odin_hauplibond.state'):find('1500\n1500\n500\n500\n0\n100\n', 1, true), 'revival reports immediately instead of waiting a minute')
os.time = old_time
print('PASS: addon authentication, session binding, expiry, allowlist, at-most-once shutdown, and logout cleanup')

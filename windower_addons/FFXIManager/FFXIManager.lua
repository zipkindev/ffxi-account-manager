_addon.name = 'FFXIManager'
_addon.author = 'FFXI Account Manager'
_addon.version = '0.3.7'
_addon.commands = {'ffximanager'}
local resources = require('resources')
local packets = require('packets')
local root = windower.addon_path .. 'data/'
local session = tostring(os.time()) .. '_' .. tostring({}):gsub('[^0-9a-f]', '') .. '_' .. tostring(math.random(100000, 999999999))
local registered = os.time()
local last_poll = 0
local last_incoming = 0
local last_report = nil
local startup_followup_at = nil
local master = nil
local seen_nonces = {}
local stem = nil
local last_state = nil
local shutdown_pending_until = 0
local function read(path)
    local file = io.open(path, 'rb')
    if not file then return nil end
    local value = file:read(4097)
    file:close()
    if not value or #value > 4096 then return nil end
    return value:gsub('\r', '')
end
local function write(path, value)
    local temporary = path .. '.next'
    local file = io.open(temporary, 'wb')
    if not file then return false end
    file:write(value)
    file:close()
    os.remove(path)
    return os.rename(temporary, path) ~= nil
end
local function identity()
    local info = windower.ffxi.get_info()
    local player = windower.ffxi.get_player()
    if not info or not info.logged_in or not player then return nil end
    local server = resources.servers and resources.servers[info.server]
    local world = server and server.en
    if not world or not player.name:match('^[A-Za-z]+$') or not world:match('^[A-Za-z]+$') then return nil end
    return player.name, world, player, info
end
local function capture_master(original)
    local character, world, player = identity()
    if not character then return end
    local ok, packet = pcall(packets.parse, 'incoming', original)
    if not ok or not packet or packet['Main Job'] ~= player.main_job_id then return end
    local level = packet['Master Level']
    if type(level) == 'number' and level >= 0 and level <= 50 and level % 1 == 0 then
        master = {character = character, world = world, job = player.main_job_id, level = level}
    end
end
local function capture(character, world, player, info, now)
    local v = player.vitals
    -- Ignore incomplete data while zoning; preserve the last usable report.
    if not v or not v.hp or not v.max_hp or not v.mp or not v.max_mp or not info.zone or info.zone == 0 then return end
    if v.max_hp <= 0 or v.hp < 0 or v.hp > v.max_hp or v.mp < 0 or v.max_mp < 0 or v.mp > v.max_mp then return end
    local zone = resources.zones and resources.zones[info.zone]
    local name = (zone and zone.en or ('Zone ' .. info.zone)):gsub('[\r\n]', ' '):sub(1, 256)
    -- Zero HP can be an incomplete startup sample, not death evidence.
    local safe_area = info.mog_house == true or info.zone == 280
    local dead = not safe_area and v.hp == 0 and (player.status == 2 or player.status == 3)
    last_state = table.concat({session, character, world, registered, now, v.hp, v.max_hp, v.mp, v.max_mp,
        dead and '1' or '0', info.zone, name}, '\n') .. '\n'
    write(root .. stem .. '.state', last_state .. '0\n')
    -- Read existing player data and cached server stats; no server requests.
    if not master or master.character ~= character or master.world ~= world or master.job ~= player.main_job_id then
        if windower.packets and windower.packets.last_incoming then
            local ok, original = pcall(windower.packets.last_incoming, 0x061)
            if ok and original then capture_master(original) end
        end
    end
    local main, sub = player.main_job, player.sub_job
    local main_level, sub_level = player.main_job_level, player.sub_job_level
    if type(main) == 'string' and main:match('^[A-Z][A-Z][A-Z]$') and
        type(sub) == 'string' and sub:match('^[A-Z][A-Z][A-Z]$') and
        type(main_level) == 'number' and main_level >= 1 and main_level <= 99 and main_level % 1 == 0 and
        type(sub_level) == 'number' and sub_level >= 0 and sub_level <= 99 and sub_level % 1 == 0 then
        local ml = master and master.character == character and master.world == world and master.job == player.main_job_id and master.level or -1
        write(root .. stem .. '.jobs', table.concat({session, character, world, registered, now,
            ml, main, main_level, sub, sub_level}, '\n') .. '\n')
    end
    -- Zone environment uses the same session and report time as the vitals.
    -- It is independent of shutdown state so older managers remain compatible.
    local function label(collection, id)
        local entry = collection and collection[id]
        return (entry and entry.en or 'Unknown'):gsub('[\r\n]', ' '):sub(1, 128)
    end
    if type(info.time) == 'number' and info.time >= 0 and info.time < 1440 and
        type(info.moon) == 'number' and info.moon >= 0 and info.moon <= 100 then
        write(root .. stem .. '.environment', table.concat({session, character, world, registered, now,
            info.zone, label(resources.weather, info.weather), math.floor(info.time),
            label(resources.days, info.day), label(resources.moon_phases, info.moon_phase), math.floor(info.moon)}, '\n') .. '\n')
    end
end
local function receipt(nonce, status)
    write(root .. stem .. '.receipt', nonce .. '\n' .. session .. '\n' .. status .. '\n')
end
local function poll()
    local now = os.time()
    if now <= last_poll then return end
    last_poll = now
    for nonce, expiry in pairs(seen_nonces) do if expiry < now then seen_nonces[nonce] = nil end end
    local character, world, player, info = identity()
    if not character then return end
    stem = world:lower() .. '_' .. character:lower()
    write(root .. stem .. '.status', session .. '\n' .. character .. '\n' .. world .. '\n' .. registered .. '\n' .. now .. '\n')
    write(root .. stem .. '.connection', session .. '\n' .. character .. '\n' .. world .. '\n' .. registered .. '\n' .. now .. '\n' .. last_incoming .. '\n')
    -- Stats and environment are sampled once per minute. Keep control polling
    -- and the lightweight health heartbeat independent of display reporting.
    if not last_report then startup_followup_at = now + 15 end
    if not last_report or now - last_report >= 60 or (startup_followup_at and now >= startup_followup_at) then
        capture(character, world, player, info, now)
        last_report = now
        if startup_followup_at and now >= startup_followup_at then startup_followup_at = nil end
    end
    local request_path = root .. stem .. '.request'
    local payload = read(request_path)
    if not payload then return end
    if not os.rename(request_path, request_path .. '.processing') then return end
    local processing = request_path .. '.processing'
    -- Claim and consume once before any action; never replay an interrupted request.
    os.remove(processing)
    local token, nonce, target, operation, expiry = payload:match('^([^\n]+)\n([^\n]+)\n([^\n]+)\n([^\n]+)\n(%d+)\n$')
    local expected = read(root .. 'control.token')
    expected = expected and expected:match('^([0-9a-f]+)\n?$')
    if not expected or #expected ~= 64 or token ~= expected then return end
    if not nonce or not nonce:match('^[0-9a-f]+$') or #nonce ~= 32 then return end
    if target ~= session or (operation ~= 'shutdown' and operation ~= 'cancel_shutdown') or not expiry or tonumber(expiry) < now or tonumber(expiry) > now + 30 then
        receipt(nonce, 'rejected')
        return
    end
    local prior = read(root .. stem .. '.receipt')
    if seen_nonces[nonce] or (prior and prior:sub(1, 33) == nonce .. '\n') then return end
    seen_nonces[nonce] = tonumber(expiry)
    -- Persist dispatch intent before issuing a command. A receipt does not mean the process exited.
    if not write(root .. stem .. '.receipt', nonce .. '\n' .. session .. '\n' .. 'dispatching\n') then return end
    local command = 'input /shutdown'
    if operation == 'cancel_shutdown' then
        -- Cancel the logout countdown itself, rather than just closing a menu.
        -- Only allow cancellation of a shutdown requested by this addon.
        if now > shutdown_pending_until then receipt(nonce, 'dispatched'); return end
        shutdown_pending_until = 0
        command = 'input /shutdown off'
    else
        shutdown_pending_until = now + 45
    end
    local ok = pcall(windower.send_command, command)
    receipt(nonce, ok and 'dispatched' or 'error')
end
local function clear()
    if not stem then return end
    local value = read(root .. stem .. '.status')
    if value and value:sub(1, #session + 1) == session .. '\n' then
        os.remove(root .. stem .. '.status')
        os.remove(root .. stem .. '.connection')
    end
end
windower.register_event('prerender', poll)
local function report_change()
    local character, world, player, info = identity()
    if not character then return end
    stem = world:lower() .. '_' .. character:lower()
    local now = os.time()
    capture(character, world, player, info, now)
    last_report = now
end
local function startup_report()
    last_report = nil
    startup_followup_at = nil
    last_poll = 0
    poll()
end
windower.register_event('load', startup_report)
windower.register_event('login', startup_report)
windower.register_event('zone change', startup_report)
windower.register_event('status change', report_change)
windower.register_event('logout', function()
    report_change()
    -- Player data may already be unavailable here. Keep the last observed vitals/zone.
    local value = stem and read(root .. stem .. '.state')
    if last_state and value and value:sub(1, #session + 1) == session .. '\n' then
        write(root .. stem .. '.state', last_state .. os.time() .. '\n')
    end
    clear()
    last_state = nil
    last_report = nil
    startup_followup_at = nil
    master = nil
    shutdown_pending_until = 0
    stem = nil
    registered = os.time()
    session = tostring(registered) .. '_' .. tostring({}):gsub('[^0-9a-f]', '') .. '_' .. tostring(math.random(100000, 999999999))
end)
windower.register_event('unload', clear)

-- Observe original server system messages only; never persist player chat or injected packets.
windower.register_event('incoming chunk', function(id, original, modified, injected, blocked)
    if injected or blocked then return end
    last_incoming = os.time()
    if id == 0x061 then capture_master(original); return end
    if id ~= 0x017 then return end
    local ok, packet = pcall(packets.parse, 'incoming', original)
    if not ok or not packet or (packet.Mode ~= 6 and packet.Mode ~= 7) then return end
    local message = packet.Message
    if type(message) ~= 'string' then return end
    message = message:gsub('[%z\1-\31\127-\255]', ' '):gsub('%s+', ' '):sub(1, 512)
    local lower = message:lower()
    if not lower:find('maintenance', 1, true) then return end
    -- Completion/cancellation text cannot safely infer a start/end schedule.
    if lower:find('completed', 1, true) or lower:find('concluded', 1, true) or lower:find('cancelled', 1, true) or lower:find('canceled', 1, true) then return end
    local character, world = identity()
    if not character then return end
    local alert_stem = world:lower() .. '_' .. character:lower()
    write(root .. alert_stem .. '.alert', session .. '\n' .. character .. '\n' .. world .. '\n' .. registered .. '\n' .. os.time() .. '\nincoming-017-system\n' .. message .. '\n')
end)

-- Loading while already logged in should publish immediately, before prerender.
poll()

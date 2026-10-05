-- SpellActionBar core: engine-free logic (bindings store, wheel index math).
-- Kept free of UE4SS globals so it can be exercised outside the game.

local Core = {}

-- bindings: { [slot] = objectPath }, slot is 1..maxSlots

function Core.parseBindings(text, maxSlots)
    local bindings = {}
    for line in (text or ""):gmatch("[^\r\n]+") do
        local slot, path = line:match("^%s*(%d+)%s*=%s*(%S.-)%s*$")
        slot = tonumber(slot)
        if slot and slot >= 1 and slot <= maxSlots and path:sub(1, 1) == "/" then
            bindings[slot] = path
        end
    end
    return bindings
end

function Core.serializeBindings(bindings, maxSlots)
    local lines = { "-- SpellActionBar bindings: <slot>=<spell data object path>. Edit freely while the game is closed." }
    for slot = 1, maxSlots do
        if bindings[slot] then lines[#lines + 1] = slot .. "=" .. bindings[slot] end
    end
    return table.concat(lines, "\n") .. "\n"
end

-- Binds `path` to `slot`. A spell lives in at most one slot, so any previous
-- slot holding it is cleared. Pressing the chord for the slot that already
-- holds this spell clears that slot instead (toggle). Returns "bound",
-- "moved" or "unbound", plus the slot the spell previously occupied (or nil).
function Core.bind(bindings, slot, path, maxSlots)
    if bindings[slot] == path then
        bindings[slot] = nil
        return "unbound", slot
    end
    local previous
    for other = 1, maxSlots do
        if bindings[other] == path then
            bindings[other] = nil
            previous = other
        end
    end
    bindings[slot] = path
    return previous and "moved" or "bound", previous
end

-- Position of a wheel entry. `index` is 0-based into SelectedSpells.
function Core.wheelPosition(index, slotsPerRadial)
    if not slotsPerRadial or slotsPerRadial < 1 then return nil end
    return index // slotsPerRadial, index % slotsPerRadial
end

-- Extracts "/Game/x/Y.Y" from a UObject:GetFullName() string ("Class /Game/x/Y.Y").
function Core.objectPath(fullName)
    return fullName and fullName:match("^%S+%s+(/%S+)$") or nil
end

-- "/Game/x/Y" -> "/Game/x/Y.Y"; object paths pass through.
function Core.toObjectPath(path)
    if path:find(".", 1, true) then return path end
    local leaf = path:match("([^/]+)$")
    return leaf and (path .. "." .. leaf) or nil
end

function Core.chordLabel(slot)
    return "Alt+" .. slot
end

-- Finds a spell's 0-based wheel index. `sameSpell(entry)` decides a match, so
-- stale/invalid array entries can be skipped by the caller.
function Core.findOnWheel(count, entryAt, sameSpell)
    for i = 1, count do
        local entry = entryAt(i)
        if entry ~= nil and sameSpell(entry) then return i - 1 end
    end
    return nil
end

return Core

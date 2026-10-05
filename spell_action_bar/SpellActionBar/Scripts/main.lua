---@diagnostic disable: undefined-global

-- SpellActionBar
-- A second hotbar under the vanilla 1-8 bar for wheel spells, driven by Alt+1..8 or numpad 1..8.
--
--   * Open the spell wheel (or the spellbook's wheel), point at a spell, press Alt+N (or
--     numpad N): the spell is bound to slot N and its icon appears on the bar. Pressing the
--     same key on that spell again clears the slot; binding it to another slot moves it.
--   * With the wheel closed, Alt+N / numpad N selects the spell bound to slot N.
--   * While Alt is held the vanilla "select slot N" mappings are switched off, so
--     Alt+N does not also change your held item. (Ctrl is dodge and Shift is sprint.)
--
-- Bindings persist in bindings.txt beside this script. The first run writes
-- api_dump.txt (class/function signatures) and, with Diagnostics on, every call to the
-- spell-selection functions goes to trace.txt. See README for what is unverified.

local ModName = "[SpellActionBar] "

local Config = {
    Slots = 8,                  -- slots 1..Slots (max 8: the vanilla bar has 8)
    SlotSize = 60,              -- px per slot at HUD scale 1.0 (match the vanilla slots)
    SlotGap = 6,                -- px between slots at HUD scale 1.0
    Left = 68,                  -- the vanilla hotbar is top-left: this bar's left edge, at HUD scale 1.0
    Top = 128,                  -- and its top edge: just clear of the vanilla bar's bottom edge
    ZOrder = 5,
    ShowEmptySlots = true,      -- false: a slot only appears once a spell is bound to it
    SuppressVanillaSlots = true,-- disable vanilla 1-8 mappings while Alt is held
    Verbose = true,             -- log how each wheel hover / activation was resolved
    Diagnostics = true,         -- write api_dump.txt once, trace spell-selection calls
    PollMs = 30,                -- input poll; the bar/state work runs every SlowEvery-th poll
    SlowEvery = 7,
}

local UEHelpers = require("UEHelpers")
local Core = require("core")

local ScriptDir = (debug.getinfo(1, "S").source:gsub("^@", "")):match("^(.*[\\/])") or ""
local BindingsFile = ScriptDir .. "bindings.txt"
local DumpFile = ScriptDir .. "api_dump.txt"
local TraceFile = ScriptDir .. "trace.txt"

-- helpers ---------------------------------------------------------------

local function log(msg) print(ModName .. msg .. "\n") end
local function vlog(msg) if Config.Verbose then log(msg) end end

local function valid(obj)
    if obj == nil then return false end
    local ok, result = pcall(function() return obj:IsValid() end)
    return ok and result == true
end

-- Breadcrumb: the step about to run goes to crumb.txt first. If the game dies inside
-- UE4SS, the file names the last step started.
local CrumbFile = ScriptDir .. "crumb.txt"
local lastCrumb
local function crumb(step)
    if step == lastCrumb then return end
    lastCrumb = step
    local f = io.open(CrumbFile, "w")
    if f then f:write(step .. "\n"); f:close() end
end

-- Runs fn; on error logs `what` with the message and returns nil.
local function try(what, fn, ...)
    crumb(what)
    local ok, a, b, c = pcall(fn, ...)
    if not ok then log(what .. " failed: " .. tostring(a)); return nil end
    return a, b, c
end

local function sameObject(a, b)
    return valid(a) and valid(b) and a:GetAddress() == b:GetAddress()
end

local function appendFile(path, text)
    local f = io.open(path, "a")
    if f then f:write(text); f:close() end
end

-- persistence -----------------------------------------------------------

local Bindings = {}
local BindingsDirty = true

local function loadBindings()
    local f = io.open(BindingsFile, "r")
    if not f then return end
    Bindings = Core.parseBindings(f:read("*a"), Config.Slots)
    f:close()
end

local function saveBindings()
    local f = io.open(BindingsFile, "w")
    if not f then log("cannot write " .. BindingsFile); return end
    f:write(Core.serializeBindings(Bindings, Config.Slots))
    f:close()
end

-- game objects ----------------------------------------------------------

local function getPC()
    local pc = UEHelpers.GetPlayerController()
    return valid(pc) and pc or nil
end

local function getComponent()
    local all = FindAllOf("SpellcastingComponent")
    if not all then return nil end
    local pc = getPC()
    local fallback
    for _, c in ipairs(all) do
        if valid(c) then
            fallback = fallback or c
            if pc and sameObject(c.PlayerController, pc) then return c end
        end
    end
    return fallback
end

local function loadSpell(path)
    local objectPath = Core.toObjectPath(path)
    if not objectPath then return nil end
    local spell = StaticFindObject(objectPath)
    if not valid(spell) then
        pcall(LoadAsset, objectPath)
        spell = StaticFindObject(objectPath)
    end
    return valid(spell) and spell or nil
end

local function spellName(spell)
    local ok, name = pcall(function() return spell.SpellDisplayName:ToString() end)
    if ok and name and name ~= "" then return name end
    return Core.objectPath(spell:GetFullName()) or "?"
end

-- SpellIcon is a soft object reference; the exact Lua surface for it is not
-- documented for this UE4SS build, so try each way of turning it into a texture.
local function iconTexture(spell)
    local soft = spell.SpellIcon
    if soft == nil then return nil end
    local attempts = {
        function() return soft:Get() end,
        function() return soft:LoadSynchronous() end,
        function()
            local lib = StaticFindObject("/Script/Engine.Default__KismetSystemLibrary")
            return lib:LoadAsset_Blocking(soft)
        end,
        function()
            local path = soft.AssetPathName and soft.AssetPathName:ToString() or soft:ToString()
            if not path or path == "" or path == "None" then return nil end
            local object = StaticFindObject(path)
            if not valid(object) then pcall(LoadAsset, path); object = StaticFindObject(path) end
            return object
        end,
    }
    for i, attempt in ipairs(attempts) do
        local ok, tex = pcall(attempt)
        if ok and valid(tex) then return tex, i end
    end
    return nil
end

-- spell wheel -----------------------------------------------------------

-- A widget is really showing only if it and every ancestor (through nested
-- user widgets too) is visible.
local function chainVisible(widget)
    local w = widget
    for _ = 1, 16 do
        local ok, shown = pcall(function() return w:IsVisible() end)
        if not ok or not shown then return false end
        local okParent, parent = pcall(function() return w:GetParent() end)
        if okParent and valid(parent) then
            w = parent
        else
            -- Root of a nested user widget: the owning user widget is WidgetTree's outer.
            local okOwner, owner = pcall(function() return w:GetOuter():GetOuter() end)
            if okOwner and valid(owner) and owner:IsA("/Script/UMG.UserWidget") then w = owner else return true end
        end
    end
    return true
end

local function openRadial()
    local all = FindAllOf("SpellcastingRadialBase")
    if not all then return nil end
    for _, radial in ipairs(all) do
        if valid(radial) and chainVisible(radial) then return radial end
    end
    return nil
end

local function sliceTexture(slice)
    local ok, tex = pcall(function() return slice.SliceIcon.Brush.ResourceObject end)
    return ok and valid(tex) and tex or nil
end

-- Returns spellData, nil or nil, reason.
local function hoveredSpell()
    local radial = openRadial()
    if not radial then return nil, "the spell wheel is not open" end
    local comp = getComponent()
    if not comp then return nil, "no SpellcastingComponent found" end

    local section = radial.CachedSectionId
    local slices = radial.Slices
    if not slices or section >= slices:GetArrayNum() then
        return nil, "no wheel slice under the pointer (section " .. tostring(section) .. ")"
    end
    local slice = slices[section + 1]
    if not valid(slice) then return nil, "wheel slice " .. section .. " is not valid" end

    local slotNum = slice.SpellSlotNum
    local per = comp.NumSpellSlotsPerRadial
    local selected = comp.SelectedSpells
    local total = selected:GetArrayNum()

    -- SpellSlotNum may be per-radial or global; consider the same slot on every page.
    local candidates = {}
    if slotNum >= per then
        candidates[1] = slotNum
    else
        for index = slotNum, total - 1, per do candidates[#candidates + 1] = index end
    end
    local found = {}
    for _, index in ipairs(candidates) do
        local spell = index < total and selected[index + 1] or nil
        if valid(spell) then found[#found + 1] = { index = index, spell = spell } end
    end
    if #found == 0 then
        return nil, "wheel slot " .. slotNum .. " is empty (SelectedSpells has " .. total .. " entries)"
    end

    local pick = found[1]
    if #found > 1 then
        local shown = sliceTexture(slice)
        for _, c in ipairs(found) do
            if shown and sameObject(iconTexture(c.spell), shown) then pick = c; break end
        end
        vlog(("slot %d exists on %d wheel pages; picked index %d by %s"):format(
            slotNum, #found, pick.index, shown and "icon match" or "first page (icon unreadable)"))
    end
    vlog(("hovered: section %d, slot %d, SelectedSpells[%d] = %s"):format(
        section, slotNum, pick.index, spellName(pick.spell)))
    return pick.spell
end

-- vanilla bar suppression -----------------------------------------------

local SlotActionPattern = "^IA_Inventory_QuickAccess_SelectSlot%d$"
local Suppressed = false
local SavedMappings = {} -- { { mapping = struct, key = "One" } }

-- Ctrl is dodge and Shift is sprint in this game, so the modifier is Alt.
local function modifierDown(pc)
    local ok, down = pcall(function() return pc:IsInputKeyDown({ KeyName = FName("LeftAlt") }) end)
    if ok and down then return true end
    ok, down = pcall(function() return pc:IsInputKeyDown({ KeyName = FName("RightAlt") }) end)
    return ok and down == true
end

local function rebuildMappings()
    local sub = FindFirstOf("EnhancedInputLocalPlayerSubsystem")
    if not valid(sub) then return false end
    -- bIgnoreAllPressedKeysUntilRelease must be off or movement keys die until released.
    return pcall(function()
        sub:RequestRebuildControlMappings({
            bIgnoreAllPressedKeysUntilRelease = false,
            bForceImmediately = true,
            bNotifyUserSettings = false,
        }, 1)
    end)
end

local function setVanillaSlots(off)
    if off == Suppressed then return end
    if off then
        SavedMappings = {}
        for _, imc in ipairs(FindAllOf("InputMappingContext") or {}) do
            if valid(imc) then
                local maps = imc.Mappings
                for i = 1, maps:GetArrayNum() do
                    local mapping = maps[i]
                    local action = mapping.Action
                    if valid(action) and action:GetFName():ToString():match(SlotActionPattern) then
                        local keyName = mapping.Key.KeyName:ToString()
                        local ok = pcall(function() mapping.Key.KeyName = FName("None") end)
                        if ok then SavedMappings[#SavedMappings + 1] = { mapping = mapping, key = keyName } end
                    end
                end
            end
        end
        if #SavedMappings == 0 then
            log("no vanilla quick-slot mappings found to disable; Alt+N will also select the vanilla slot")
        else
            vlog("disabled " .. #SavedMappings .. " vanilla slot mappings")
        end
    else
        for _, saved in ipairs(SavedMappings) do
            pcall(function() saved.mapping.Key.KeyName = FName(saved.key) end)
        end
        vlog("restored " .. #SavedMappings .. " vanilla slot mappings")
        SavedMappings = {}
    end
    Suppressed = off
    rebuildMappings()
end

-- bar widget ------------------------------------------------------------

local Bar = { host = nil, slots = {}, visible = nil, applied = nil }

-- Vanilla slot look: its own slot-background texture (T_Common_ItemSlotsBackground) and
-- Amiri font, tinted purple instead of the vanilla dark grey.
local FrameColor = { R = 0.66, G = 0.48, B = 0.90, A = 0.85 } -- light violet rim
local BackTint = { R = 0.50, G = 0.26, B = 0.78, A = 1.0 }    -- tint over the vanilla texture
local BackPlain = { R = 0.20, G = 0.09, B = 0.32, A = 0.88 }  -- used if the texture can't load
local BackTexturePath = "/Game/Art/UI/Common/T_Common_ItemSlotsBackground.T_Common_ItemSlotsBackground"
local FontPath = "/Game/UI/Fonts/Amiri-Regular_Font.Amiri-Regular_Font"

local function loadByPath(path)
    local object = StaticFindObject(path)
    if not valid(object) then pcall(LoadAsset, path); object = StaticFindObject(path) end
    return valid(object) and object or nil
end

local HostClassPaths = {
    "/Game/UI/Common/WBP_IconImage.WBP_IconImage_C",
    "/Game/UI/Common/WBP_DomTextBlock.WBP_DomTextBlock_C",
}

local function findHostClass()
    for _, path in ipairs(HostClassPaths) do
        local class = StaticFindObject(path)
        if not valid(class) then pcall(LoadAsset, path); class = StaticFindObject(path) end
        if valid(class) then return class, path end
    end
    return nil
end

-- Sizes are placeholders: applyMetrics() resizes everything to match the vanilla bar.
local function buildBar(pc)
    local hostClass, hostPath = findHostClass()
    if not hostClass then log("no concrete widget class available to host the bar"); return false end
    local lib = StaticFindObject("/Script/UMG.Default__WidgetBlueprintLibrary")
    local host = lib:Create(pc, hostClass, pc)
    if not valid(host) then log("WidgetBlueprintLibrary:Create failed for " .. hostPath); return false end
    local tree = host.WidgetTree
    if not valid(tree) then log("host widget has no WidgetTree"); return false end

    local function make(name)
        return StaticConstructObject(StaticFindObject("/Script/UMG." .. name), tree)
    end

    local backTexture = loadByPath(BackTexturePath)
    Bar.fontObj = loadByPath(FontPath)
    if not backTexture then log("vanilla slot background texture not loadable; using a plain purple backing") end
    local row = make("HorizontalBox")
    local slots = {}
    for i = 1, Config.Slots do
        local size = make("SizeBox")
        local overlay = make("Overlay")
        size:SetContent(overlay)

        local frame = make("Image")
        frame:SetColorAndOpacity(FrameColor)
        local back = make("Image")
        if backTexture then
            back:SetBrushFromTexture(backTexture, false)
            back:SetColorAndOpacity(BackTint)
        else
            back:SetColorAndOpacity(BackPlain)
        end
        local icon = make("Image")
        local label = make("TextBlock")
        label:SetText(FText(Core.chordLabel(i)))
        pcall(function()
            label:SetShadowOffset({ X = 1, Y = 1 })
            label:SetShadowColorAndOpacity({ R = 0, G = 0, B = 0, A = 0.9 })
        end)

        overlay:AddChildToOverlay(frame)
        local backSlot = overlay:AddChildToOverlay(back)
        local iconSlot = overlay:AddChildToOverlay(icon)
        local labelSlot = overlay:AddChildToOverlay(label)
        pcall(function()
            labelSlot:SetHorizontalAlignment(1) -- left
            labelSlot:SetVerticalAlignment(1)   -- top
        end)

        local rowSlot = row:AddChildToHorizontalBox(size)
        slots[i] = { size = size, icon = icon, label = label, backSlot = backSlot,
                     iconSlot = iconSlot, labelSlot = labelSlot, rowSlot = rowSlot }
    end

    tree.RootWidget = row
    host:AddToViewport(Config.ZOrder)
    host:SetVisibility(3) -- HitTestInvisible: never steal clicks from the game
    -- Scale about the top-left corner so the bar stays seated under the vanilla bar.
    pcall(function() host:SetRenderTransformPivot({ X = 0, Y = 0 }) end)
    Bar.host, Bar.slots, Bar.visible, Bar.applied = host, slots, true, nil
    log("bar created (host " .. hostPath .. ")")
    return true
end

-- The HUD is up when a vanilla quick-access bar is showing. One IsVisible call per bar and
-- no geometry: GetCachedGeometry / LocalToViewport (struct-returning Slate calls) were
-- the calls running when the game crashed inside UE4SS, so layout no longer measures anything.
local function hudVisible()
    for _, bar in ipairs(FindAllOf("QuickAccessBarBase") or {}) do
        if valid(bar) then
            local ok, shown = pcall(function() return bar:IsVisible() end)
            if ok and shown then return true end
        end
    end
    return false
end

-- The in-game HUD scale (Settings > Accessibility), DominionAccessibilitySettings.HudScale.
local function hudScale()
    local ok, scale = pcall(function()
        local settings = FindFirstOf("DominionAccessibilitySettings")
        if not valid(settings) then
            settings = StaticFindObject("/Script/Dominion.Default__DominionAccessibilitySettings")
        end
        return valid(settings) and settings.HudScale or nil
    end)
    if ok and type(scale) == "number" and scale >= 0.25 and scale <= 4 then return scale end
    return 1
end

-- Sizes the bar for HUD scale `k` and seats it under the vanilla top-left bar.
local function applyLayout(k)
    local key = ("%.3f"):format(k)
    if key == Bar.applied then return end
    local slot = Config.SlotSize
    local half = Config.SlotGap / 2
    local inset = math.max(1, slot * 0.04)       -- frame thickness
    local iconInset = slot * 0.10
    for i = 1, Config.Slots do
        local ui = Bar.slots[i]
        ui.size:SetWidthOverride(slot)
        ui.size:SetHeightOverride(slot)
        pcall(function()
            ui.rowSlot:SetPadding({ Left = half, Top = 0, Right = half, Bottom = 0 })
            ui.backSlot:SetPadding({ Left = inset, Top = inset, Right = inset, Bottom = inset })
            ui.iconSlot:SetPadding({ Left = iconInset, Top = iconInset, Right = iconInset, Bottom = iconInset })
            ui.labelSlot:SetPadding({ Left = inset + slot * 0.05, Top = inset, Right = 0, Bottom = 0 })
        end)
        pcall(function()
            local font = ui.label.Font
            font.Size = math.max(8, math.floor(slot * 0.24))
            if Bar.fontObj then font.FontObject = Bar.fontObj end
            ui.label:SetFont(font)
        end)
    end
    local host = Bar.host
    host:SetRenderScale({ X = k, Y = k })
    host:SetAnchorsInViewport({ Minimum = { X = 0, Y = 0 }, Maximum = { X = 0, Y = 0 } })
    host:SetAlignmentInViewport({ X = 0, Y = 0 })
    host:SetPositionInViewport({ X = Config.Left * k, Y = Config.Top * k }, false)
    Bar.applied = key
    vlog(("layout: HUD scale %.3f, slot %d px, at %.0f,%.0f"):format(k, slot, Config.Left * k, Config.Top * k))
    -- One-shot state report: plain property reads, so a bar that exists but doesn't draw
    -- can be told apart from one that never reached the viewport.
    pcall(function()
        local tree = host.WidgetTree
        log(("state: inViewport=%s visibility=%s root=%s"):format(
            tostring(host:IsInViewport()), tostring(host:GetVisibility()),
            valid(tree.RootWidget) and tree.RootWidget:GetFullName() or "NONE"))
    end)
end

local function refreshBar()
    for i = 1, Config.Slots do
        local ui = Bar.slots[i]
        local spell = Bindings[i] and loadSpell(Bindings[i]) or nil
        local show = spell ~= nil
        if spell then
            local tex = iconTexture(spell)
            if tex then
                ui.icon:SetBrushFromTexture(tex, false)
                ui.icon:SetColorAndOpacity({ R = 1, G = 1, B = 1, A = 1 })
                ui.icon:SetVisibility(3)
            else
                ui.icon:SetVisibility(1)
                log("no icon texture for " .. spellName(spell) .. "; slot " .. i .. " shows its key label only")
            end
        else
            ui.icon:SetVisibility(1)
        end
        ui.size:SetVisibility((show or Config.ShowEmptySlots) and 3 or 1)
    end
    BindingsDirty = false
end

local function syncBar()
    local pc = getPC()
    if not pc then Bar.host = nil; return end
    if not valid(Bar.host) then
        Bar.host = nil
        if not try("building the bar", buildBar, pc) then return end
        BindingsDirty = true
    end
    crumb("hudVisible")
    local want = hudVisible()
    if want ~= Bar.visible then
        crumb("host visibility")
        Bar.host:SetVisibility(want and 3 or 1)
        Bar.visible = want
    end
    if want then
        crumb("hudScale")
        local k = hudScale()
        try("applying layout", applyLayout, k)
    end
    if BindingsDirty then crumb("refreshBar"); refreshBar() end
end

-- activation ------------------------------------------------------------

local function currentlySelected(comp)
    local ok, spell = pcall(function() return comp:GetCurrentlySelectedSpellData() end)
    return ok and spell or nil
end

local function paramCount(ufunction)
    local n = 0
    pcall(function() ufunction:ForEachProperty(function() n = n + 1 end) end)
    return n
end

-- Selects `spell` the way the wheel does, using the signatures in api_dump.txt:
-- Server_NotifySpellRadialSelected(RadialIndex) picks the wheel page, then the HUD wheel
-- widget is driven like a player would (RadialMenuBase:HighlightSlice(SliceId) then
-- :SelectSlice()). Afterwards GetCurrentlySelectedSpellData must report the spell;
-- anything else is logged with what was seen. Client_NotifySelectedSpell(SpellData, slot)
-- is the server's confirmation back to the client, so it is traced, never called.
local function activate(slot)
    local path = Bindings[slot]
    if not path then vlog("slot " .. slot .. " is empty"); return end
    local spell = loadSpell(path)
    if not spell then log("slot " .. slot .. ": spell asset not loadable: " .. path); return end
    local comp = getComponent()
    if not comp then log("no SpellcastingComponent; are you in a world?"); return end

    local selected = comp.SelectedSpells
    local index = Core.findOnWheel(selected:GetArrayNum(), function(i) return selected[i] end,
        function(entry) return sameObject(entry, spell) end)
    if not index then
        log(("slot %d: %s is no longer on your spell wheel"):format(slot, spellName(spell)))
        return
    end
    local radial, inRadial = Core.wheelPosition(index, comp.NumSpellSlotsPerRadial)
    vlog(("activating %s: wheel index %d (radial %d, slot %d)"):format(spellName(spell), index, radial, inRadial))

    local pageFn = StaticFindObject("/Script/Dominion.SpellcastingComponent:Server_NotifySpellRadialSelected")
    if not valid(pageFn) or paramCount(pageFn) ~= 1 then
        log("SpellcastingComponent:Server_NotifySpellRadialSelected missing or changed (see api_dump.txt)")
        return
    end
    local wheel
    for _, r in ipairs(FindAllOf("SpellcastingRadialBase") or {}) do
        if valid(r) and not r.bIsSpellbookInstance then wheel = r; break end
    end
    if not wheel then log("HUD spell wheel widget not found"); return end
    local ok, err = pcall(function()
        comp:Server_NotifySpellRadialSelected(radial)
        wheel:HighlightSlice(inRadial)
        wheel:SelectSlice()
    end)
    if not ok then log("selecting the slice failed: " .. tostring(err)); return end

    local now = currentlySelected(comp)
    if sameObject(now, spell) then
        vlog("selected " .. spellName(spell))
    elseif now == nil then
        log("selection calls ran but GetCurrentlySelectedSpellData is unavailable; cannot confirm " .. spellName(spell))
    else
        log(("selection calls ran but the selected spell is %s, not %s"):format(spellName(now), spellName(spell)))
    end
end

-- assignment ------------------------------------------------------------

local function assign(slot)
    local spell, reason = hoveredSpell()
    if not spell then log("cannot bind slot " .. slot .. ": " .. reason); return end
    local path = Core.objectPath(spell:GetFullName())
    if not path then log("unexpected spell name " .. spell:GetFullName()); return end
    local result, previous = Core.bind(Bindings, slot, path, Config.Slots)
    saveBindings()
    BindingsDirty = true
    local name = spellName(spell)
    if result == "unbound" then log(("slot %d cleared (%s)"):format(slot, name))
    elseif result == "moved" then log(("%s moved from slot %d to slot %d"):format(name, previous, slot))
    else log(("%s bound to slot %d"):format(name, slot)) end
end

local function onChord(slot)
    if openRadial() then assign(slot) else activate(slot) end
end

-- diagnostics -----------------------------------------------------------

local DumpClasses = {
    "SpellcastingComponent", "SpellcastingUIAPI", "SpellcastingMainPanel", "SpellcastingRadialBase",
    "SpellcastingRadialSlice", "RadialMenuBase", "QuickAccessBarBase", "QuickAccessBarSlotBase",
    "SpellSlotBase", "UtilitySpellData", "UtilitySpellDataSubsystem", "DomSpellBook",
}
local StopAt = { "/Script/CoreUObject", "/Script/Engine", "/Script/UMG", "/Script/Slate", "/Script/CommonUI" }

local function dumpClass(out, class, depth)
    out[#out + 1] = "  class " .. class:GetFullName()
    pcall(function()
        class:ForEachFunction(function(fn)
            local params = {}
            pcall(function()
                fn:ForEachProperty(function(p)
                    params[#params + 1] = p:GetFName():ToString() .. ":" .. p:GetClass():GetFName():ToString()
                end)
            end)
            out[#out + 1] = "    fn " .. fn:GetFName():ToString() .. "(" .. table.concat(params, ", ") .. ")"
        end)
    end)
    if depth < 6 then
        local super = class:GetSuperStruct()
        if valid(super) then
            local name = super:GetFullName()
            for _, prefix in ipairs(StopAt) do if name:find(prefix, 1, true) then return end end
            dumpClass(out, super, depth + 1)
        end
    end
end

local function writeApiDump()
    if io.open(DumpFile, "r") then return end
    local out = { "== SpellActionBar API dump ==" }
    for _, name in ipairs(DumpClasses) do
        out[#out + 1] = "### " .. name
        local class = StaticFindObject("/Script/Dominion." .. name)
        if valid(class) then dumpClass(out, class, 0) else out[#out + 1] = "  not found" end
    end
    local f = io.open(DumpFile, "w")
    if f then f:write(table.concat(out, "\n") .. "\n"); f:close(); log("wrote " .. DumpFile) end
end

local TracedFunctions = {
    "Client_NotifySelectedSpell", "Client_NotifyAllSelectedSpells", "Server_NotifySpellRadialSelected",
    "Server_SwapSpells", "Server_UpdateSpellData", "Server_SpawnSpellPlacementVisualizer",
    "Client_CancelSpellcasting", "CancelSpellcasting", "OnPlaceSpellInputActionTriggered", "SelectSlice",
}

local function paramText(param)
    local ok, value = pcall(function() return param:get() end)
    if not ok then return "?" end
    if type(value) == "userdata" then
        local named, full = pcall(function() return value:GetFullName() end)
        return named and full or tostring(value)
    end
    return tostring(value)
end

local function installTrace()
    for _, name in ipairs(TracedFunctions) do
        local path = "/Script/Dominion.SpellcastingComponent:" .. name
        if name == "SelectSlice" then path = "/Script/Dominion.RadialMenuBase:SelectSlice" end
        local ok, err = pcall(RegisterHook, path, function(_, ...)
            local parts = {}
            for i, p in ipairs({ ... }) do parts[i] = paramText(p) end
            appendFile(TraceFile, ("%s(%s)\n"):format(name, table.concat(parts, ", ")))
        end)
        if not ok then log("cannot trace " .. name .. ": " .. tostring(err)) end
    end
end

-- startup ---------------------------------------------------------------

if Config.Slots > 8 or Config.Slots < 1 then Config.Slots = 8 end
loadBindings()

-- Slot N fires on Alt+N (number row) or on numpad N with no modifier. Polled, not
-- registered with RegisterKeyBind: its modifier callbacks never fired in game. Number-row
-- keys are only read while Alt is held; numpad keys are always read.
local DigitKeys = { "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight" }
local NumpadKeys = { "NumPadOne", "NumPadTwo", "NumPadThree", "NumPadFour", "NumPadFive", "NumPadSix", "NumPadSeven", "NumPadEight" }
local WasDown = { digit = {}, numpad = {} }
local function keyDown(pc, name)
    local ok, down = pcall(function() return pc:IsInputKeyDown({ KeyName = FName(name) }) end)
    return ok and down == true
end
local function pollKeys(pc, names, wasDown, use)
    for slot = 1, Config.Slots do
        local down = use and keyDown(pc, names[slot])
        if down and not wasDown[slot] then try("slot " .. slot, onChord, slot) end
        wasDown[slot] = down
    end
end

if Config.Diagnostics then installTrace() end

-- Crash guard: the UI code runs against engine internals that are unverified. The guard
-- file exists from the first time the vanilla bar is measured until 30 s later. If the
-- game dies in that window the file survives, and the next launch starts in safe mode
-- (no widgets, no input remapping) instead of crash-looping.
-- Delete crash_guard.txt to leave safe mode.
local GuardFile = ScriptDir .. "crash_guard.txt"
local SafeMode = io.open(GuardFile, "r") ~= nil
if SafeMode then
    log("SAFE MODE: the previous session crashed right after loading in. The UI and input changes are off. Delete " .. GuardFile .. " to try again.")
end
local guardAt = nil
local guardDone = false

local dumped = false
local ticks = 0
local cachedPC = nil
local function tick()
    ticks = ticks + 1
    local slow = ticks % Config.SlowEvery == 0
    if slow or not valid(cachedPC) then cachedPC = getPC() end
    local pc = cachedPC
    if not pc then return end
    if SafeMode then
        if slow and Config.Diagnostics and not dumped then dumped = true; try("api dump", writeApiDump) end
        return
    end

    local alt = modifierDown(pc)
    if Config.SuppressVanillaSlots then try("vanilla slot toggle", setVanillaSlots, alt) end
    pollKeys(pc, DigitKeys, WasDown.digit, alt)
    pollKeys(pc, NumpadKeys, WasDown.numpad, true)

    if not slow and not BindingsDirty then return end
    if Config.Diagnostics and not dumped then dumped = true; try("api dump", writeApiDump) end
    if not guardDone and not guardAt and #(FindAllOf("QuickAccessBarBase") or {}) > 0 then
        local f = io.open(GuardFile, "w")
        if f then f:write("armed; removed after 30s of stable play\n"); f:close() end
        guardAt = ticks
    end
    if guardAt and not guardDone and (ticks - guardAt) * Config.PollMs >= 30000 then
        os.remove(GuardFile)
        guardDone = true
    end
    try("bar sync", syncBar)
end

LoopAsync(Config.PollMs, function()
    ExecuteInGameThread(tick)
    return false
end)

log(("loaded: %d bindings, Alt+1..%d / numpad 1..%d"):format((function() local n = 0 for _ in pairs(Bindings) do n = n + 1 end return n end)(), Config.Slots, Config.Slots))

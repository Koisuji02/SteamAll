-- =====================================================================================
--  main.lua
--  La parte "backend" del plugin, che Millennium esegue fuori dall'interfaccia di Steam.
--  Al nostro plugin non serve: tutto il lavoro lo fa la parte nell'interfaccia
--  (.millennium/Dist/index.js). Qui diciamo solo a Millennium che siamo pronti.
--  (Lua è il linguaggio dei backend dei plugin Millennium: "--" inizia un commento.)
-- =====================================================================================

local millennium = require("millennium")

local function on_load()
  millennium.ready()
end

local function on_frontend_loaded()
end

local function on_unload()
end

return {
  on_load = on_load,
  on_frontend_loaded = on_frontend_loaded,
  on_unload = on_unload,
}

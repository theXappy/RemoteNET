-- declare the protocol
rnet_proto = Proto("rNET", "rNET Protocol")

-- create fields for the protocol
local f_magic = ProtoField.string("rNET.magic", "Magic")
local f_length = ProtoField.uint32("rNET.length", "Length")
local f_payload = ProtoField.bytes("rNET.payload", "Payload")
local f_is_diver_connection = ProtoField.bool("rNET.is_diver_connection", "Is Diver Connection")

local json_dis = Dissector.get("json")

-- add the fields to the protocol
rnet_proto.fields = { f_magic, f_length, f_payload, f_is_diver_connection }

diver_con_ports = {}

-- Function to add a port to the set (if it doesn't already exist)
function add_unique_port(ports_set, port)
    if not ports_set[port] then
        ports_set[port] = true
    end
end

function is_diver_connection(ports_set, port)
    return not not ports_set[port]
end


-- create a function to dissect the packets
function rnet_proto.dissector(buffer, pinfo, tree)
    -- set the protocol column
    pinfo.cols.protocol = rnet_proto.name

    -- create a subtree for the protocol
    local subtree = tree:add(rnet_proto, buffer())

    -- add the magic field
    local magic = buffer(0, 4):string()
    subtree:add(f_magic, buffer(0, 4)):append_text(" (" .. magic .. ")")

    -- add the length field
    local length = buffer(4, 4):le_uint()
    subtree:add(f_length, buffer(4, 4), length)

    -- add the payload field
    local payload_buff = buffer(8, length)
    subtree:add(f_payload, payload_buff)

    -- extract the payload as a string
    local payload = payload_buff:string()


    -- call the JSON dissector on the payload
    json_dis:call(payload_buff:tvb(), pinfo, tree)

    local src_port = pinfo.src_port
    local dst_port = pinfo.dst_port
	pinfo.cols.info = "[" .. src_port.. " -> " .. dst_port .. "]"

    -- set the packet length
    pinfo.cols.info:append("[rNET]")

-- extract the value of the UrlAbsolutePath field from the JSON payload
    local req_id = string.match(payload, '"RequestId":(%d+),')
    -- set the value of the Info column to the UrlAbsolutePath value
    if req_id then
        pinfo.cols.info:append("[ReqID: " .. req_id .. "]")
    end
	
    -- Print "flags" (shitty way to show Request/Response)
	pinfo.cols.info:append("[")
    local queryString = string.match(payload, '"QueryString":([{"]+)')
	pinfo.cols.info:append(queryString and "Q" or ".")
	local body = string.match(payload, '"Body":"(.*)[^\\]"')
	pinfo.cols.info:append(body and "B" or ".")
	pinfo.cols.info:append("]")
	


    -- extract the value of the UrlAbsolutePath field from the JSON payload
    local url_absolute_path = string.match(payload, '"UrlAbsolutePath":"([^"]+)"')
    -- set the value of the Info column to the UrlAbsolutePath value
    if url_absolute_path then
        pinfo.cols.info:append(" Path: " .. url_absolute_path)
    end
	
    if url_absolute_path == "/proxy_intro" then
		local body = string.match(payload, '"Body":"(.*)[^\\]"')
        if body then
            body = string.gsub(body, '\\"', '"') -- unescape the JSON string
            local role = string.match(body, '"role":"([^"]+)"')
            if role then
                pinfo.cols.info:append(", role: " .. role)
				if role == "diver" then
					pinfo.cols.info:append(" (!!!)")
					add_unique_port(diver_con_ports, pinfo.src_port)
				end
            end
            local status = string.match(body, '"status":"([^"]+)"')
            if status then
                pinfo.cols.info:append(", status: " .. status)
            end
		else
		
        end
    end

	local is_any_port_diver_port = is_diver_connection(diver_con_ports, pinfo.src_port) or is_diver_connection(diver_con_ports, pinfo.dst_port)
	subtree:add(f_is_diver_connection, buffer(0, 0), is_any_port_diver_port)

	return buffer:len()
end

-- create a heuristic dissector to detect the protocol
local function rnet_heuristic(buffer, pinfo, tree)
    -- check for the magic value
    if buffer:len() >= 8 and buffer(0, 4):string() == "rNET" then
        return rnet_proto.dissector(buffer, pinfo, tree)
    end
end

-- register the protocol as a heuristic dissector
local tcp_port_table = DissectorTable.get("tcp.port")
tcp_port_table:add(0, rnet_proto)
rnet_proto:register_heuristic("tcp", rnet_heuristic)
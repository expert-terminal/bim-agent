require 'socket'
require 'json'
require 'securerandom'
require 'fileutils'

module ExpertTerminalBimAgent
  module SketchUpBridge
    MAX_REQUEST = 262_144

    def self.start
      return if @server
      @token = SecureRandom.hex(32)
      @server = TCPServer.new('127.0.0.1', 0)
      @clients = {}
      folder = ENV['LOCALAPPDATA'] || File.join(Dir.home, '.bim-agent')
      folder = File.join(folder, 'BIM-agent') if ENV['LOCALAPPDATA']
      FileUtils.mkdir_p(folder)
      @config_path = File.join(folder, 'sketchup-bridge.json')
      File.write(@config_path, JSON.generate({ port: @server.addr[1], token: @token }))
      File.chmod(0600, @config_path) unless RUBY_PLATFORM.match?(/mswin|mingw|cygwin/i)
      @timer = UI.start_timer(0.1, true) { poll }
      at_exit { stop }
    rescue StandardError => error
      warn("BIM-agent SketchUp failed to start: #{error.message}")
    end

    def self.stop
      UI.stop_timer(@timer) if @timer
      @clients&.each_key { |socket| socket.close rescue nil }
      @server&.close
      File.delete(@config_path) if @config_path && File.exist?(@config_path)
      @timer = nil
      @server = nil
    end

    def self.poll
      loop do
        socket = @server.accept_nonblock(exception: false)
        break if socket == :wait_readable
        socket.close_on_exec = true if socket.respond_to?(:close_on_exec=)
        @clients[socket] = +''
      end
      @clients.keys.each do |socket|
        chunk = socket.read_nonblock(16_384, exception: false)
        if chunk == :wait_readable
          next
        elsif chunk.nil?
          close_client(socket)
          next
        end
        @clients[socket] << chunk
        if @clients[socket].bytesize > MAX_REQUEST
          respond(socket, { ok: false, error: 'Request too large.' })
        elsif @clients[socket].include?("\n")
          line = @clients[socket].split("\n", 2).first
          begin
            request = JSON.parse(line)
            raise 'Invalid bridge token.' unless request['token'] == @token
            respond(socket, { ok: true, result: dispatch(request) })
          rescue StandardError => error
            respond(socket, { ok: false, error: error.message })
          end
        end
      rescue EOFError
        close_client(socket)
      end
    rescue StandardError => error
      warn("BIM-agent SketchUp bridge error: #{error.message}")
    end

    def self.respond(socket, payload)
      socket.write(JSON.generate(payload) + "\n")
    ensure
      close_client(socket)
    end

    def self.close_client(socket)
      @clients.delete(socket)
      socket.close rescue nil
    end

    def self.dispatch(request)
      model = Sketchup.active_model
      raise 'Open a SketchUp model first.' unless model
      case request['action']
      when 'status'
        { title: model.title, path: model.path, version: Sketchup.version }
      when 'groups'
        limit = [[Integer(request.fetch('limit', 50)), 1].max, 200].min
        model.entities.grep(Sketchup::Group).first(limit).map do |group|
          { persistentId: group.persistent_id, name: group.name, layer: group.layer.name }
        end
      when 'create_extrusion'
        create_extrusion(model, request)
      when 'delete_group'
        delete_group(model, request)
      else
        raise "Unknown action: #{request['action']}"
      end
    end

    def self.create_extrusion(model, request)
      points = request.fetch('pointsMm')
      raise 'Provide 3 to 50 footprint points.' unless points.is_a?(Array) && points.length.between?(3, 50)
      xy = points.map do |point|
        raise 'Each point must be [xMm, yMm].' unless point.is_a?(Array) && point.length == 2
        point.map { |value| bounded_number(value) }
      end
      raise 'Footprint must contain at least 3 unique points.' if xy.uniq.length < 3
      z = bounded_number(request.fetch('zMm', 0))
      height = bounded_number(request.fetch('heightMm'))
      raise 'Height must be at least 1 mm.' if height < 1
      name = request.fetch('name', 'BIM-agent extrusion').to_s.strip[0, 100]
      approved = UI.messagebox("Create '#{name}' from #{xy.length} footprint points, #{height} mm high?", MB_YESNO) == IDYES
      return { approved: false } unless approved
      model.start_operation('BIM-agent: create extrusion', true)
      begin
        group = model.entities.add_group
        footprint = xy.map { |x, y| Geom::Point3d.new(x.mm, y.mm, z.mm) }
        face = group.entities.add_face(footprint)
        raise 'SketchUp could not create a face; check for a self-intersecting footprint.' unless face
        face.reverse! if face.normal.z < 0
        face.pushpull(height.mm)
        group.name = name
        group.set_attribute('BIM-agent', 'kind', 'extrusion')
        id = group.persistent_id
        model.commit_operation
        { approved: true, persistentId: id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.delete_group(model, request)
      id = Integer(request.fetch('persistentId'))
      group = model.find_entity_by_persistent_id(id)
      raise 'Group not found.' unless group.is_a?(Sketchup::Group)
      approved = UI.messagebox("Delete SketchUp group '#{group.name}' (#{id})?", MB_YESNO) == IDYES
      return { approved: false } unless approved
      model.start_operation('BIM-agent: delete group', true)
      begin
        group.erase!
        model.commit_operation
        { approved: true, persistentId: id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.bounded_number(value)
      number = Float(value)
      raise 'Coordinate or dimension outside ±1,000,000 mm.' unless number.finite? && number.abs <= 1_000_000
      number
    end
  end
end

ExpertTerminalBimAgent::SketchUpBridge.start

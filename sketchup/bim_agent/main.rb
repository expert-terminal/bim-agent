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
        begin
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
        rescue StandardError => error
          respond(socket, { ok: false, error: error.message }) if @clients.key?(socket)
        end
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
      when 'components'
        limit = [[Integer(request.fetch('limit', 50)), 1].max, 200].min
        model.entities.grep(Sketchup::ComponentInstance).first(limit).map do |component|
          { persistentId: component.persistent_id, name: component.definition.name, definitionId: component.definition.persistent_id, layer: component.layer.name }
        end
      when 'materials'
        model.materials.first(200).map { |material| { name: material.name, color: [material.color.red, material.color.green, material.color.blue], alpha: material.color.alpha } }
      when 'tags'
        model.layers.first(200).map { |tag| { name: tag.name, visible: tag.visible, locked: tag.locked? } }
      when 'create_extrusion'
        create_extrusion(model, request)
      when 'create_component'
        create_component(model, request)
      when 'create_box'
        create_box(model, request)
      when 'create_material'
        create_material(model, request)
      when 'assign_material'
        assign_material(model, request)
      when 'create_tag'
        create_tag(model, request)
      when 'assign_tag'
        assign_tag(model, request)
      when 'move_group'
        transform_group(model, request, :move)
      when 'rotate_group'
        transform_group(model, request, :rotate)
      when 'copy_group'
        copy_group(model, request)
      when 'delete_group'
        delete_group(model, request)
      else
        raise "Unknown action: #{request['action']}"
      end
    end

    def self.create_component(model, request)
      name = request.fetch('name', 'BIM-agent component').to_s.strip[0, 100]
      points = footprint_points(request.fetch('pointsMm'), request.fetch('zMm', 0))
      height = bounded_number(request.fetch('heightMm'))
      raise 'Height must be at least 1 mm.' if height < 1
      return { approved: false } unless UI.messagebox("Create component '#{name}' from #{points.length} footprint points, #{height} mm high?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create component', true)
      begin
        definition = model.definitions.add(name)
        face = definition.entities.add_face(points)
        raise 'SketchUp could not create a face; check footprint geometry.' unless face
        face.reverse! if face.normal.z < 0
        face.pushpull(height.mm)
        instance = model.entities.add_instance(definition, Geom::Transformation.new)
        model.commit_operation
        { approved: true, persistentId: instance.persistent_id, definitionId: definition.persistent_id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_box(model, request)
      name = request.fetch('name', 'BIM-agent box').to_s.strip[0, 100]
      origin = request.fetch('originMm').map { |v| bounded_number(v) }
      width = bounded_number(request.fetch('widthMm'))
      depth = bounded_number(request.fetch('depthMm'))
      height = bounded_number(request.fetch('heightMm'))
      raise 'Box dimensions must be positive.' unless [width, depth, height].all?(&:positive?)
      return { approved: false } unless UI.messagebox("Create '#{name}' box #{width} × #{depth} × #{height} mm?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create box', true)
      begin
        x, y, z = origin
        points = [[x,y,z],[x+width,y,z],[x+width,y+depth,z],[x,y+depth,z]].map { |p| Geom::Point3d.new(p[0].mm,p[1].mm,p[2].mm) }
        group = model.entities.add_group
        face = group.entities.add_face(points)
        raise 'SketchUp could not create box footprint.' unless face
        face.reverse! if face.normal.z < 0
        face.pushpull(height.mm)
        group.name = name
        id = group.persistent_id
        model.commit_operation
        { approved: true, persistentId: id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_material(model, request)
      name = request.fetch('name').to_s.strip[0, 100]
      rgb = request.fetch('rgb').map { |v| Integer(v) }
      alpha = Integer(request.fetch('alpha', 255))
      raise 'Material name cannot be empty.' if name.empty?
      raise 'RGB values must be 0–255.' unless rgb.length == 3 && rgb.all? { |v| v.between?(0, 255) }
      raise 'Alpha must be 0–255.' unless alpha.between?(0, 255)
      return { approved: false } unless UI.messagebox("Create or update SketchUp material '#{name}'?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create material', true)
      begin
        material = model.materials[name] || model.materials.add(name)
        material.color = Sketchup::Color.new(*rgb)
        material.alpha = alpha / 255.0
        model.commit_operation
        { approved: true, name: material.name }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.assign_material(model, request)
      entity = find_group(model, request.fetch('persistentId'))
      material = model.materials[request.fetch('materialName')] || raise('Material not found.')
      return { approved: false } unless UI.messagebox("Assign material '#{material.name}' to '#{entity.name}'?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: assign material', true)
      begin
        entity.material = material
        model.commit_operation
        { approved: true, persistentId: entity.persistent_id, material: material.name }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_tag(model, request)
      name = request.fetch('name').to_s.strip[0, 100]
      raise 'Tag name cannot be empty.' if name.empty?
      return { approved: false } unless UI.messagebox("Create SketchUp tag '#{name}'?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create tag', true)
      begin
        tag = model.layers[name] || model.layers.add(name)
        model.commit_operation
        { approved: true, name: tag.name }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.assign_tag(model, request)
      entity = find_group(model, request.fetch('persistentId'))
      tag = model.layers[request.fetch('tagName')] || raise('Tag not found.')
      return { approved: false } unless UI.messagebox("Assign tag '#{tag.name}' to '#{entity.name}'?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: assign tag', true)
      begin
        entity.layer = tag
        model.commit_operation
        { approved: true, persistentId: entity.persistent_id, tag: tag.name }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.transform_group(model, request, mode)
      entity = find_group(model, request.fetch('persistentId'))
      if mode == :move
        values = request.fetch('deltaMm').map { |v| bounded_number(v).mm }
        transform = Geom::Transformation.translation(Geom::Vector3d.new(*values))
        prompt = "Move '#{entity.name}' by #{request['deltaMm'].join(', ')} mm?"
      else
        values = request.fetch('pivotMm').map { |v| bounded_number(v).mm }
        degrees = Float(request.fetch('degrees'))
        raise 'Rotation must be finite and within ±36000 degrees.' unless degrees.finite? && degrees.abs <= 36_000
        transform = Geom::Transformation.rotation(Geom::Point3d.new(*values), Z_AXIS, degrees.degrees)
        prompt = "Rotate '#{entity.name}' around vertical axis by #{degrees}°?"
      end
      return { approved: false } unless UI.messagebox(prompt, MB_YESNO) == IDYES
      model.start_operation("BIM-agent: #{mode} group", true)
      begin
        entity.transform!(transform)
        model.commit_operation
        { approved: true, persistentId: entity.persistent_id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.copy_group(model, request)
      source = find_group(model, request.fetch('persistentId'))
      values = request.fetch('deltaMm').map { |v| bounded_number(v).mm }
      return { approved: false } unless UI.messagebox("Copy '#{source.name}' by #{request['deltaMm'].join(', ')} mm?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: copy group', true)
      begin
        copy = source.copy
        copy.transform!(Geom::Transformation.translation(Geom::Vector3d.new(*values)))
        model.commit_operation
        { approved: true, persistentId: copy.persistent_id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.find_group(model, id)
      entity = model.find_entity_by_persistent_id(Integer(id))
      raise 'Group not found.' unless entity.is_a?(Sketchup::Group)
      entity
    end

    def self.footprint_points(points, z_mm)
      raise 'Provide 3 to 50 footprint points.' unless points.is_a?(Array) && points.length.between?(3, 50)
      z = bounded_number(z_mm)
      xy = points.map do |point|
        raise 'Each point must be [xMm, yMm].' unless point.is_a?(Array) && point.length == 2
        point.map { |value| bounded_number(value) }
      end
      raise 'Footprint must contain at least 3 unique points.' if xy.uniq.length < 3
      xy.map { |x, y| Geom::Point3d.new(x.mm, y.mm, z.mm) }
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

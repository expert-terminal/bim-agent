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
      when 'scenes'
        model.pages.first(200).map { |page| { name: page.name, persistentId: page.persistent_id } }
      when 'create_face'
        create_face(model, request)
      when 'create_dimension'
        create_dimension(model, request)
      when 'create_scene'
        create_scene(model, request)
      when 'create_section_plane'
        create_section_plane(model, request)
      when 'delete_section_plane'
        delete_section_plane(model, request)
      when 'set_camera'
        set_camera(model, request)
      when 'create_edges'
        create_edges(model, request)
      when 'create_circle'
        create_circle(model, request)
      when 'create_arc'
        create_arc(model, request)
      when 'create_cylinder'
        create_cylinder(model, request)
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

    def self.create_face(model, request)
      name = request.fetch('name', 'BIM-agent surface').to_s.strip[0, 100]
      raw = request.fetch('pointsMm')
      raise 'Provide 3 to 100 XYZ points.' unless raw.is_a?(Array) && raw.length.between?(3, 100)
      points = raw.map do |point|
        raise 'Each point must be [xMm, yMm, zMm].' unless point.is_a?(Array) && point.length == 3
        Geom::Point3d.new(*point.map { |value| bounded_number(value).mm })
      end
      raise 'The surface needs at least 3 unique points.' if points.uniq.length < 3
      return { approved: false } unless UI.messagebox("Create a planar surface '#{name}' from #{points.length} points?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create surface', true)
      begin
        group = model.entities.add_group
        face = group.entities.add_face(points)
        raise 'SketchUp could not create a face. Check that points are coplanar and non-self-intersecting.' unless face
        group.name = name
        id = group.persistent_id
        model.commit_operation
        { approved: true, persistentId: id, areaSquareMm: (face.area * 25.4 * 25.4).round(2) }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.set_camera(model, request)
      eye = point3_mm(request.fetch('eyeMm'))
      target = point3_mm(request.fetch('targetMm'))
      up_data = request.fetch('up')
      raise 'up must contain 3 vector values.' unless up_data.is_a?(Array) && up_data.length == 3
      up = Geom::Vector3d.new(*up_data.map { |value| bounded_number(value) })
      raise 'Camera eye and target must differ; up must be non-zero and non-parallel to the view direction.' if eye.distance(target) < 1.mm || up.length < 1e-9 || up.parallel?(target - eye)
      perspective = request.fetch('perspective', true)
      raise 'perspective must be true or false.' unless perspective == true || perspective == false
      return { approved: false } unless UI.messagebox("Set the active SketchUp camera to eye #{request['eyeMm']} and target #{request['targetMm']}?", MB_YESNO) == IDYES
      camera = Sketchup::Camera.new(eye, target, up)
      camera.perspective = perspective
      model.active_view.camera = camera
      model.active_view.invalidate
      { approved: true, perspective: perspective }
    end

    def self.create_edges(model, request)
      runs = request.fetch('runs')
      raise 'Provide 1 to 50 edge runs.' unless runs.is_a?(Array) && runs.length.between?(1, 50)
      points = runs.map do |run|
        raise 'Each edge run needs 2 to 200 XYZ points.' unless run.is_a?(Array) && run.length.between?(2, 200)
        run.map { |point| point3_mm(point) }
      end
      return { approved: false } unless UI.messagebox("Create #{points.length} connected edge run(s) in the active SketchUp model?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create edges', true)
      begin
        ids = points.map do |run|
          edges = model.entities.add_edges(run)
          raise 'SketchUp could not create an edge run.' if edges.empty?
          edges.map(&:persistent_id)
        end
        model.commit_operation
        { approved: true, edgePersistentIds: ids.flatten }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_circle(model, request)
      center = point3_mm(request.fetch('centerMm'))
      normal_data = request.fetch('normal')
      raise 'normal must contain 3 vector values.' unless normal_data.is_a?(Array) && normal_data.length == 3
      normal = Geom::Vector3d.new(*normal_data.map { |value| bounded_number(value) })
      radius = bounded_number(request.fetch('radiusMm'))
      segments = Integer(request.fetch('segments', 24))
      raise 'Normal must be non-zero, radius positive, and segments 8–256.' if normal.length < 1e-9 || radius <= 0 || !segments.between?(8, 256)
      return { approved: false } unless UI.messagebox("Create circle with radius #{radius} mm and #{segments} segments?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create circle', true)
      begin
        edges = model.entities.add_circle(center, normal, radius.mm, segments)
        raise 'SketchUp could not create the circle.' if edges.empty?
        model.commit_operation
        { approved: true, edgePersistentIds: edges.map(&:persistent_id) }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_arc(model, request)
      center = point3_mm(request.fetch('centerMm'))
      normal_data = request.fetch('normal')
      xaxis_data = request.fetch('xAxis')
      raise 'normal and xAxis must contain 3 vector values.' unless normal_data.is_a?(Array) && normal_data.length == 3 && xaxis_data.is_a?(Array) && xaxis_data.length == 3
      normal = Geom::Vector3d.new(*normal_data.map { |value| bounded_number(value) })
      xaxis = Geom::Vector3d.new(*xaxis_data.map { |value| bounded_number(value) })
      radius = bounded_number(request.fetch('radiusMm'))
      start_angle = bounded_number(request.fetch('startRadians'))
      end_angle = bounded_number(request.fetch('endRadians'))
      segments = Integer(request.fetch('segments', 24))
      raise 'Arc vectors must be non-zero, radius positive, and segments 2–256.' if normal.length < 1e-9 || xaxis.length < 1e-9 || radius <= 0 || !segments.between?(2, 256)
      raise 'Arc angles must be increasing and span less than 2π radians.' unless end_angle > start_angle && end_angle - start_angle < 2 * Math::PI
      return { approved: false } unless UI.messagebox("Create arc from #{start_angle} to #{end_angle} radians at radius #{radius} mm?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create arc', true)
      begin
        edges = model.entities.add_arc(center, xaxis, normal, radius.mm, start_angle, end_angle, segments)
        raise 'SketchUp could not create the arc.' if edges.empty?
        model.commit_operation
        { approved: true, edgePersistentIds: edges.map(&:persistent_id) }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_cylinder(model, request)
      name = request.fetch('name', 'BIM-agent cylinder').to_s.strip[0, 100]
      center = point3_mm(request.fetch('baseCenterMm'))
      radius = bounded_number(request.fetch('radiusMm'))
      height = bounded_number(request.fetch('heightMm'))
      segments = Integer(request.fetch('segments', 24))
      raise 'Radius and height must be positive; segments must be 8–256.' unless radius.positive? && height.positive? && segments.between?(8, 256)
      return { approved: false } unless UI.messagebox("Create '#{name}' cylinder, radius #{radius} mm and height #{height} mm?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create cylinder', true)
      begin
        group = model.entities.add_group
        edges = group.entities.add_circle(center, Z_AXIS, radius.mm, segments)
        face = group.entities.add_face(edges)
        raise 'SketchUp could not create cylinder footprint.' unless face
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

    def self.create_dimension(model, request)
      start_point = point3_mm(request.fetch('startMm'))
      end_point = point3_mm(request.fetch('endMm'))
      offset = request.fetch('offsetMm')
      raise 'Offset must be a 3D vector.' unless offset.is_a?(Array) && offset.length == 3
      vector = Geom::Vector3d.new(*offset.map { |value| bounded_number(value).mm })
      raise 'Dimension endpoints must differ and offset must be non-zero.' if start_point.distance(end_point) < 0.001.mm || vector.length < 0.001.mm
      return { approved: false } unless UI.messagebox('Create a linear dimension using current model units and dimension style?', MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create dimension', true)
      begin
        dimension = model.entities.add_dimension_linear(start_point, end_point, vector)
        raise 'SketchUp could not create the dimension.' unless dimension
        id = dimension.persistent_id
        model.commit_operation
        { approved: true, persistentId: id, measuredLengthMm: start_point.distance(end_point).to_mm.round(2) }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_scene(model, request)
      name = request.fetch('name').to_s.strip[0, 100]
      raise 'Scene name is required.' if name.empty?
      return { approved: false } unless UI.messagebox("Create SketchUp scene '#{name}' from the current model view and display state?", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create scene', true)
      begin
        page = model.pages.add(name)
        id = page.persistent_id
        model.commit_operation
        { approved: true, name: page.name, persistentId: id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.create_section_plane(model, request)
      point = point3_mm(request.fetch('pointMm'))
      normal_data = request.fetch('normal')
      raise 'normal must contain 3 vector values.' unless normal_data.is_a?(Array) && normal_data.length == 3
      normal = Geom::Vector3d.new(*normal_data.map { |value| bounded_number(value) })
      raise 'Section plane normal must be non-zero.' if normal.length < 1e-9
      return { approved: false } unless UI.messagebox('Create and activate a section plane at the supplied point?', MB_YESNO) == IDYES
      model.start_operation('BIM-agent: create section plane', true)
      begin
        plane = model.entities.add_section_plane(point, normal)
        raise 'SketchUp could not create the section plane.' unless plane
        plane.activate
        id = plane.persistent_id
        model.commit_operation
        { approved: true, persistentId: id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.delete_section_plane(model, request)
      id = Integer(request.fetch('persistentId'))
      plane = model.find_entity_by_persistent_id(id)
      raise 'Section plane not found.' unless plane.is_a?(Sketchup::SectionPlane)
      return { approved: false } unless UI.messagebox("Delete section plane #{id}? This removes the cut plane from the model.", MB_YESNO) == IDYES
      model.start_operation('BIM-agent: delete section plane', true)
      begin
        plane.erase!
        model.commit_operation
        { approved: true, persistentId: id }
      rescue StandardError
        model.abort_operation
        raise
      end
    end

    def self.point3_mm(raw)
      raise 'Point must contain [xMm, yMm, zMm].' unless raw.is_a?(Array) && raw.length == 3
      Geom::Point3d.new(*raw.map { |value| bounded_number(value).mm })
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

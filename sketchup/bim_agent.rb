require 'sketchup.rb'
require 'extensions.rb'

module ExpertTerminalBimAgent
  unless defined?(@extension_registered) && @extension_registered
    extension = SketchupExtension.new('BIM-agent', 'bim_agent/main')
    extension.description = 'Local MCP bridge for architectural modeling in SketchUp.'
    extension.version = '0.4.0'
    extension.creator = 'BIM-agent contributors'
    Sketchup.register_extension(extension, true)
    @extension_registered = true
  end
end

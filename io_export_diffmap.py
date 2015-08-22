bl_info = {
    "name": "Export MetaMorph Diff Map Animation",
    "author": "Foolish Frost / Ivo Grigull",
    "version": (1, 1, 2),
    "blender": (2, 5, 8),
    "api": 36079,
    "location": "Mesh-data section in property-window",
    "description": "Creates Diff Maps for each Shapekey",
    "warning": "",
    "url": "http://www.rezzable.com",
    "category": "Import-Export"}

import bpy
from mathutils import Vector, Color
import time
from bpy.props import *
from bpy_extras.io_utils import ExportHelper, ImportHelper
import os

__version__ = '1.1.2'

# ------------------------------------ core functions ------------------------------------

# Global variables
original_materials = []
mat = None
original_face_mat_indices = []

ShapeKeyName = []
MaxDiffStore = []


# Find faces that use the given vertex
# Returns: tuple with face_index and vertex_index
def vertex_find_connections( mesh, vert_index):

    list = []

    for n in mesh.polygons:
        for i in range( len(n.vertices) ):
            if n.vertices[i] == vert_index:
                list.append( (n.index, i ) )
    return list


# Remember and set up things
#   Deselect all verts
#   Remember materials
#   Create temp vertex color layer
def pre(ob):
    print("Prep work started...")

    mesh = ob.data
    uvtex = mesh.uv_textures.active
    vcol = mesh.vertex_colors.active

    global original_materials, mat
    global original_face_mat_indices

    # Deselect all vertices (to avoid odd artefacts, some bug?)
    for n in mesh.vertices:
        n.select = False

    # Store face material indices
    original_face_mat_indices = []
    for n in mesh.polygons:
        original_face_mat_indices.append( n.material_index )
        n.material_index = 0

    # Remember and remove materials
    original_materials = []
    for n in mesh.materials:
        print("Saving Material: " + n.name)
        original_materials.append(n)
    #for n in original_materials:
    temp_i = 0
    for n in mesh.materials:
        mesh.materials.pop(temp_i)
        temp_i = temp_i+1
    # Create new temp material for baking
    mat = bpy.data.materials.new(name="DiffMap_Bake")
    mat.use_vertex_color_paint = True
    mat.diffuse_color = Color([1,1,1])
    mat.diffuse_intensity = 1.0
    mat.use_shadeless = True
    mesh.materials.append(mat)
    # mesh.materials[0] = mat

    # Add new vertex color layer for baking
    if len(mesh.vertex_colors) < 8-1:

        vcol = mesh.vertex_colors.new(name="DiffMap_Bake")
        mesh.vertex_colors.active = vcol
        vcol.active_render = True
    else:
        print("Amount limit of vertex color layers exceeded")



# Restore things
#   Remove temp materials and restore originals
#   Remove temp vertex color layer
def post(ob):
    print("Post work started...")

    global original_materials, mat
    global original_face_mat_indices

    mesh = ob.data
    uvtex = mesh.uv_textures.active
    vcol = mesh.vertex_colors.active
    original_face_images = []

    # Remove temp material
    mesh.materials.pop()
    mat.user_clear()
    bpy.data.materials.remove( mat )

    # Restore original materials
    temp_index = 0
    #mesh.materials.empty()
    for n in original_materials:
        #mesh.materials.append(n)
        if (len(mesh.materials) < temp_index - 1):
            mesh.materials.append(n)
        else:
            mesh.materials[temp_index] = original_materials[temp_index]
        temp_index = temp_index + 1

    # Restore face material indices
    for n in range(len(original_face_mat_indices)-1):
        mesh.polygons[n].material_index = original_face_mat_indices[n]

    # Remove temp vertex color layer
    bpy.ops.mesh.vertex_color_remove()


    # Refresh UI
    bpy.context.scene.frame_current = bpy.context.scene.frame_current

    # Free some memory
    original_materials = []
    original_face_mat_indices = []



def generate_diffmap_from_shape(ob, filepath, name, shape, shapeson, width=128, height=128, margin=10 ):

    global ShapeKeyName
    global MaxDiffStore

    mesh = ob.data
    uvtex = mesh.uv_textures.active
    vcol = mesh.vertex_colors.active


    # Find biggest distance offset in shape
    maxdiff = 0.0

    for n in mesh.vertices:

        diff = n.co.copy() - shape.data[n.index].co.copy()
        for i in diff:
            if abs(i) > maxdiff:
                maxdiff = abs(i)

    if maxdiff > 0:
        ShapeKeyName = ShapeKeyName + [shape.name]
        MaxDiffStore = MaxDiffStore + [maxdiff]

    if shapeson == True:
        # Generate vertex color from shape key offset
        for n in mesh.vertices:

            faces = vertex_find_connections( mesh, n.index)

            color = Color( [0,0,0] )

            diff = n.co.copy() - shape.data[n.index].co.copy()

            if maxdiff > 0:
                color[0] = 1.0 - (((diff[0] / maxdiff) + 1.0) *0.5)
                color[1] = 1.0 - (((diff[1] / maxdiff) + 1.0) *0.5)
                color[2] = 1.0 - (((diff[2] / maxdiff) + 1.0) *0.5)

            # Apply vertex color to all connected face corners (vertcolors have same structure as uvs)
            for i in faces:
                if i[1] == 0:
                    vcol.data[i[0]].color = color
                if i[1] == 1:
                    vcol.data[i[0]].color = color
                if i[1] == 2:
                    vcol.data[i[0]].color = color
                if i[1] == 3:
                    vcol.data[i[0]].color = color


        # Create new image to bake to
        image = bpy.data.images.new(name="DiffMap_Bake", width=width, height=height)
        image.generated_width = width
        image.generated_height = height


        # Construct complete filepath
        platform = str(bpy.app.build_platform)
        if platform.find("Windows") != -1:
            path = filepath + '\\' + name + '-' + shape.name + '.tga'

        elif platform.find("Linux") != -1:
            path = filepath + '/' + name + '-' + shape.name + '.tga'

        else:
            path = filepath + '/' + name + '-' + shape.name + '.tga'

        image.filepath = path


        # assign image to mesh (all uv faces)
        original_image = uvtex.data[0].image # Simply taking the image from the first face
        original_face_images = []

        for n in uvtex.data:
            if n.image == None:
                original_face_images.append( None )
            else:
                original_face_images.append( n.image.name )

            n.image = image


        # Bake
        render = bpy.context.scene.render

        # tempcmv = render.use_color_management;

        render.bake_type = 'TEXTURE'
        render.bake_margin = margin
        render.use_bake_clear = True
        render.use_bake_selected_to_active = False
        render.bake_quad_split = 'AUTO'
        # render.use_color_management = False

        bpy.ops.object.bake_image()



        image.save()

        # re-assign images to mesh faces
        for n in range( len(original_face_images) ):

            tmp = original_face_images[n]
            if original_face_images[n] != None:
                tmp = bpy.data.images[ original_face_images[n] ]
            else:
                tmp = None

            uvtex.data[n].image = tmp


        # Remove image from memory
        image.user_clear()
        bpy.data.images.remove(image)

        # render.use_color_management = tempcmv

        # Tell user what was exported
        print( " exported %s" % path )


# General error checking
def found_error(self, context):

    ob = context.active_object
    scene = context.scene
    filepath = self.filepath

    if ob.type != 'MESH':
        self.report({'ERROR'}, "Object is not a mesh")
        print("Object is not a mesh")
        return True
    if ob.data.shape_keys == None:
        self.report({'ERROR'}, "Mesh has no shape keys")
        print("Mesh has no shape keys")
        return True
    if len(ob.data.uv_textures) <= 0:
        self.report({'ERROR'}, "Mesh has no UVs")
        print("Mesh has no UVs")
        return True
    if not os.path.exists( filepath ):
        self.report({'ERROR'}, "Invalid dir: %s" % filepath)
        print("Invalid dir: %s" % filepath)
        return True
    if len( ob.data.polygons ) <= 0:
        self.report({'ERROR'}, "Mesh contains no polygons")
        print("Mesh contains no polygons")
        return True

    return False


def main(self, context):

    global ShapeKeyName
    global MaxDiffStore

    ob = context.active_object
    scene = context.scene

    print("-----------------------------------------------------")
    print("Starting Diff-Map export for object %s ..." % ob.name )

    # Error checking
    if found_error(self, context):
        print(found_error(self, context))
        return

    pre(ob)

    shape = ob.active_shape_key
    filepath = self.filepath
    name = self.name
    width = self.width
    height = self.height
    margin = self.margin
    animationson = self.animationson
    shapeson = self.shapeson

    ShapeKeyName = []
    MaxDiffStore = []

    #for n in ob.data.shape_keys.key_blocks:
    for n in ob.data.shape_keys.key_blocks:
        if n.name != 'Basis': # Skip the 'Basis' shape
            generate_diffmap_from_shape(ob, filepath, name, n, shapeson, width, height, margin )
    #generate_diffmap_from_shape(ob,filepath,name,ob.data.shape_keys.key_blocks["Melt Spread"],shapeson,width,height,margin)
    post(ob)

    if animationson == True:
        Write_Animation(filepath, name, self)

    print(" Finished.")
    print("-----------------------------------------------------")




def Write_Animation(Afilepath, Afilename, self):

    global ShapeKeyName
    global MaxDiffStore

    ShapeKeyName2 = []
    ShapeKeyName3 = []
    MaxDiffStore2 = []

    print("-------------------------------")
    print("Starting writing Animation List")
    #print("-------------------------------")

    Afileset = Afilepath + '/' + Afilename + '-DiffMapAnimation.TXT'

    # Okay, let's get the base data.
    MyObject = bpy.context.active_object
    MyShapekeys = MyObject.data.shape_keys.key_blocks
    AnimationStart = bpy.context.scene.frame_start
    AnimationEnd = bpy.context.scene.frame_end

    name = self.name

    framestring = str(AnimationStart) + 'to' + str(AnimationEnd)
    Afileset = Afilepath + '/' + Afilename + '-' + framestring + '-DiffMapAnimation.TXT'
    # open the file...
    Animation_Output = open(Afileset,"w")

    for AnimationShapes in range(0, len(ShapeKeyName)):
        hasvalue = False
        for AnimationFrame in range(AnimationStart, AnimationEnd + 1):
            bpy.context.scene.frame_set(AnimationFrame)
            if MyShapekeys[ShapeKeyName[AnimationShapes]].value > 0.0005:
                hasvalue = True
        if hasvalue == True:
            ShapeKeyName2 = ShapeKeyName2 + [ShapeKeyName[AnimationShapes]]
            ShapeKeyName3 = ShapeKeyName3 + [name+"-"+ShapeKeyName[AnimationShapes]]
            MaxDiffStore2 = MaxDiffStore2 + [MaxDiffStore[AnimationShapes]]

    Animation_Output.write(str(list(ShapeKeyName3)) + "\n")
    Animation_Output.write(str(list(MaxDiffStore2)) + "\n")

    # And loop for every frame of the animation!
    for AnimationFrame in range(AnimationStart, AnimationEnd + 1):
        # Set the current animation frame
        bpy.context.scene.frame_set(AnimationFrame)

        #print("Now working with animation frame: " + str(AnimationFrame))

        # And loop for all but the Basis shapekey.
        row = []
        for AnimationShapes in range(0, len(ShapeKeyName2)):
            MyData = MyShapekeys[ShapeKeyName2[AnimationShapes]].value
            row = row + [float("%0.4f" % (MyData))]

        Animation_Output.write(str(list(row)) + "\n")
        #print (list(row))

    Animation_Output.close()

    #print("---------------------------")
    print("Done writing Animation List")
    print("---------------------------")


# ------------------------------------ UI area  ------------------------------------



class EXPORT_OT_tools_diffmap_exporter(bpy.types.Operator):
    '''Import from DXF file format (.dxf)'''
    bl_idname = "object.export_diffmaps_from_shapes"
    bl_description = 'Export to MetaMorph file format'
    bl_label = "Export Diff" +' v.'+ __version__
    bl_space_type = "PROPERTIES"
    bl_region_type = "WINDOW"
    bl_context = "data"
    bl_context = "data"

    filename_ext = ".tga"
    filter_glob = StringProperty(default="*.tga", options={'HIDDEN'})

    filepath = StringProperty(name="File Path", description="Filepath used for exporting diffmap files", maxlen= 1024, default= "", subtype='FILE_PATH')
    filename = bpy.props.StringProperty(name="File Name", description="Name of the file",)
    name = StringProperty( name="Name", description="Name for the file (without tga extension please)", maxlen = 512, default = "Name")

    #try:
    #    bpy.context.active_object.name
    #except NameError:
    #    name = StringProperty( name="Name", description="Name for the file (without tga extension please)", maxlen = 512, default = "Name")
    #else:
    #    name = StringProperty( name="Name", description="Name for the file (without tga extension please)", maxlen = 512, default = bpy.context.active_object.name)

    width = IntProperty( name="Width", description="Width of image to export", default = 256, min= 1, max=65535)
    height = IntProperty( name="Height", description="Height of image to export", default = 256, min= 1, max=65535)
    shapeson = BoolProperty( name="Export ShapeKeys", description="Save shapekeys as TGA images", default = True)
    animationson = BoolProperty( name="Export Animation", description="Save shapekeys animation", default = True)
    margin = IntProperty( name="Edge Margin", description="sets outside margin around UV edges", default = 10, min= 0, max=64)


    ##### DRAW #####
    def draw(self, context):
        layout = self.layout

        filepath = os.path.dirname( self.filepath )

        os.path.join(filepath)

        row = layout.row()

        row = layout.row()
        row.prop(self, "name")

        col = layout.column(align=True)
        col.prop(self, "width")
        col.prop(self, "height")
        col.prop(self, "margin")

        me = context.active_object.data
        col = layout.column(align=False)
        # col.template_list("MESH_UL_uvmaps_vcols", "uvmaps", me, "uv_textures", me.uv_textures, "active_index", rows=2)

        col = layout.column(align=False)
        col.prop(self, "shapeson")
        col.prop(self, "animationson")

    def execute(self, context):
        #name = context.active_object.name

        start = time.time()
        main(self, context)
        print ("Time elapsed:", time.time() - start, "seconds.")

        return {'FINISHED'}

    def invoke(self, context, event):
        wm = context.window_manager
        wm.fileselect_add(self)

        try:
            bpy.context.active_object.name
        except NameError:
            pass
        else:
            self.name = bpy.context.active_object.name


        return {'RUNNING_MODAL'}

def menu_func(self, context):
    if bpy.data.filepath:
        default_path = os.path.split(bpy.data.filepath)[0] + "/"
        self.layout.operator(EXPORT_OT_tools_diffmap_exporter.bl_idname, text="Export DiffMaps and Animations").filepath = default_path
    else:
        self.layout.operator(EXPORT_OT_tools_diffmap_exporter.bl_idname, text="Export DiffMaps and Animations")

def register():
    bpy.utils.register_module(__name__)
    bpy.types.INFO_MT_file_export.append(menu_func)

def unregister():
    bpy.utils.unregister_module(__name__)
    bpy.types.INFO_MT_file_export.remove(menu_func)

if __name__ == "__main__":
    register()

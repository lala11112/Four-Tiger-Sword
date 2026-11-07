Lab Room 02 V7 — enclosed 10m elevator, 35 props
Assets remain under Assets/LabRoom02V5 intentionally to preserve existing prefab, native mesh, FBX and material GUIDs.
33 elevator dimensions: 4.8m width, 4.6m depth, 10m height. The car has opaque side walls, back and ceiling. The upper shaft has actual opaque front, side, back and top mesh panels. Only the ground boarding entrance is open. This is a static visual prop, not a moving elevator or gameplay implementation. Its BoxCollider remains a nonblocking trigger.
Every prefab has one GameObject, zero children, one native MeshFilter and one MeshRenderer. Identity scale/rotation, 1 unit = 1m, bottom-center pivot and +Z front. UV0 texture coordinates and UV1 emission-mask layout are preserved.
34 stage transfer gate remains excluded. Unity package import cannot delete assets or scene instances already imported in your project. If you imported V5, manually remove the old 34 gate if no longer needed. No automatic cleanup code is included.
The demo uses a 2m reference grid. Optional preview URP settings do not overwrite project Graphics/Quality settings.

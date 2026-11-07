V7 enclosed elevator update only — asset 33
Import updates the existing Assets/LabRoom02V5 elevator with the original prefab/native mesh/FBX GUIDs and paths. Shared material/texture GUIDs are retained.
Dimensions remain exactly 4.8m wide, 4.6m deep and 10m high. Car sides/back/ceiling and the upper shaft front/sides/back/top are opaque geometry; only the ground boarding entrance stays open.
Static visual prop only. No animated travel or gameplay. BoxCollider is a nonblocking trigger. UV0 textures and UV1 emission-mask layout are preserved. +Z front, bottom-center pivot and scale 1.
34 stage transfer gate remains excluded. Importing a unitypackage cannot delete previously imported gate assets or scene instances. Remove old 34 manually if needed; this update never automatically cleans another project.

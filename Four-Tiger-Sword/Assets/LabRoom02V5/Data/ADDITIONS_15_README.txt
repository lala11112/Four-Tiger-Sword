Lab Room 02 — 15 additions (IDs 47–61)
Unity 6 URP, tested Unity 6000.3.21f1 / URP 17.3.0.
Import this package when you only want the 15 new props. It contains no previous 21 prefab/model/mesh assets and no all-36 demo scene.
Each prefab: one GameObject, zero children, one native MeshFilter, one MeshRenderer. Identity transform, meters, bottom-center pivot, +Z front.
UV0 textures and UV1 emission-mask layout are preserved. Supplied mask PNGs use UV1; ordinary URP/Lit emission uses material color/maps on UV0.
IDs 56 and 59 have compound BoxCollider components on the same GameObject, preserving the open middle/interior. Other new props use a bounds BoxCollider. Adjust collisions for your controller.
Static visual props only. No gameplay scripts or automatic project pipeline changes.

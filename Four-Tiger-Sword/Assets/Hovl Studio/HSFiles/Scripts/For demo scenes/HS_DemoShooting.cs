using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Hovl
{
    public class HS_DemoShooting : MonoBehaviour
    {
        [Header("Fire rate")]
        [Range(0.01f, 1f)]
        public float fireRate = 0.1f;
        float fireCountdown;

        [Header("References")]
        [SerializeField] Transform firePoint;
        [SerializeField] Camera cam;
        [SerializeField] Animation camAnim;

        [Header("Projectile settings")]
        [SerializeField] float maxLength = 100f;
        [SerializeField] GameObject[] prefabs;

        [Header("Pooling")]
        [SerializeField] int maxPoolSizePerPrefab = 40;

        [Header("Projectile switching")]
        [SerializeField] float switchDelay = 0.4f;

        int currentPrefabIndex;
        float buttonSaver;

        static Transform globalPoolRoot;

        // key = prefab instance id, value = pool list
        static readonly Dictionary<int, List<GameObject>> pools = new Dictionary<int, List<GameObject>>();
        static readonly Dictionary<int, Transform> poolParents = new Dictionary<int, Transform>();

        void Awake()
        {
            EnsureGlobalPool();

            if (cam == null)
                cam = Camera.main;
        }

        void Start()
        {
            Counter(0);
        }

        void Update()
        {
            HandleShooting();
            HandleProjectileSwitch();
            HandleRotation();

            if (fireCountdown > 0f)
                fireCountdown -= Time.deltaTime;

            buttonSaver += Time.deltaTime;
        }

        void HandleShooting()
        {
            if (IsFirePressedThisFrame())
            {
                Shoot();
            }

            if (IsFastFireHeld() && fireCountdown <= 0f)
            {
                Shoot();
                fireCountdown = fireRate;
            }
        }

        void HandleProjectileSwitch()
        {
            float horizontal = GetHorizontalInput();

            if (horizontal < 0f && buttonSaver >= switchDelay)
            {
                buttonSaver = 0f;
                Counter(-1);
            }
            else if (horizontal > 0f && buttonSaver >= switchDelay)
            {
                buttonSaver = 0f;
                Counter(1);
            }
        }

        void HandleRotation()
        {
            if (cam == null)
                return;

            Vector2 pointerPosition = GetPointerScreenPosition();
            Ray ray = cam.ScreenPointToRay(pointerPosition);

            if (Physics.Raycast(ray, out RaycastHit hit, maxLength))
            {
                RotateToMouseDirection(hit.point);
            }
        }

        void Shoot()
        {
            if (prefabs == null || prefabs.Length == 0)
                return;

            if (firePoint == null)
            {
                Debug.LogWarning("HS_DemoShooting: FirePoint is not assigned.");
                return;
            }

            GameObject prefab = prefabs[currentPrefabIndex];
            if (prefab == null)
                return;

            GameObject projectile = GetProjectile(prefab);
            if (projectile == null)
                return;

            if (camAnim != null && camAnim.clip != null)
                camAnim.Play(camAnim.clip.name);

            Transform projectileTransform = projectile.transform;
            projectileTransform.SetParent(null, false);
            projectileTransform.SetPositionAndRotation(firePoint.position, firePoint.rotation);

            Rigidbody rb = projectile.GetComponent<Rigidbody>();
            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.angularVelocity = Vector3.zero;
            }

            projectile.SetActive(true);

            IPooledProjectile pooledProjectile = projectile.GetComponent<IPooledProjectile>();
            if (pooledProjectile != null)
                pooledProjectile.OnSpawnedFromPool();
        }

        GameObject GetProjectile(GameObject prefab)
        {
            if (prefab == null)
                return null;

            int prefabId = prefab.GetInstanceID();

            if (!pools.TryGetValue(prefabId, out List<GameObject> pool))
            {
                pool = new List<GameObject>();
                pools[prefabId] = pool;
            }

            CleanupDestroyedObjects(pool);

            for (int i = 0; i < pool.Count; i++)
            {
                GameObject pooledObject = pool[i];

                if (pooledObject == null)
                    continue;

                if (!pooledObject.activeInHierarchy)
                    return pooledObject;
            }

            if (GetValidObjectCount(pool) >= maxPoolSizePerPrefab)
                return null;

            GameObject newProjectile = CreateProjectile(prefab, prefabId);
            if (newProjectile != null)
                pool.Add(newProjectile);

            return newProjectile;
        }

        void CleanupDestroyedObjects(List<GameObject> pool)
        {
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                if (pool[i] == null)
                    pool.RemoveAt(i);
            }
        }

        int GetValidObjectCount(List<GameObject> pool)
        {
            int count = 0;

            for (int i = 0; i < pool.Count; i++)
            {
                if (pool[i] != null)
                    count++;
            }

            return count;
        }

        GameObject CreateProjectile(GameObject prefab, int prefabId)
        {
            Transform parent = GetOrCreatePoolParent(prefab, prefabId);

            GameObject newProjectile = Instantiate(prefab, parent);
            newProjectile.SetActive(false);

            return newProjectile;
        }

        Transform GetOrCreatePoolParent(GameObject prefab, int prefabId)
        {
            if (poolParents.TryGetValue(prefabId, out Transform existingParent) && existingParent != null)
                return existingParent;

            GameObject parentObject = new GameObject(prefab.name + "_Pool");
            parentObject.transform.SetParent(globalPoolRoot);
            poolParents[prefabId] = parentObject.transform;

            return parentObject.transform;
        }

        static void EnsureGlobalPool()
        {
            if (globalPoolRoot != null)
                return;

            GameObject existing = GameObject.Find("Hovl_GlobalProjectilePool");
            if (existing != null)
            {
                globalPoolRoot = existing.transform;
                DontDestroyOnLoad(existing);
                return;
            }

            GameObject poolObject = new GameObject("Hovl_GlobalProjectilePool");
            DontDestroyOnLoad(poolObject);
            globalPoolRoot = poolObject.transform;
        }

        void Counter(int count)
        {
            if (prefabs == null || prefabs.Length == 0)
                return;

            currentPrefabIndex += count;

            if (currentPrefabIndex >= prefabs.Length)
                currentPrefabIndex = 0;
            else if (currentPrefabIndex < 0)
                currentPrefabIndex = prefabs.Length - 1;
        }

        void RotateToMouseDirection(Vector3 destination)
        {
            Vector3 direction = destination - transform.position;

            if (direction.sqrMagnitude <= 0.0001f)
                return;

            transform.rotation = Quaternion.LookRotation(direction);
        }

        bool IsFirePressedThisFrame()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetButtonDown("Fire1"))
                return true;
#endif

            return false;
        }

        bool IsFastFireHeld()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && Mouse.current.rightButton.isPressed)
                return true;
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Input.GetMouseButton(1))
                return true;
#endif

            return false;
        }

        float GetHorizontalInput()
        {
            float horizontal = 0f;

#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null)
            {
                if (Keyboard.current.aKey.isPressed)
                    horizontal -= 1f;

                if (Keyboard.current.dKey.isPressed)
                    horizontal += 1f;
            }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            if (Mathf.Approximately(horizontal, 0f))
            {
                if (Input.GetKey(KeyCode.A))
                    horizontal -= 1f;

                if (Input.GetKey(KeyCode.D))
                    horizontal += 1f;

                if (Mathf.Approximately(horizontal, 0f))
                    horizontal = Input.GetAxisRaw("Horizontal");
            }
#endif

            return Mathf.Clamp(horizontal, -1f, 1f);
        }

        Vector2 GetPointerScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
                return Mouse.current.position.ReadValue();
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }
    }

    public interface IPooledProjectile
    {
        void OnSpawnedFromPool();
    }
}
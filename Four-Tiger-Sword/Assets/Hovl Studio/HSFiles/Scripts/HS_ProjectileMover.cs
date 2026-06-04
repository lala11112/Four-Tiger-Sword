using System.Collections;
using UnityEngine;

namespace Hovl
{
    public class HS_ProjectileMover : MonoBehaviour
    {
        [SerializeField] protected float speed = 15f;
        [SerializeField] protected float hitOffset = 0f;
        [SerializeField] protected bool UseFirePointRotation;
        [SerializeField] protected Vector3 rotationOffset = Vector3.zero;

        [Header("Effects")]
        [SerializeField] protected GameObject hit;
        [SerializeField] protected ParticleSystem hitPS;
        [SerializeField] protected GameObject flash;
        [SerializeField] protected ParticleSystem projectilePS;
        [SerializeField] protected GameObject[] Detached;

        [Header("Components")]
        [SerializeField] protected Rigidbody rb;
        [SerializeField] protected Collider col;
        [SerializeField] protected Light lightSourse;

        [Header("Lifetime")]
        [SerializeField] protected bool notDestroy = false;
        [SerializeField] protected float lifeTime = 5f;
        [SerializeField] protected float detachedLifeTime = 1f;

        protected bool initialized;
        protected bool collided;
        protected Coroutine lifeRoutine;
        protected Coroutine disableAfterHitRoutine;

        [System.Serializable]
        protected class DetachedState
        {
            public GameObject obj;
            public Transform originalParent;
            public Vector3 localPosition;
            public Quaternion localRotation;
            public Vector3 localScale;
        }

        protected DetachedState[] detachedStates;

        protected virtual void Awake()
        {
            if (rb == null)
                rb = GetComponent<Rigidbody>();

            if (col == null)
                col = GetComponent<Collider>();

            SetupDetachedCache();
        }

        protected virtual void Start()
        {
            initialized = true;

            if (flash != null)
                flash.transform.SetParent(null, true);

            StartLifeTimer();
        }

        protected virtual void OnEnable()
        {
            collided = false;

            if (!initialized)
                return;

            StopRunningCoroutines();

            if (lightSourse != null)
                lightSourse.enabled = true;

            if (col != null)
                col.enabled = true;

            if (rb != null)
            {
                rb.constraints = RigidbodyConstraints.None;
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.angularVelocity = Vector3.zero;
            }

            if (projectilePS != null)
            {
                projectilePS.Clear(true);
                projectilePS.Play(true);
            }

            if (notDestroy)
                RestoreDetachedObjects();

            StartLifeTimer();
        }

        protected virtual void OnDisable()
        {
            StopRunningCoroutines();
        }

        protected virtual void StopRunningCoroutines()
        {
            if (lifeRoutine != null)
            {
                StopCoroutine(lifeRoutine);
                lifeRoutine = null;
            }

            if (disableAfterHitRoutine != null)
            {
                StopCoroutine(disableAfterHitRoutine);
                disableAfterHitRoutine = null;
            }
        }

        protected virtual void SetupDetachedCache()
        {
            if (Detached == null || Detached.Length == 0)
                return;

            detachedStates = new DetachedState[Detached.Length];

            for (int i = 0; i < Detached.Length; i++)
            {
                GameObject obj = Detached[i];
                if (obj == null)
                    continue;

                detachedStates[i] = new DetachedState
                {
                    obj = obj,
                    originalParent = obj.transform.parent,
                    localPosition = obj.transform.localPosition,
                    localRotation = obj.transform.localRotation,
                    localScale = obj.transform.localScale
                };
            }
        }

        protected virtual void RestoreDetachedObjects()
        {
            if (detachedStates == null || detachedStates.Length == 0)
                return;

            for (int i = 0; i < detachedStates.Length; i++)
            {
                DetachedState state = detachedStates[i];
                if (state == null || state.obj == null)
                    continue;

                Transform t = state.obj.transform;

                t.SetParent(state.originalParent, false);
                t.localPosition = state.localPosition;
                t.localRotation = state.localRotation;
                t.localScale = state.localScale;

                ParticleSystem[] systems = state.obj.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < systems.Length; j++)
                {
                    ParticleSystem ps = systems[j];
                    if (ps == null)
                        continue;

                    ps.Clear(true);
                    ps.Play(true);
                }
            }
        }

        protected virtual void StartLifeTimer()
        {
            if (lifeRoutine != null)
                StopCoroutine(lifeRoutine);

            lifeRoutine = StartCoroutine(LifeTimerRoutine(lifeTime));
        }

        protected virtual IEnumerator LifeTimerRoutine(float time)
        {
            yield return new WaitForSeconds(time);

            if (notDestroy)
            {
                if (gameObject.activeSelf)
                    gameObject.SetActive(false);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        protected virtual void FixedUpdate()
        {
            if (collided || rb == null || speed == 0f)
                return;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = transform.forward * speed;
#else
            rb.velocity = transform.forward * speed;
#endif
        }

        protected virtual void OnCollisionEnter(Collision collision)
        {
            if (collided)
                return;

            collided = true;
            StopRunningCoroutines();

            if (rb != null)
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
                rb.velocity = Vector3.zero;
#endif
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.FreezeAll;
            }

            if (lightSourse != null)
                lightSourse.enabled = false;

            if (col != null)
                col.enabled = false;

            if (projectilePS != null)
                projectilePS.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            if (collision.contactCount > 0)
                SpawnHit(collision.contacts[0]);

            ReleaseDetachedObjects();

            float endDelay = 1f;
            if (hitPS != null)
                endDelay = Mathf.Max(hitPS.main.duration, 0.05f);

            if (notDestroy)
                disableAfterHitRoutine = StartCoroutine(DisableAfterHit(endDelay));
            else
                Destroy(gameObject, endDelay);
        }

        protected virtual IEnumerator DisableAfterHit(float delay)
        {
            yield return new WaitForSeconds(delay);

            if (gameObject != null && gameObject.activeSelf)
                gameObject.SetActive(false);
        }

        protected virtual void SpawnHit(ContactPoint contact)
        {
            if (hit == null)
                return;

            Vector3 pos = contact.point + contact.normal * hitOffset;
            Quaternion rot = Quaternion.FromToRotation(Vector3.up, contact.normal);

            hit.transform.position = pos;
            hit.transform.rotation = rot;

            if (UseFirePointRotation)
                hit.transform.rotation = transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            else if (rotationOffset != Vector3.zero)
                hit.transform.rotation = Quaternion.Euler(rotationOffset);
            else
                hit.transform.LookAt(contact.point + contact.normal);

            if (hitPS != null)
            {
                hitPS.Clear(true);
                hitPS.Play(true);
            }
        }

        protected virtual void ReleaseDetachedObjects()
        {
            if (detachedStates == null || detachedStates.Length == 0)
                return;

            for (int i = 0; i < detachedStates.Length; i++)
            {
                DetachedState state = detachedStates[i];
                if (state == null || state.obj == null)
                    continue;

                Transform t = state.obj.transform;
                t.SetParent(null, true);

                ParticleSystem[] systems = state.obj.GetComponentsInChildren<ParticleSystem>(true);
                for (int j = 0; j < systems.Length; j++)
                {
                    ParticleSystem ps = systems[j];
                    if (ps == null)
                        continue;

                    ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }

                if (!notDestroy)
                    Destroy(state.obj, detachedLifeTime);
            }
        }
    }
}
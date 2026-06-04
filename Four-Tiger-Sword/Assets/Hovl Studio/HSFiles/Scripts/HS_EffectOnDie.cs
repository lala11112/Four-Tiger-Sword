/*This script created with help from this video: https://youtu.be/tdSmKaJvCoA
 * and https://learn.unity.com/tutorial/introduction-to-object-pooling */

using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class HS_EffectOnDie : MonoBehaviour
{
    public List<GameObject> pooledObjects;
    public GameObject EffectsOnDie;
    public int poolSize;
    public float poolReturnTimer = 1.5f;

    void Start()
    {
        pooledObjects = new List<GameObject>();
        GameObject tmp;
        for (int i = 0; i < poolSize; i++)
        {
            tmp = Instantiate(EffectsOnDie, transform);
            tmp.SetActive(false);
            pooledObjects.Add(tmp);
        }
    }

    public void LateUpdate()
    {
        ParticleSystem.Particle[] particles = new ParticleSystem.Particle[GetComponent<ParticleSystem>().particleCount];
        int length = GetComponent<ParticleSystem>().GetParticles(particles);
        int i = 0;
        while (i < length)
        {
            if (EffectsOnDie != null && particles[i].remainingLifetime < Time.deltaTime)
            {
                GameObject effectInstance = GetPooledObjects();
                if (effectInstance != null)
                {
                    effectInstance.transform.position = particles[i].position;
                    effectInstance.SetActive(true);
                    StartCoroutine(LateCall(effectInstance));
                }
            }
            i++;
        }
    }

    public GameObject GetPooledObjects()
    {
        for (int i = 0; i < poolSize; i++)
        {
            if (!pooledObjects[i].activeInHierarchy)
            {
                return pooledObjects[i];
            }
        }
        return null;
    }

    // Return Instances to the pool
    private IEnumerator LateCall(GameObject soundInstance)
    {
        yield return new WaitForSeconds(poolReturnTimer);
        soundInstance.SetActive(false);
        yield break;
    }
}

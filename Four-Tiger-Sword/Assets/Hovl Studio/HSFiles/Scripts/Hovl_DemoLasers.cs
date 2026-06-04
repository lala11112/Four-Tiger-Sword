using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization.Formatters;
using System;
using UnityEngine;

public class Hovl_DemoLasers : MonoBehaviour
{
    public GameObject FirePoint;
    public Camera Cam;
    public float MaxLength;
    public GameObject[] Prefabs;

    private Ray RayMouse;
    private Vector3 direction;
    private Quaternion rotation;

    [Header("GUI")]
    private float windowDpi;

    private int Prefab;
    private GameObject Instance;

    // Store laser components without hard references to types
    private Component LaserScript;
    private Component LaserScript2;

    // Double-click protection
    private float buttonSaver = 0f;

    void Start()
    {
        if (Screen.dpi < 1) windowDpi = 1;
        if (Screen.dpi < 200) windowDpi = 1;
        else windowDpi = Screen.dpi / 200f;

        Counter(0);
    }

    void Update()
    {
        // Enable laser
        if (Input.GetMouseButtonDown(0))
        {
            if (Instance != null)
                Destroy(Instance);

            Instance = Instantiate(Prefabs[Prefab], FirePoint.transform.position, FirePoint.transform.rotation);
            Instance.transform.parent = transform;

            // Try to find scripts by name without compile dependency
            LaserScript = Instance.GetComponent("Hovl_Laser");
            LaserScript2 = Instance.GetComponent("Hovl_Laser2");
        }

        // Disable laser prefab
        if (Input.GetMouseButtonUp(0))
        {
            if (LaserScript != null)
                LaserScript.SendMessage("DisablePrepare", SendMessageOptions.DontRequireReceiver);

            if (LaserScript2 != null)
                LaserScript2.SendMessage("DisablePrepare", SendMessageOptions.DontRequireReceiver);

            if (Instance != null)
                Destroy(Instance, 1f);
        }

        // To change lasers
        if ((Input.GetKey(KeyCode.A) || Input.GetAxis("Horizontal") < 0) && buttonSaver >= 0.4f)
        {
            buttonSaver = 0f;
            Counter(-1);
        }

        if ((Input.GetKey(KeyCode.D) || Input.GetAxis("Horizontal") > 0) && buttonSaver >= 0.4f)
        {
            buttonSaver = 0f;
            Counter(+1);
        }

        buttonSaver += Time.deltaTime;

        // Current fire point
        if (Cam != null)
        {
            RaycastHit hit;
            var mousePos = Input.mousePosition;
            RayMouse = Cam.ScreenPointToRay(mousePos);

            if (Physics.Raycast(RayMouse.origin, RayMouse.direction, out hit, MaxLength))
            {
                RotateToMouseDirection(gameObject, hit.point);
            }
            else
            {
                var pos = RayMouse.GetPoint(MaxLength);
                RotateToMouseDirection(gameObject, pos);
            }
        }
        else
        {
            Debug.Log("No camera");
        }
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10 * windowDpi, 5 * windowDpi, 400 * windowDpi, 20 * windowDpi),
            "Use the keyboard buttons A/<- and D/-> to change lazers!");
        GUI.Label(new Rect(10 * windowDpi, 20 * windowDpi, 400 * windowDpi, 20 * windowDpi),
            "Use left mouse button for shooting!");
    }

    void Counter(int count)
    {
        Prefab += count;

        if (Prefab > Prefabs.Length - 1)
            Prefab = 0;
        else if (Prefab < 0)
            Prefab = Prefabs.Length - 1;
    }

    void RotateToMouseDirection(GameObject obj, Vector3 destination)
    {
        direction = destination - obj.transform.position;
        rotation = Quaternion.LookRotation(direction);
        obj.transform.localRotation = Quaternion.Lerp(obj.transform.rotation, rotation, 1);
    }
}
using UnityEngine;

public class PlayerCombat : MonoBehaviour
{
    private FormBase[] forms;
    [SerializeField]private FormBase currentForm;

    private PlayerInput playerInput;
    // Start is called once before the first execution of Update after the MonoBehaviour is created

    void Awake()
    {
        playerInput = GetComponent<PlayerInput>();
        playerInput.OnFormChange += FormChange;
    }
    void Start()
    {
        forms = GetComponentsInChildren<FormBase>();
        currentForm = forms[0];
    }

    // Update is called once per frame
    void Update()
    {
        if (playerInput.AttackPressed)
        {
            currentForm.Attack();
        }
    }

    void FormChange(int index)
    {
        currentForm = forms[index];
    }
}

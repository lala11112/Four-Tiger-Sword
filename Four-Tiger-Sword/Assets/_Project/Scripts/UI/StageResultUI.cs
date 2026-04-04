using UnityEngine;
using UnityEngine.UI;

public class StageResultUI : MonoBehaviour
{
    [Header("Result Panel")]
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private Text resultText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button menuButton;

    private void Start()
    {
        resultPanel.SetActive(false);
    }

    public void ShowResult(bool isClear)
    {
        resultPanel.SetActive(true);
        resultText.text = isClear ? "Stage Clear!" : "Game Over...";
    }
}
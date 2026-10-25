using UnityEngine;

namespace StudioBaekho
{
    /// <summary>A list of floor props to use together; Map Forge chooses their positions.</summary>
    [CreateAssetMenu(fileName = "Prop Set", menuName = "Baekho/Prop Set")]
    public sealed class BaekhoPropSet : ScriptableObject
    {
        public enum Usage { Equipment, Storage, Natural }
        public enum Arrangement { Either, Wall, Island }

        [Tooltip("이 묶음을 맵 제작기로 가져옵니다.")]
        public bool importEnabled = true;
        [Tooltip("묶음 이름입니다. 비워 두면 이 에셋의 이름을 사용합니다. 최대 80자.")]
        public string label = "";
        [Tooltip("Equipment: 작업·설비, Storage: 보관, Natural: 자연물")]
        public Usage usage = Usage.Equipment;
        [Tooltip("Either: 자동, Wall: 벽 주변, Island: 구역 안쪽")]
        public Arrangement arrangement = Arrangement.Either;
        [Tooltip("함께 쓸 바닥 소품 프리팹 목록입니다. 배치와 반복 횟수는 앱에서 정합니다.")]
        public GameObject[] prefabs = new GameObject[0];
    }
}

using UnityEngine;

/// <summary>
/// 서버에서 받은 S2C_Move 기반으로 다른 엔티티(다른 플레이어/몬스터)를 부드럽게 이동시킨다
/// TownManager가 SetDestination()을 호출한다
/// </summary>
public class EntityMover : MonoBehaviour
{
    public float moveSpeed = 5f;

    private Vector3 _destination;
    private bool _hasDestination;

    void Start()
    {
        _destination = transform.position;
    }

    void Update()
    {
        if (!_hasDestination) return;

        if (Vector3.Distance(transform.position, _destination) < 0.05f)
        {
            transform.position = _destination;
            _hasDestination = false;
            return;
        }

        var dir = (_destination - transform.position).normalized;
        transform.position = Vector3.MoveTowards(transform.position, _destination, moveSpeed * Time.deltaTime);
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), Time.deltaTime * 15f);
    }

    public void SetDestination(Vector3 destination)
    {
        _destination = destination;
        _hasDestination = true;
    }
}

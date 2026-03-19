namespace GameServer.Game.Components;

/// <summary>
/// 엔티티의 방어력 컴포넌트.
/// 받는 데미지 = max(1, 공격력 - Defense)
/// </summary>
public struct DefenseComponent
{
    public int Defense;

    public DefenseComponent(int defense)
    {
        Defense = defense;
    }
}

using UnityEngine;
namespace LineWars.Visuals {
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class TwoHandWeaponGrip : MonoBehaviour {
    public Transform supportHandTarget;
    [Range(0, 1)] public float weight = 1f;
    Animator animator;
    static readonly int Death = Animator.StringToHash("death0");
    void Awake() { animator = GetComponent<Animator>(); }
    void OnAnimatorIK(int layerIndex) {
        if (!animator) animator = GetComponent<Animator>();
        if (!supportHandTarget || !animator.isHuman) return;
        bool dying = animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Death;
        if (animator.IsInTransition(0)) dying |= animator.GetNextAnimatorStateInfo(0).shortNameHash == Death;
        float activeWeight = dying ? 0f : weight;
        animator.SetIKPositionWeight(AvatarIKGoal.LeftHand, activeWeight);
        animator.SetIKRotationWeight(AvatarIKGoal.LeftHand, activeWeight);
        animator.SetIKPosition(AvatarIKGoal.LeftHand, supportHandTarget.position);
        animator.SetIKRotation(AvatarIKGoal.LeftHand, supportHandTarget.rotation);
    }
}
}

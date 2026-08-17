// [Interflow fix 2026-06-27] Все подсказки [Tooltip] в этом файле локализованы на русский (правка ассета, разрешена Artsiom; только текст Tooltip). Оригинал EN: _BACKUP_TOOLTIPS/Scripts/Unit.cs. Реестр: wiki concepts/asset-fork-debt.
using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace StrategyCore
{
    // Unit.Visuals.cs — визуал (VFX/MATERIALS/ANIMATIONS/EFFECTORS/PROJECTORS/SOUND). Вырезано 1:1 из Unit.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Unit
    {
        // Хэши параметров аниматора: строки в горячем пути заменены на Animator.StringToHash (ревью 1.10)
        static readonly int animIdle = Animator.StringToHash("idle");
        static readonly int animWalk = Animator.StringToHash("walk");
        static readonly int animAttack = Animator.StringToHash("attack");
        static readonly int animBuilding = Animator.StringToHash("building");
        static readonly int animIdleReady = Animator.StringToHash("idleReady");
        static readonly int animCasting = Animator.StringToHash("casting");
        static readonly int animMoveSpeed = Animator.StringToHash("movespeed");

        // ============================= VFX ==============================================================================

        /// <summary>
        /// Adds VFX element to VFX Holder.
        /// </summary>
        /// <param name="vfx">VFX to add.</param>
        /// <param name="aboveHead">Should VFX be above head.</param>
        /// <param name="unitCentre">Should VFX be at the unit`s centre.</param>
        /// <returns></returns>
        public VFXReferencer AddVFX(VFXReferencer vfx, bool aboveHead = false, bool unitCentre = false)
        {
            VFXReferencer temp = Instantiate(vfx, vfxHolder);
            if (unitCentre) temp.transform.localPosition += new Vector3(0, unitHeight * 0.5f, 0);
            else if (aboveHead) temp.transform.localPosition += new Vector3(0, unitHeight + 0.1f, 0);

            return temp;
        }

        /// <summary>
        /// Removes VFX element from unit`s VFX Holder.
        /// </summary>
        /// <param name="vfx">VFX to remove.</param>
        public void RemoveVFX(VFXReferencer vfx)
        {
            VFXReferencer[] elements = vfxHolder.GetComponentsInChildren<VFXReferencer>();
            for (int i = 0; i < elements.Length; i++)
            {
                if (elements[i].id == vfx.id)
                {
                    Destroy(elements[i].gameObject);
                    return;
                }
            }
        }

        // ============================= MATERIALS AND RENDERERS ==============================================================================

        /// <summary>
        /// Sets unit`s player color in MaterialPropertyBlock
        /// </summary>
        void SetPlayerColor()
        {
            matBlock.SetColor("_PlayerColor", SlotManager.instance.playerColors[owner]);

            foreach (var renderer in meshRenderers)
            {
                renderer.SetPropertyBlock(matBlock);
            }
        }

        /// <summary>
        /// Sets the Overlay color. It is used for coloring the whole unit with particular color. For example when it is affected by certain ability during the cast.
        /// </summary>
        /// <param name="color">Overlay color.</param>
        /// <param name="oneFrame">Should the overlay color be set only for one frame.</param>
        public void SetOverlayColor(Color color, bool oneFrame = true)
        {
            matBlock.SetColor("_OverlayColor", color);

            foreach (var renderer in meshRenderers)
            {
                renderer.SetPropertyBlock(matBlock);
            }

            if (oneFrame) overlayColorOneFrame = true;
        }

        /// <summary>
        /// Resets overlay color.
        /// </summary>
        void ResetOverlayColor()
        {
            if (isInvisible) matBlock.SetColor("_OverlayColor", StateColors.Invisibility);
            else matBlock.SetColor("_OverlayColor", StateColors.Default);

            foreach (var renderer in meshRenderers)
            {
                renderer.SetPropertyBlock(matBlock);
            }
        }

        /// <summary>
        /// Overlay color resets itself after every frame.
        /// </summary>
        void HandleOverlayColor()
        {
            if (overlayColorOneFrame)
            {
                ResetOverlayColor();
                overlayColorOneFrame = false;
            }
        }

        /// <summary>
        /// Hides the unit by disabling its renderers. Creates static copy if necessary.
        /// </summary>
        public void HideRenderers()
        {
            if (!renderersOn) return;

            // Create static copy if unit is staticDestructible
            CreateStaticCopy();

            // Remove from selection
            Presentation.Selection?.RemoveFromSelection(this);

            for (int i = 0; i < renderers.Count; i++) renderers[i].gameObject.SetActive(false);
            for (int i = 0; i < meshRenderers.Count; i++)
            {
                meshRenderers[i].enabled = false;
            }

            renderersOn = false;
        }

        /// <summary>
        /// Shows the unit by enabling its renderers. Destroys static copy if necessary.
        /// </summary>
        public void ShowRenderers()
        {
            if (renderersOn) return;
            // If show renderers called while unit is not visible by the current team, ignore it
            if (!IsVisible(SlotManager.instance.currentTeam)) return;

            DestroyStaticCopy();

            for (int i = 0; i < renderers.Count; i++) renderers[i].gameObject.SetActive(true);
            for (int i = 0; i < meshRenderers.Count; i++) meshRenderers[i].enabled = true;

            if (animator)
            {
                if (isBeingBuilt)
                {
                    AnimatorSetBool(AnimationState.Idle, false);
                    if (animator && constructionUnit.constructionAnimExists) animator.Play("construction", 0, constructionUnit.currentConstructionPercentage);
                }
                else AnimatorSetBool(currentAnimatorBoolState, true);
                animator.SetFloat("blendingIndex", animationBlendingIndex);
            }

            renderersOn = true;
        }

        /// <summary>
        /// Copies the visuals from another unit. Call idle before replacing! Make sure that new unit`s radius and colliders are same sized! Only height is copied as part of visuals.
        /// </summary>
        /// <param name="refUnit">Unit to copy the visuals from.</param>
        /// <param name="replacePermanently">Should the original visuals be replaced completely.</param>
        public void ReplaceRenderers(Unit refUnit, bool replacePermanently)
        {
            // Instantiate new unit
            Unit newUnit = Instantiate(refUnit, transform.position, Quaternion.identity);
            newUnit.transform.rotation = transform.rotation; // Copy this unit`s rotation
                                                             // Copy main renderer
            GameObject newMainRenderer = newUnit.transform.GetChild(0).gameObject;
            newMainRenderer.transform.parent = transform;
            newMainRenderer.transform.localPosition = new Vector3(0, 0, 0);
            newMainRenderer.transform.SetSiblingIndex(1);
            // Destroy created new unit
            DestroyImmediate(newUnit.gameObject);

            // Renderer set
            if (replacePermanently)
            {
                DestroyImmediate(mainRenderer);
                currentMainShape = refUnit;
            }
            else
            {
                if (mainRenderer.transform.GetSiblingIndex() == 0)
                {
                    // Currently in original form
                    mainRenderer.SetActive(false);
                }
                else
                {
                    // Currently polymorphed
                    DestroyImmediate(mainRenderer);
                }
            }

            mainRenderer = newMainRenderer;

            // ====== Save Unit Data ======
            pmVerticalPart = verticalPart;
            pmHorizontallPart = horizontalPart;
            pmHorizontalForward = horizontalPartForward;
            pmLaunchSite = launchSite;

            // ====== Copy Unit Data ======
            // Height
            unitHeight = newUnit.unitHeight;
            // VerticalPart
            verticalPart = newUnit.verticalPart;
            // HorizontalPart
            float yRot = (horizontalPart != transform) ? GetUnitRotation() : 0; // Get current Y rotation with old horizontal part
            horizontalPart = newUnit.horizontalPart;
            horizontalPartSet = false;
            if (yRot != 0) SetUnitRotation(yRot); // If equals to 0, we already set rotation when newUnit was instantiated 

            // Sound
            readySound = newUnit.readySound;
            moveSound = newUnit.moveSound;
            clickSound = newUnit.clickSound;
            deathSound = newUnit.deathSound;
            attackCommandSound = newUnit.attackCommandSound;
            attackStartSound = newUnit.attackStartSound;
            attackEndSound = newUnit.attackEndSound;
            weaponSound = newUnit.weaponSound;
            // Animation
            animationMoveSpeed = newUnit.animationMoveSpeed;
            animationAttackDelay = newUnit.animationAttackDelay;
            // DieVFX
            dieVFX = newUnit.dieVFX;
            // Launch
            launchSite = newUnit.launchSite;
            launchVFX = newUnit.launchVFX;
            // Projectile
            if (newUnit.projectileGO != null && ((attackType == AttackType.Continuous && newUnit.attackType == AttackType.Continuous) || (attackType != AttackType.Continuous && newUnit.attackType != AttackType.Continuous)))
            {
                // We copy projectile from reference only if attack types match and reference projectile exists
                projectileGO = newUnit.projectileGO;
            }

            // Renderers and Colors
            CalculateVisuals();
            SetPlayerColor();

            // Set animation state
            if (isBeingBuilt)
            {
                AnimatorSetBool(AnimationState.Idle, false);
                if (animator && constructionUnit.constructionAnimExists) animator.Play("construction", 0, constructionUnit.currentConstructionPercentage);
            }
            else AnimatorSetBool(currentAnimatorBoolState, true);
            if (animator) animator.SetFloat("blendingIndex", animationBlendingIndex);

            // We must set active false only after the Animator.HasState had been run
            if (!FoWVisible) for (int i = 0; i < meshRenderers.Count; i++) meshRenderers[i].enabled = false;
        }

        /// <summary>
        /// Restores the visual representation of the unit to its original.
        /// </summary>
        public void RestoreRenderers()
        {
            // For horizontal part
            float yRot = (horizontalPart != transform) ? GetUnitRotation() : 0; // Get current Y rotation with old horizontal part

            // Replace
            GameObject ogRenderer = transform.GetChild(0).gameObject;
            if (ogRenderer != mainRenderer) DestroyImmediate(mainRenderer);
            mainRenderer = ogRenderer;
            mainRenderer.SetActive(true);

            // ====== Copy Unit Data ======
            var ogUnit = GameManager.instance.gameUnits[unitTypeID];
            // Height
            unitHeight = ogUnit.unitHeight;
            // VerticalPart
            verticalPart = pmVerticalPart;
            // HorizontalPart
            horizontalPart = pmHorizontallPart;
            horizontalPartForward = pmHorizontalForward;
            horizontalPartSet = true;
            if (yRot != 0) SetUnitRotation(yRot); // If equals to 0, we already set rotation when newUnit was instantiated 

            // Sound
            readySound = ogUnit.readySound;
            moveSound = ogUnit.moveSound;
            clickSound = ogUnit.clickSound;
            deathSound = ogUnit.deathSound;
            attackCommandSound = ogUnit.attackCommandSound;
            attackStartSound = ogUnit.attackStartSound;
            attackEndSound = ogUnit.attackEndSound;
            weaponSound = ogUnit.weaponSound;
            // Animation
            animationMoveSpeed = ogUnit.animationMoveSpeed;
            animationAttackDelay = ogUnit.animationAttackDelay;
            // DieVFX
            dieVFX = ogUnit.dieVFX;
            // Launch
            launchSite = pmLaunchSite;
            launchVFX = ogUnit.launchVFX;
            // Projectile
            if (ogUnit.projectileGO != null && ((attackType == AttackType.Continuous && ogUnit.attackType == AttackType.Continuous) || (attackType != AttackType.Continuous && ogUnit.attackType != AttackType.Continuous)))
            {
                // We copy projectile from reference only if attack types match and reference projectile exists
                projectileGO = ogUnit.projectileGO;
            }

            // Renderers and Colors
            CalculateVisuals();
            SetPlayerColor();

            // Set animation state
            if (isBeingBuilt)
            {
                AnimatorSetBool(AnimationState.Idle, false);
                if (animator && constructionUnit.constructionAnimExists) animator.Play("construction", 0, constructionUnit.currentConstructionPercentage);
            }
            else AnimatorSetBool(currentAnimatorBoolState, true);
            if (animator) animator.SetFloat("blendingIndex", animationBlendingIndex);

            // We must set active false only after the Animator.HasState had been run
            if (!FoWVisible) for (int i = 0; i < meshRenderers.Count; i++) meshRenderers[i].enabled = false;
        }

        // ============================= ANIMATIONS ==============================================================================

        /// <summary>
        /// Changes the attack animation speed depending on the current attack speed.
        /// </summary>
        /// <param name="periodic">Are changing the animation speed based on periodic attack speed.</param>
        public void ChangeAttackAnimationSpeed(bool periodic = false)
        {
            if (animator && attackAnimationsCount != 0)
            {
                if (periodic)
                {
                    if (periodicAttackDelay < attackAnimationLength)
                    {
                        float animSpeed = attackAnimationLength / periodicAttackDelay;
                        animator.SetFloat("attackspeed", animSpeed);
                        currentAttackAnimLength = periodicAttackDelay; // attackAnimationLength * animSpeed;
                    }
                    else
                    {
                        animator.SetFloat("attackspeed", 1);
                        currentAttackAnimLength = attackAnimationLength;
                    }
                }
                else
                {
                    if (attackSpeed < attackAnimationLength)
                    {
                        float animSpeed = attackAnimationLength / attackSpeed;
                        animator.SetFloat("attackspeed", animSpeed);
                        currentAttackAnimLength = attackSpeed; // attackAnimationLength * animSpeed;
                    }
                    else
                    {
                        animator.SetFloat("attackspeed", 1);
                        currentAttackAnimLength = attackAnimationLength;
                    }
                }

                if (animationAttackDelay > 1) animationAttackDelay = 1;
                currentAnimAttackDelay = currentAttackAnimLength * animationAttackDelay - crossFadeTime;
                if (currentAnimAttackDelay < 0) currentAnimAttackDelay = 0;
            }
        }

        /// <summary>
        /// Changes the movement animation speed depending on the current movespeed.
        /// </summary>
        public void ChangeMoveSpeedAnimationSpeed()
        {
            if (animator && animationMoveSpeed != 0)
            {
                animator.SetFloat("movespeed", moveSpeed / animationMoveSpeed);
            }
        }

        /// <summary>
        /// Randomly plays different idle animation if they exist.
        /// </summary>
        public void RandomIdleAnimation()
        {
            if (unitState != UnitStates.Idle) return;
            if (constructionUnit && constructionUnit.isWorking) return;

            // We use currentActionTime to save memory instead of creating a new variable
            if (FoWVisible && (!NetworkConnectionHandler.isClient || (!activeAbilityInUse && activeAbilityCastTime == 0))) // For clients we should also check the isWorking
            {
                if (!stunned && !isMoving && target == null && !isBeingBuilt)
                {
                    currentActionTime += GameManager.instance.currentDeltaTime;

                    if (currentActionTime > idleRandomTime && !dead)
                    {
                        currentActionTime = 0;
                        animator.CrossFade("idle" + UnityEngine.Random.Range(0, idleAnimationCount), crossFadeTime, 0, 0f);
                    }
                }
            }
        }

        /// <summary>
        /// Changes the bool parameter for the loop animations of the animator.
        /// </summary>
        /// <param name="state">Animation state to change.</param>
        /// <param name="boolState">Bool parameters of the animation state.</param>
        /// <param name="setIdle">Should the idle state be set to the opposite of the boolState.</param>
        public void AnimatorSetBool(AnimationState state, bool boolState, bool setIdle = true)
        {
            if (animator)
            {
                if (state == AnimationState.Reset)
                {
                    // Reset
                    animator.SetBool(animIdle, true);
                    animator.SetBool(animWalk, false);
                    animator.SetBool(animAttack, false);
                    animator.SetBool(animBuilding, false);
                    animator.SetBool(animIdleReady, false);
                    animator.SetBool(animCasting, false);

                    currentAnimatorBoolState = AnimationState.Idle;
                }
                else if (state == AnimationState.Idle)
                {
                    // Idle
                    animator.SetBool(animIdle, boolState);
                    currentAnimatorBoolState = state;
                }
                else
                {
                    if (state == AnimationState.IdleReady)
                    {
                        // Idle Ready
                        animator.SetBool(animIdleReady, boolState);
                    }
                    else if (state == AnimationState.Walk)
                    {
                        // Walk
                        animator.SetBool(animWalk, boolState);
                    }
                    else if (state == AnimationState.Casting)
                    {
                        // Casting
                        animator.SetBool(animCasting, boolState);
                    }
                    else if (state == AnimationState.ContinuousAttack)
                    {
                        // Continuous attack
                        animator.SetBool(animAttack, boolState);
                    }
                    else if (state == AnimationState.Building)
                    {
                        // Building
                        animator.SetBool(animBuilding, boolState);
                    }

                    // Idle
                    if (setIdle)
                    {
                        if (boolState)
                        {
                            animator.SetBool(animIdle, false);
                            currentAnimatorBoolState = state;
                        }
                        else
                        {
                            animator.SetBool(animIdle, true);
                            currentAnimatorBoolState = AnimationState.Idle;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Sets the animation blending index.
        /// </summary>
        /// <param name="blendingIndex">Animation blend index.</param>
        public void SetAnimationBlendingIndex(float blendingIndex, bool sync = true)
        {
            if (animator) animator.SetFloat("blendingIndex", blendingIndex);
            animationBlendingIndex = blendingIndex;

            // Sync with clients
            if (sync && NetworkManager.Singleton.IsServer) NetworkDataSync.instance.AnimationPrefixClientRpc(netID, animationBlendingIndex);
        }

        // ============================= EFFECTORS ==============================================================================

        /// <summary>
        /// Called every GameManager.Tick to update the state of effectors.
        /// </summary>
        private void HandleEffectors()
        {
            // Reverse iteration because list might be modified
            for (int i = effectors.Count - 1; i >= 0; i--)
            {
                Effector.EffectorUpdate(effectors[i], this);
            }
        }

        // ============================= PROJECTORS ==============================================================================

        /// <summary>
        /// Enables the selection circle, if does not exist creates one
        /// </summary>
        public void ProjectorsEnable()
        {
            if (selectionCircle)
            {
                // Activate
                selectionCircle.gameObject.SetActive(true);
            }
            else
            {
                // Create
                SpriteRenderer sr = Instantiate(ReferenceManager.instance.selectionRenderer, transform);
                selectionCircle = sr.transform;
                // Sprite
                if (unitRadius > Utils.largeSelectorSize) sr.sprite = ReferenceManager.instance.selectionLarge;
                else if (unitRadius > Utils.mediumSelectorSize) sr.sprite = ReferenceManager.instance.selectionMedium;
                else sr.sprite = ReferenceManager.instance.selectionSmall;
                // Color
                if (SlotManager.instance.currentTeam != team && team != (int)Teams.NeutralPassive) sr.color = ReferenceManager.instance.selectionEnemyColor;
                else sr.color = ReferenceManager.instance.selectionAllyColor;
                // Scale
                selectionCircle.localScale = new Vector3(unitRadius / selectionCircle.lossyScale.x, unitRadius / selectionCircle.lossyScale.y, 1);
                // Position
                selectionCircle.localPosition += new Vector3(0, 0.1f, 0);
            }
        }

        /// <summary>
        /// Disables the selection circle
        /// </summary>
        public void ProjectorsDisable()
        {
            if (selectionCircle) selectionCircle.gameObject.SetActive(false);
        }

        // ============================= SOUND ==============================================================================

        /// <summary>
        /// Every game tick rate updates the timer
        /// </summary>
        public void CommandSoundTimerUpdate()
        {
            commandSoundTime -= GameManager.instance.currentDeltaTime;

            if (commandSoundTime <= 0)
            {
                // Command sound is finished
                CommandSoundDestroy();
            }
        }

        private void CommandSoundDestroy()
        {
            if (commandSound) Destroy(commandSound.gameObject);
            commandSoundTime = 0;
            GameManager.instance.Tick -= CommandSoundTimerUpdate;
        }

    }
}

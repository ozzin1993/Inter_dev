using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    // VFX Line should be attached to LineRenderer or Particle VFX.
    // It allows to control the parameters such as distance to properly reach the target.
    // DO NOT: Change the hierarchy of base LineRenderer and Particle VFX. This script is hard coded to find appropriate components, see Start().

    // Implement? Should not trigger effect on the same target twice

    public class VFXLine : MonoBehaviour
    {
        [VFXLineID]
        public int id;

        bool isParticle = true;

        private int launchSiteLength;
        private List<ParticleSystem> particles;
        private List<LineRenderer> lineRenderers;
        private List<Transform> lineRendererEnds;

        public static float particleDistanceAdjuster = 0.44f; // 0.435 is hard coded depending on the speed/life of the particleVFX

        // FoW visibility - we show or hide this vfx element if either of both units are visible to current team
        private Unit thisUnit;
        private bool currentlyVisible = true;

        // Start is called before the first frame update
        void Awake()
        {
            if (GetComponent<ParticleSystem>() == null)
            {
                // Line Renderer System
                isParticle = false;
            }
        }

        // Creates appropriate VFX elements depending on the size of launch positions and target count

        // Used by unit to initiate its attack line vfx
        // VFXLine - which vfx to initiate
        // Unit - this unit`s parameters will be used to decide how many vfx lines to create
        public static VFXLine CreateVFX(VFXLine vfxLine, Unit unit)
        {
            // Origins
            if (unit.launchSite.Length > 0)
            {
                vfxLine = Instantiate(vfxLine, unit.launchSite[0]);
            }
            else
            {
                vfxLine = Instantiate(vfxLine, unit.mainRenderer.transform);
                vfxLine.transform.position += new Vector3(0, unit.unitHeight * 0.5f, 0);
            }

            vfxLine.thisUnit = unit;

            // Target count
            int targetCount = 1; // main target
            if (unit.multiTarget) targetCount += unit.multiTargetCount;
            else if (unit.bounceCount != 0) targetCount += unit.bounceCount;

            // If launch sites exist
            if (unit.launchSite.Length > 0)
            {
                vfxLine.UpdateVFX(targetCount, null, unit.launchSite);
            }
            else
            {
                vfxLine.UpdateVFX(targetCount);
            }

            vfxLine.Deactivate();

            return vfxLine;
        }

        // Instantiates VFX at given origins with given target count
        // Use SetTarget/SetTargets to update the target VFX positions
        public static VFXLine CreateVFX(VFXLine vfxLine, Vector3[] origins, int targetCount)
        {
            if (origins == null || origins.Length == 0) return null;

            vfxLine = Instantiate(vfxLine, origins[0], Quaternion.identity);
            vfxLine.UpdateVFX(targetCount, origins, null);
            vfxLine.Deactivate();

            return vfxLine;
        }

        // Instantiates VFX as a child of given transforms with given target count
        // Use SetTarget/SetTargets to update the target VFX positions
        public static VFXLine CreateVFX(VFXLine vfxLine, Transform[] transformOrigins, int targetCount)
        {
            if (transformOrigins == null || transformOrigins.Length == 0) return null;

            vfxLine = Instantiate(vfxLine, transformOrigins[0]);
            vfxLine.UpdateVFX(targetCount, null, transformOrigins);
            vfxLine.Deactivate();

            return vfxLine;
        }

        // Instantiates VFX as a child of given transforms with given target count
        // Use SetTarget/SetTargets to update the target VFX positions
        public static VFXLine CreateVFX(VFXLine vfxLine, Unit unitOrigin, int targetCount)
        {
            vfxLine = Instantiate(vfxLine, unitOrigin.transform);
            vfxLine.transform.position += new Vector3(0, unitOrigin.unitHeight * 0.5f, 0);
            vfxLine.UpdateVFX(targetCount, null, null);
            vfxLine.Deactivate();

            return vfxLine;
        }

        // Updates VFX element count
        // When MultiTarget or Bounce count changes on the unit MUST CALL THIS METHOD
        public void UpdateVFX(int targetCount, Vector3[] origins = null, Transform[] transformOrigins = null)
        {
            int vfxCount = targetCount;
            launchSiteLength = 1;

            // Decide how many additional vfxCount we should add
            bool isTransformOrigin = (transformOrigins == null) ? false : true;
            if (origins != null || transformOrigins != null)
            {
                // For each launch position (origin) we create 1 vfx element
                launchSiteLength = (isTransformOrigin) ? transformOrigins.Length : origins.Length;
                if (launchSiteLength == 0) launchSiteLength = 1;
                vfxCount += launchSiteLength - 1;
            }

            isParticle = (GetComponent<ParticleSystem>()) ? true : false; 
            if (isParticle)
            {
                if (particles == null) particles = new List<ParticleSystem>(vfxCount);

                // If already existed, we adjust the count
                if (particles.Count == vfxCount) return;
                else if (vfxCount < particles.Count)
                {
                    // Remove unused
                    for (int i = 0; i < particles.Count - vfxCount; i++)
                    {
                        Destroy(particles[particles.Count - 1]);
                    }
                    return;
                }

                // Create vfx elements
                ParticleSystem particle = GetComponent<ParticleSystem>();

                if (particles.Count == 0)
                {
                    particles.Add(particle);
                }

                int l = particles.Count % launchSiteLength;
                for (int i = particles.Count; i < vfxCount; i++)
                {
                    // Add as a child of first VFX element if launch site is singular
                    if (launchSiteLength == 1)
                    {
                        particles.Add(Instantiate(particle, particles[0].transform.parent));
                    }
                    else
                    {
                        // Multiple launch site, add VFX as a child of these sites
                        if (isTransformOrigin) particles.Add(Instantiate(particle, transformOrigins[l]));
                        else particles.Add(Instantiate(particle, origins[l], Quaternion.identity));
                    }
                    particles[i].Stop();

                    // Launch site index
                    l++;
                    if (l == launchSiteLength) l = 0;
                }
            }
            else
            {
                // Line renderer
                if (lineRenderers == null)
                {
                    lineRenderers = new List<LineRenderer>(vfxCount);
                    lineRendererEnds = new List<Transform>(vfxCount);
                }

                // If already existed, we adjust the count
                if (lineRenderers.Count == vfxCount) return;
                else if (vfxCount < lineRenderers.Count)
                {
                    // Remove unused
                    for (int i = 0; i < lineRenderers.Count - vfxCount; i++)
                    {
                        Destroy(lineRenderers[lineRenderers.Count - 1]);
                        Destroy(lineRendererEnds[lineRendererEnds.Count - 1]);
                    }
                    return;
                }

                // Create vfx elements
                LineRenderer lineRenderer = transform.GetChild(0).GetComponent<LineRenderer>();
                Transform lineRendererEnd = transform.GetChild(2);

                if (lineRenderers.Count == 0)
                {
                    lineRenderers.Add(lineRenderer);
                    lineRendererEnds.Add(lineRendererEnd);
                }

                int l = lineRenderers.Count % launchSiteLength;
                for (int i = lineRenderers.Count; i < vfxCount; i++)
                {
                    // Add as a child of first VFX element if launch site is singular
                    if (launchSiteLength == 1)
                    {
                        lineRenderers.Add(Instantiate(lineRenderer, lineRenderers[0].transform.parent));
                        lineRendererEnds.Add(Instantiate(lineRendererEnd, lineRendererEnds[0].transform.parent));
                    }
                    else
                    {
                        // Multiple launch site, add VFX as a child of these sites
                        if (isTransformOrigin)
                        {
                            lineRenderers.Add(Instantiate(lineRenderer, transformOrigins[l]));
                            lineRendererEnds.Add(Instantiate(lineRendererEnd, transformOrigins[l]));
                        }
                        else
                        {
                            lineRenderers.Add(Instantiate(lineRenderer, origins[l], Quaternion.identity));
                            lineRendererEnds.Add(Instantiate(lineRendererEnd, origins[l], Quaternion.identity));
                        }
                    }
                    lineRenderers[i].gameObject.SetActive(false);
                    lineRendererEnds[i].gameObject.SetActive(false);

                    // Launch site index
                    l++;
                    if (l == launchSiteLength) l = 0;
                }
            }
        }

        // Accepts single Unit
        public void SetTarget(Unit target)
        {
            // FoW Visibilty
            if (thisUnit && thisUnit.FoWVisible) Show(); // Origin unit is visible
            else if (thisUnit == null && FogOfWar.instance.IsVisible(transform.position, SlotManager.instance.currentTeam)) Show(); // Origin position is visible
            else if (target.FoWVisible) Show(); // Target is visible
            else
            {
                Deactivate();
                return;
            }

            SetTargetInternal(target.transform.position, target.unitHeight);
        }

        // Accepts single position
        public void SetTarget(Vector3 targetPosition)
        {
            // FoW Visibilty
            if (thisUnit && thisUnit.FoWVisible) Show(); // Origin unit is visible
            else if (thisUnit == null && FogOfWar.instance.IsVisible(transform.position, SlotManager.instance.currentTeam)) Show(); // Origin position is visible
            else if (FogOfWar.instance.IsVisible(targetPosition, SlotManager.instance.currentTeam)) Show(); // Target position is visible
            else
            {
                Deactivate();
                return;
            }

            SetTargetInternal(targetPosition, 0);
        }

        // Array targets
        public void SetTarget(Unit[] target, bool bounce)
        {
            // FoW Visibilty
            // If origin unit not visible (if specified) or origin position is not visible, and target not visible we turn off VFX
            if (((thisUnit && !thisUnit.FoWVisible) || (thisUnit == null && !FogOfWar.instance.IsVisible(transform.position, SlotManager.instance.currentTeam))) && !target[0].FoWVisible)
            {
                Deactivate();
                return;
            }
            else
            {
                SetTargetInternal(target, bounce);
            }
        }

        // List targets
        public void SetTarget(List<Unit> target, bool bounce)
        {
            // FoW Visibilty
            // If origin unit not visible (if specified) or origin position is not visible, and target not visible we turn off VFX
            if (((thisUnit && !thisUnit.FoWVisible) || (thisUnit == null && !FogOfWar.instance.IsVisible(transform.position, SlotManager.instance.currentTeam))) && !target[0].FoWVisible)
            {
                Deactivate();
                return;
            }
            else
            {
                SetTargetInternal(target, bounce);
            }
        }

        // Single unit
        private void SetTargetInternal(Vector3 targetPosition, float height)
        {
            // VFX Params
            if (isParticle)
            {
                // Optimal choice of making particle follow the target visually synchronously seems to be chaning its scale parameter. You are free to experiment. 
                float val = Vector3.Distance(transform.position, targetPosition + new Vector3(0, height * 0.5f)) * particleDistanceAdjuster; // 0.435 is hard coded depending on the speed/life of the particleVFX
                transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                transform.LookAt(targetPosition + new Vector3(0, height * 0.5f));
            }
            else
            {
                // Line renderer
                lineRendererEnds[0].position = targetPosition + new Vector3(0, height * 0.5f, 0);
                lineRenderers[0].SetPosition(1, lineRenderers[0].transform.InverseTransformPoint(lineRendererEnds[0].position));
            }
        }

        // Accepts array of Unit
        private void SetTargetInternal(Unit[] target, bool bounce)
        {
            currentlyVisible = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (isParticle)
            {
                if (bounce)
                {
                    // BOUNCE

                    // All vfx at launch sites should look at first target
                    for (int i = 0; i < launchSiteLength; i++)
                    {
                        // Optimal choice of making particle follow the target visually synchronously seems to be chaning its scale parameter. You are free to experiment.
                        float val = Vector3.Distance(particles[i].transform.position, target[0].transform.position + new Vector3(0, target[0].unitHeight * 0.5f)) * particleDistanceAdjuster;
                        particles[i].transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                        particles[i].transform.LookAt(target[0].transform.position + new Vector3(0, target[0].unitHeight * 0.5f));

                        // Activate
                        if (!particles[i].isPlaying)
                        {
                            particles[i].Play();
                        }
                    }

                    // Rest of vfx elements are for bouncing from one unit to another
                    int l = launchSiteLength - 1;
                    for (int i = 1; i < target.Length; i++)
                    {
                        float val = Vector3.Distance(particles[l + i].transform.position, target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f)) * particleDistanceAdjuster;
                        particles[l + i].transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                        particles[l + i].transform.LookAt(target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f));
                        particles[l + i].transform.position = target[i - 1].transform.position + new Vector3(0, target[i - 1].unitHeight * 0.5f);

                        // Activate
                        if (!particles[l + i].isPlaying)
                        {
                            particles[l + i].Play();
                        }
                    }

                    // Remaining vfx must be disabled
                    for (int i = l + target.Length; i < particles.Count; i++)
                    {
                        if (particles[i].isPlaying)
                        {
                            particles[i].Stop();
                        }
                    }
                }
                else
                {
                    // MULTITARGET   
                    for (int i = 0; i < target.Length; i++)
                    {
                        // Disable if no target
                        if (target[i] == null)
                        {
                            if (particles[i].isPlaying)
                            {
                                particles[i].Stop();
                            }
                            continue;
                        }

                        // Target exists, set parameters
                        float val = Vector3.Distance(particles[i].transform.position, target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f)) * particleDistanceAdjuster;
                        particles[i].transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                        particles[i].transform.LookAt(target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f));

                        // Enable
                        if (!particles[i].isPlaying)
                        {
                            particles[i].Play();
                        }
                    }

                    // Disable rest of VFX elements
                    for (int i = (target.Length > launchSiteLength) ? target.Length : launchSiteLength; i < particles.Count; i++)
                    {
                        if (particles[i].isPlaying)
                        {
                            particles[i].Stop();
                        }
                    }
                }
            }
            else
            {
                // Line renderer

                if (bounce)
                {
                    // BOUNCE
                    // All vfx at launch sites should look at first target
                    for (int i = 0; i < launchSiteLength; i++)
                    {
                        lineRendererEnds[i].position = target[0].transform.position + new Vector3(0, target[0].unitHeight * 0.5f, 0);
                        lineRenderers[i].SetPosition(1, lineRenderers[i].transform.InverseTransformPoint(lineRendererEnds[i].position));

                        // Activate
                        if (!lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(true);
                            lineRendererEnds[i].gameObject.SetActive(true);
                        }
                    }

                    // Rest of vfx elements are for bouncing from one unit to another
                    int l = launchSiteLength - 1;
                    for (int i = 1; i < target.Length; i++)
                    {
                        lineRendererEnds[l + i].position = target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f, 0);
                        lineRenderers[l + i].SetPosition(1, lineRenderers[l + i].transform.InverseTransformPoint(lineRendererEnds[l + i].position));
                        lineRenderers[l + i].SetPosition(0, lineRenderers[l + i].transform.InverseTransformPoint(lineRendererEnds[l + i - 1].position));

                        // Activate
                        if (!lineRenderers[l + i].gameObject.activeSelf)
                        {
                            lineRenderers[l + i].gameObject.SetActive(true);
                            lineRendererEnds[l + i].gameObject.SetActive(true);
                        }
                    }

                    // Remaining vfx must be disabled
                    for (int i = l + target.Length; i < lineRenderers.Count; i++)
                    {
                        if (lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(false);
                            lineRendererEnds[i].gameObject.SetActive(false);
                        }
                    }
                }
                else
                {
                    // MULTITARGET
                    for (int i = 0; i < target.Length; i++)
                    {
                        // Disable if no target
                        if (target[i] == null)
                        {
                            if (lineRenderers[i].gameObject.activeSelf)
                            {
                                lineRenderers[i].gameObject.SetActive(false);
                                lineRendererEnds[i].gameObject.SetActive(false);
                            }
                            continue;
                        }

                        // Target exists, set parameters
                        lineRendererEnds[i].position = target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f, 0);
                        lineRenderers[i].SetPosition(1, lineRenderers[i].transform.InverseTransformPoint(lineRendererEnds[i].position));

                        // Enable
                        if (!lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(true);
                            lineRendererEnds[i].gameObject.SetActive(true);
                        }
                    }

                    // Disable rest of VFX elements
                    for (int i = (target.Length > launchSiteLength) ? target.Length : launchSiteLength; i < lineRenderers.Count; i++)
                    {
                        if (lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(false);
                            lineRendererEnds[i].gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        // 1 : 1 copy of SetTarget(Unit[] target) above
        // Change is only target.count for list, instead of target.length for array
        // To avoid list to array conversion
        public void SetTargetInternal(List<Unit> target, bool bounce)
        {
            // FoW Visibilty
            if (!thisUnit.FoWVisible && !target[0].FoWVisible)
            {
                Deactivate();
                return;
            }
            else currentlyVisible = true;
            if (!gameObject.activeSelf) gameObject.SetActive(true);

            if (isParticle)
            {
                if (bounce)
                {
                    // BOUNCE
                    // All vfx at launch sites should look at first target
                    for (int i = 0; i < launchSiteLength; i++)
                    {
                        // Optimal choice of making particle follow the target visually synchronously seems to be chaning its scale parameter. You are free to experiment.
                        float val = Vector3.Distance(particles[i].transform.position, target[0].transform.position + new Vector3(0, target[0].unitHeight * 0.5f)) * particleDistanceAdjuster;
                        particles[i].transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                        particles[i].transform.LookAt(target[0].transform.position + new Vector3(0, target[0].unitHeight * 0.5f));

                        // Activate
                        if (!particles[i].isPlaying)
                        {
                            particles[i].Play();
                        }
                    }

                    // Rest of vfx elements are for bouncing from one unit to another
                    int l = launchSiteLength - 1;
                    for (int i = 1; i < target.Count; i++)
                    {
                        float val = Vector3.Distance(particles[l + i].transform.position, target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f)) * particleDistanceAdjuster;
                        particles[l + i].transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                        particles[l + i].transform.LookAt(target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f));
                        particles[l + i].transform.position = target[i - 1].transform.position + new Vector3(0, target[i - 1].unitHeight * 0.5f);

                        // Activate
                        if (!particles[l + i].isPlaying)
                        {
                            particles[l + i].Play();
                        }
                    }

                    // Remaining vfx must be disabled
                    for (int i = l + target.Count; i < particles.Count; i++)
                    {
                        if (particles[i].isPlaying)
                        {
                            particles[i].Stop();
                        }
                    }
                }
                else
                {
                    // MULTITARGET
                    for (int i = 0; i < target.Count; i++)
                    {
                        float val = Vector3.Distance(particles[i].transform.position, target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f)) * particleDistanceAdjuster;
                        particles[i].transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
                        particles[i].transform.LookAt(target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f));

                        // Enable
                        if (!particles[i].isPlaying)
                        {
                            particles[i].Play();
                        }
                    }

                    // Disable rest of VFX elements
                    for (int i = (target.Count > launchSiteLength) ? target.Count : launchSiteLength; i < particles.Count; i++)
                    {
                        if (particles[i].isPlaying)
                        {
                            particles[i].Stop();
                        }
                    }
                }
            }
            else
            {
                // Line renderer
                if (bounce)
                {
                    // BOUNCE

                    // All vfx at launch sites should look at first target
                    for (int i = 0; i < launchSiteLength; i++)
                    {
                        lineRendererEnds[i].position = target[0].transform.position + new Vector3(0, target[0].unitHeight * 0.5f, 0);
                        lineRenderers[i].SetPosition(1, lineRenderers[i].transform.InverseTransformPoint(lineRendererEnds[i].position));

                        // Activate
                        if (!lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(true);
                            lineRendererEnds[i].gameObject.SetActive(true);
                        }
                    }

                    // Rest of vfx elements are for bouncing from one unit to another
                    int l = launchSiteLength - 1;
                    for (int i = 1; i < target.Count; i++)
                    {
                        lineRendererEnds[l + i].position = target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f, 0);
                        lineRenderers[l + i].SetPosition(1, lineRenderers[l + i].transform.InverseTransformPoint(lineRendererEnds[l + i].position));
                        lineRenderers[l + i].SetPosition(0, lineRenderers[l + i].transform.InverseTransformPoint(lineRendererEnds[l + i - 1].position));

                        // Activate
                        if (!lineRenderers[l + i].gameObject.activeSelf)
                        {
                            lineRenderers[l + i].gameObject.SetActive(true);
                            lineRendererEnds[l + i].gameObject.SetActive(true);
                        }
                    }

                    // Remaining vfx must be disabled
                    for (int i = l + target.Count; i < lineRenderers.Count; i++)
                    {
                        if (lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(false);
                            lineRendererEnds[i].gameObject.SetActive(false);
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < target.Count; i++)
                    {
                        lineRendererEnds[i].position = target[i].transform.position + new Vector3(0, target[i].unitHeight * 0.5f, 0);
                        lineRenderers[i].SetPosition(1, lineRenderers[i].transform.InverseTransformPoint(lineRendererEnds[i].position));

                        // Enable
                        if (!lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(true);
                            lineRendererEnds[i].gameObject.SetActive(true);
                        }
                    }

                    // Disable rest of VFX elements
                    for (int i = (target.Count > launchSiteLength) ? target.Count : launchSiteLength; i < lineRenderers.Count; i++)
                    {
                        if (lineRenderers[i].gameObject.activeSelf)
                        {
                            lineRenderers[i].gameObject.SetActive(false);
                            lineRendererEnds[i].gameObject.SetActive(false);
                        }
                    }
                }
            }
        }

        public void SetDistance(float value)
        {
            if (isParticle)
            {
                // Optimal choice of making particle follow the target visually synchronously seems to be chaning its scale parameter. You are free to experiment. 
                float val = value * particleDistanceAdjuster; // 0.435 is hard coded depending on the speed/life of the particleVFX
                transform.localScale = new Vector3(Mathf.Min(val * 0.7f, 3f), Mathf.Min(val * 0.7f, 3f), val);
            }
        }

        public void LookAt(Vector3 position)
        {
            transform.LookAt(position);
        }

        public void Deactivate()
        {
            if (!currentlyVisible) return;
            currentlyVisible = false;

            if (isParticle)
            {
                for (int i = 0; i < particles.Count; i++)
                {
                    if (particles[i].gameObject.activeSelf)
                    {
                        particles[i].Stop();
                    }
                }
            }
            else
            {
                for (int i = 0; i < lineRenderers.Count; i++)
                {
                    if (lineRenderers[i].gameObject.activeSelf)
                    {
                        lineRenderers[i].gameObject.SetActive(false);
                        lineRendererEnds[i].gameObject.SetActive(false);
                    }
                }

                gameObject.SetActive(false);
            }
        }

        // Enables the visibility 
        private void Show()
        {
            if (currentlyVisible) return;
            currentlyVisible = true;

            if (isParticle)
            {
                particles[0].Play();
            }
            else
            {
                lineRenderers[0].gameObject.SetActive(true);
                lineRendererEnds[0].gameObject.SetActive(true);
                gameObject.SetActive(true);
            }
        }
    }
}

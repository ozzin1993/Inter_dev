using UnityEngine;
// URP
using UnityEngine.Rendering.Universal;

namespace StrategyCore
{
    public class ProjectorHelper
    {
        public static void SetSize(Transform projector, float radius)
        {
            // BRP
            // Projector proj = projector.GetComponent<Projector>();
            // if (proj != null)
            // {
            //     proj.nearClipPlane = -radius;
            //     proj.farClipPlane = radius;
            //     proj.orthographicSize = radius;
            // }

            // ==================== URP
            DecalProjector urpProjector = projector.GetComponent<DecalProjector>();
            radius = radius * 2;
            urpProjector.size = new Vector3(radius, radius, radius);
        }

        // FoW Projector initializer
        public static void InitializeFoWProjector(Transform projectorObj, Material mainFowMat, Material edgeFowMat)
        {
            // BRP
            // Projector projector = projectorObj.GetComponent<Projector>();
            // 
            // projector.nearClipPlane = Grid.Instance.height / -2;
            // projector.farClipPlane = Grid.Instance.height / 2;
            // projector.orthographicSize = Grid.Instance.width / 2;
            // projector.material = mainFowMat;
            // projector.enabled = true;

            // ==================== URP
            DecalProjector decalProjector = projectorObj.GetComponent<DecalProjector>();
            
            decalProjector.size = new Vector3(Grid.Instance.width, Grid.Instance.height, 25f);
            decalProjector.pivot = new Vector3(0, 0, 0);
            
            projectorObj.gameObject.SetActive(true);
            
            // Edge projectors
            float edgeWidth = GameManager.Instance.cameraEdge * 3f;
            
            for (int i = 0; i < 4; i++)
            {
                DecalProjector edgeProjector = projectorObj.gameObject.AddComponent<DecalProjector>();
                edgeProjector.material = edgeFowMat;
                edgeProjector.drawDistance = decalProjector.drawDistance;
                edgeProjector.fadeFactor = decalProjector.fadeFactor;
                edgeProjector.renderingLayerMask = decalProjector.renderingLayerMask;
            
                if (i == 0)
                {
                    // Left
                    edgeProjector.size = new Vector3(edgeWidth, Grid.Instance.height + edgeWidth * 2f, 25f);
                    edgeProjector.pivot = new Vector3((-Grid.Instance.width - edgeWidth) * 0.5f + 0.0f, 0, 0);
                }
                else if (i == 1)
                {
                    // Right
                    edgeProjector.size = new Vector3(edgeWidth, Grid.Instance.height + edgeWidth * 2f, 25f);
                    edgeProjector.pivot = new Vector3((Grid.Instance.width + edgeWidth) * 0.5f - 0.0f, 0, 0);
                }
                else if (i == 2)
                {
                    // Top
                    edgeProjector.size = new Vector3(Grid.Instance.width, edgeWidth, 25f);
                    edgeProjector.pivot = new Vector3(0, (Grid.Instance.height + edgeWidth) * 0.5f - 0.0f, 0);
                }
                else
                {
                    // Bottom
                    edgeProjector.size = new Vector3(Grid.Instance.width, edgeWidth, 25f);
                    edgeProjector.pivot = new Vector3(0, (-Grid.Instance.height - edgeWidth) * 0.5f + 0.0f, 0);
                }
            }
        }
    }
}

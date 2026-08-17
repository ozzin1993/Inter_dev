using UnityEngine;
using StrategyCore;

namespace Camera_TopDownNS
{
    public class BuildingPlacer : MonoBehaviour
    {
        [SerializeField] private Camera_TopDown Camera_TopDown;
        [SerializeField] private Vector2 gridSize; // Snap to grid, if 0 no snapping

        private int draggableLayerMask = 1 << 3; // Dragable objects layer mask

        private bool placementMode = false;
        private bool dragMode = true;
        private Transform objectToPlace;
        private Vector3 objectOffset;

        [SerializeField] private Material activeMaterial;
        private Material initialMaterial;

        private void Awake()
        {

        }

        // Start is called before the first frame update
        void Start()
        {
        }

        // Update is called once per frame
        void Update()
        {
            HandlePlacementMode();
        }
        void OnEnable()
        {
            //subscribe to event
            Camera_TopDown.onDoubleClick += DblClick;
            Camera_TopDown.onClick += Click;
            Camera_TopDown.onLongClick += LongClick;
        }

        void OnDisable()
        {
            //Un-subscribe to event
            Camera_TopDown.onDoubleClick -= DblClick;
            Camera_TopDown.onClick -= Click;
            Camera_TopDown.onLongClick -= LongClick;
        }

        void Click(Vector2 position)
        {
            // Placement mode off if cursor is not over active object
            if (placementMode)
            {
                ExitPlacementMode(position);
            }
        }

        void DblClick(Vector2 position)
        {

        }

        void LongClick(Vector2 position)
        {
            // Placement mode off if cursor is not over active object
            if (placementMode && !dragMode)
            {
                ExitPlacementMode(position);
            }

            // Enter placement mode if mode is off and cursor over placeable object
            if (!placementMode)
            {
                if ((Physics.Raycast(Utils.MainCamera.ScreenPointToRay(position), out RaycastHit raycastHit, 300, draggableLayerMask)))
                {
                    // Enter placement mode
                    placementMode = true;
                    objectToPlace = raycastHit.transform;
                    Vector3 cursorPoint = Camera_TopDown.PlaneRayCast(Camera_TopDown.GetCursorPosition());
                    objectOffset = objectToPlace.position - cursorPoint;
                    initialMaterial = objectToPlace.GetComponent<Renderer>().material;
                    objectToPlace.GetComponent<Renderer>().material = activeMaterial;
                    dragMode = true;
                    Camera_TopDown.HideCursor();
                    Camera_TopDown.TouchMove_OFF();
                }
            }
        }

        private void HandlePlacementMode()
        {
            if (placementMode)
            {
                // Start dragging if cursor over active object
                if (Camera_TopDown.ClickButtonDown())
                {
                    if ((Physics.Raycast(Utils.MainCamera.ScreenPointToRay(Camera_TopDown.GetCursorPosition()), out RaycastHit raycastHit, 300, draggableLayerMask)))
                    {
                        if (raycastHit.transform.gameObject != objectToPlace.gameObject)
                        {
                            dragMode = false;
                            Camera_TopDown.ShowCursor();
                            Camera_TopDown.TouchMove_ON();
                        }
                        else
                        {
                            dragMode = true;
                            Camera_TopDown.HideCursor();
                            Camera_TopDown.TouchMove_OFF();
                            Vector3 cursorPoint = Camera_TopDown.PlaneRayCast(Camera_TopDown.GetCursorPosition());
                            objectOffset = objectToPlace.position - cursorPoint;
                        }
                    }
                }

                // Position active object
                if (dragMode && Camera_TopDown.ClickButtonPressed())
                {
                    Vector3 newPosition = Camera_TopDown.PlaneRayCast(Camera_TopDown.GetCursorPosition());
                    newPosition += objectOffset;
                    objectToPlace.position = Camera_TopDown.SnapToGrid(gridSize, newPosition);
                }

                // Place active object
                if (dragMode && Camera_TopDown.ClickButtonUp())
                {
                    dragMode = false;
                    Camera_TopDown.ShowCursor();
                    Camera_TopDown.TouchMove_ON();
                }
            }
        }

        private void ExitPlacementMode(Vector2 position)
        {
            // Exit placement mode if cursor is not over active object
            if ((Physics.Raycast(Utils.MainCamera.ScreenPointToRay(position), out RaycastHit raycastHit, 300, draggableLayerMask)))
            {
                if (raycastHit.transform != objectToPlace)
                {
                    // Exit placement mode
                    placementMode = false;
                    objectToPlace.GetComponent<Renderer>().material = initialMaterial;
                }
            }
            else
            {
                // Exit placement mode
                placementMode = false;
                objectToPlace.GetComponent<Renderer>().material = initialMaterial;
            }
        }
    }
}

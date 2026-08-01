using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.EnhancedTouch;
using ETouch = UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using StrategyCore;

namespace Camera_TopDownNS
{
    // Camera_TopDown.Input.cs — обработчики ввода (клава/геймпад/тач/мышь). Вырезано 1:1 из Camera_TopDown.cs (разрезка на partial-ы 2026-08-01, задача №11).
    public partial class Camera_TopDown
    {
        void HandleGeneralInput()
        {
            // Slow Mode
            if (cameraControls.GeneralMap.SlowMode.IsPressed())
            {
                movementSpeed = slowSpeed;
            }
            else
            {
                movementSpeed = 1;
            }

            // Active input Gamepad Or Mouse Or Touch
            if (ETouch.Touch.activeTouches.Count == 0)
            {
                if (currentInputDevice == 0 && Mouse.current != null)
                {
                    // Mouse
                    cursorPosition = Mouse.current.position.ReadValue();
                    //Cursor.lockState = CursorLockMode.Confined;
                    Cursor.lockState = CursorLockMode.None;
                    isOverUI = IsOverUI(cursorPosition);
                }
                else if (currentInputDevice == 1 && Gamepad.current != null)
                {
                    // Gamepad
                    cursorPosition = virtualCursorTransform.position;
                    isOverUI = IsOverUI(cursorPosition);
                }
                else
                {
                    // Touch
                    currentInputDevice = 2;
                    isOverUI = false;
                }
            }
            else
            {
                // Touch
                currentInputDevice = 2;
                cursorPosition = ETouch.Touch.activeTouches[0].screenPosition;
                isOverUI = IsOverUI(cursorPosition);
            }

            // Reset Camera
            if (cameraControls.GeneralMap.ResetCamera.WasPerformedThisFrame())
            {
                newZoom = resetZoom;
                newRotation = resetRotation;
                followTransform = null;
            }

            // Rotation Buttons
            float rotationValue = cameraControls.GeneralMap.RotationButtons.ReadValue<float>();
            if (rotationValue != 0 && !isOverUI)
            {
                newRotation += (currentUpVector * -(rotationValue * (movementSpeed * buttonRotationSensitivitiy)) * multiplier2D) * Time.deltaTime; // Rotation
            }

            // Tilt Buttons
            float tiltValue = cameraControls.GeneralMap.TiltButtons.ReadValue<float>();
            if (tiltValue != 0 && !isOverUI)
            {
                newRotation += (new Vector3(1, 0, 0) * (tiltValue * (movementSpeed * buttonRotationSensitivitiy))) * Time.deltaTime; // Tilt
            }

            // Zoom Buttons
            float zoomValue = cameraControls.GeneralMap.ZoomButtons.ReadValue<float>();
            if (zoomValue != 0 && !isOverUI)
            {
                newZoom += (zoomValue * zoomAmount * (movementSpeed * buttonZoomSensitivitiy)) * Time.deltaTime;
            }

            // Move camera - WASD, Arrows, GamePad LeftStick
            Vector2 moveVector = cameraControls.GeneralMap.MoveVector.ReadValue<Vector2>();

            // Edge move
            if (currentInputDevice == 0 || currentInputDevice == 1)
            {
                if (cursorPosition.x > Screen.width - edgePadding) { moveVector.x = 1f * edgeMoveSensitivity; }
                else if (cursorPosition.x < edgePadding) { moveVector.x = -1f * edgeMoveSensitivity; }
                if (cursorPosition.y > Screen.height - edgePadding) { moveVector.y = 1f * edgeMoveSensitivity; }
                else if (cursorPosition.y < edgePadding) { moveVector.y = -1f * edgeMoveSensitivity; }
            }

            if (cameraType == CameraType._2D)
            {
                transformForward = Quaternion.AngleAxis(transform.rotation.eulerAngles.z, currentUpVector) * Vector3.up;
                transformRight = Quaternion.AngleAxis(-90, currentUpVector) * transformForward;
            }
            else
            {
                transformForward = Quaternion.AngleAxis(transform.rotation.eulerAngles.y, currentUpVector) * Vector3.forward;
                transformRight = Quaternion.AngleAxis(90, currentUpVector) * transformForward;
            }

            newPosition += ((transformForward * moveVector.y * movementSpeed * moveSensitivitiy) + (transformRight * moveVector.x * movementSpeed * moveSensitivitiy)) * Time.deltaTime;
        }

        void HandleGamepadInput()
        {
            if (Gamepad.current != null)
            {
                // Virtual Cursor - Right stick

                Vector2 rightStick = cameraControls.GeneralMap.RightStick.ReadValue<Vector2>();

                Vector2 delta = rightStick * movementSpeed * virtualMouseSensitivity * Time.deltaTime;

                Vector3 newPosition = virtualCursorTransform.position + (new Vector3(rightStick.x * movementSpeed * virtualMouseSensitivity, rightStick.y * movementSpeed * virtualMouseSensitivity, 0) * Time.deltaTime);

                newPosition.x = Mathf.Clamp(newPosition.x, 0, Screen.width);
                newPosition.y = Mathf.Clamp(newPosition.y, 0, Screen.height);

                virtualCursorTransform.position = newPosition;

                InputState.Change(virtualMouse.position, new Vector2(newPosition.x, newPosition.y));
                InputState.Change(virtualMouse.delta, delta);

                // Virtual mouse scroll
                if (isOverUI)
                {
                    InputState.Change(virtualMouse.scroll, cameraControls.GeneralMap.Scroll.ReadValue<Vector2>() * 50f);
                }

                // Virtual mouse click
                virtualMouse.CopyState<MouseState>(out var mouseState);
                mouseState.WithButton(MouseButton.Left, cameraControls.GeneralMap.Click.IsPressed());
                InputState.Change(virtualMouse, mouseState);

                if (rightStick != Vector2.zero)
                {
                    currentInputDevice = 1;
                }

                // Unfocus UI element after click, it gets stuck when using gamepad
                if (cameraControls.GeneralMap.Click.WasReleasedThisFrame())
                {
                    EventSystem.current.SetSelectedGameObject(null);
                }
            }
        }

        void HandleTouchInput()
        {
            // Movement Touch
            if (ETouch.Touch.activeTouches.Count == 1 && !isOverUI) // Middle or touch
            {
                ETouch.Touch touch = ETouch.Touch.activeTouches[0];

                if (fromDoubleTouchToSingle)
                {
                    fromDoubleTouchToSingle = false;
                    dragStartPosition = PlaneRayCast(touch.screenPosition);
                }

                switch (touch.phase)
                {
                    // Touch started
                    case UnityEngine.InputSystem.TouchPhase.Began:
                        if (!turnOffTouchMove)
                        {
                            dragStartPosition = PlaneRayCast(touch.screenPosition);
                        }

                        break;

                    // Touch moved
                    case UnityEngine.InputSystem.TouchPhase.Moved:
                        if (!turnOffTouchMove)
                        {
                            dragCurrentPosition = PlaneRayCast(touch.screenPosition);
                            newPosition = transform.position + dragStartPosition - dragCurrentPosition;
                        }

                        break;

                    // Touch lifted
                    case UnityEngine.InputSystem.TouchPhase.Ended:

                        break;
                }
            }
            // Zoom and Rotation
            else if (ETouch.Touch.activeTouches.Count == 2)
            {
                ETouch.Touch touch = ETouch.Touch.activeTouches[0];
                ETouch.Touch touch2 = ETouch.Touch.activeTouches[1];

                // Zoom
                if (Vector2.Dot(touch.delta.normalized, touch2.delta.normalized) < -0.85f)
                {
                    // Get touch positions on plane
                    Vector2 prevPositionTouch = touch.screenPosition - touch.delta;
                    Vector2 prevPositionTouch2 = touch2.screenPosition - touch2.delta;

                    Vector3 currentTouch = PlaneRayCast(touch.screenPosition);
                    Vector3 prevTouch = PlaneRayCast(prevPositionTouch);

                    Vector3 currentTouch2 = PlaneRayCast(touch2.screenPosition);
                    Vector3 prevTouch2 = PlaneRayCast(prevPositionTouch2);

                    // Get distances
                    float initialDistance = Vector3.Distance(prevTouch, prevTouch2);
                    float currentDistance = Vector3.Distance(currentTouch, currentTouch2);

                    float deltaDistance = initialDistance - currentDistance;

                    // Do zoom
                    newZoom -= deltaDistance * zoomAmount * touchZoomSensitivity;
                    cursorPosition = Vector2.Lerp(touch.screenPosition, touch2.screenPosition, 0.5f);
                }
                // Rotation
                else if (Vector2.Dot(touch.delta.normalized, touch2.delta.normalized) > 0.85f)
                {
                    // Vertical or Horizontal
                    float angle = Vector2.Angle(touch.delta.normalized, new Vector2(0, 1));

                    // Horizontal - rotation
                    if (angle > 35 && angle < 145)
                    {
                        newRotation += currentUpVector * (touch.delta.x * (touchRotationSensitivity)) * multiplier2D;
                    }
                    // Vertical - tilt
                    else
                    {
                        newRotation += new Vector3(1, 0, 0) * -(touch.delta.y * (touchRotationSensitivity));
                    }
                }

                fromDoubleTouchToSingle = true;
            }
        }

        void HandleMouseInput()
        {
            if (currentInputDevice == 0 && !isOverUI)
            {
                // Movement Mouse
                if (Mouse.current.middleButton.wasPressedThisFrame) // Middle
                {
                    dragStartPosition = PlaneRayCast(Mouse.current.position.ReadValue());
                }

                if (Mouse.current.middleButton.IsPressed()) // Middle
                {
                    dragCurrentPosition = PlaneRayCast(Mouse.current.position.ReadValue());
                    newPosition = transform.position + dragStartPosition - dragCurrentPosition;
                }

                // Zoom
                if (Mouse.current.scroll.y.ReadValue() != 0) // Scroll
                {
                    newZoom += Mathf.Clamp(Mouse.current.scroll.y.ReadValue(), -1, 1) * zoomAmount * mouseZoomSensitivity;
                    cursorPosition = Mouse.current.position.ReadValue();
                }

                // Rotation
                if (Mouse.current.rightButton.wasPressedThisFrame) // Right
                {
                    rotateStartPosition = Mouse.current.position.ReadValue();
                }
                if (Keyboard.current.leftAltKey.IsPressed() && Mouse.current.rightButton.IsPressed()) // Right
                {
                    rotateCurrentPosition = Mouse.current.position.ReadValue();
                    Vector3 difference = rotateStartPosition - rotateCurrentPosition;
                    rotateStartPosition = rotateCurrentPosition;

                    newRotation += currentUpVector * -(difference.x * mouseRotationSensitivity) * multiplier2D; // Rotation
                    newRotation += new Vector3(1, 0, 0) * (difference.y * mouseRotationSensitivity); // Tilt
                }
            }
        }

        void CalculateInput()
        {
            // Cursor handling
            if (currentInputDevice == 0 && !cursorHidden)
            {
                Cursor.visible = true;
                virtualCursorTransform.gameObject.SetActive(false);
            }
            else if (currentInputDevice == 1 && !cursorHidden)
            {
                Cursor.visible = false;
                virtualCursorTransform.gameObject.SetActive(true);
            }
            else
            {
                Cursor.visible = false;
                virtualCursorTransform.gameObject.SetActive(false);
            }

            // Follow set position
            if (followTransform != null)
            {
                newPosition = followTransform.position;
                cursorPosition = new Vector2(Screen.width / 2, Screen.height / 2);
            }

            // Position
            newPosition = PositionClamp(newPosition);
            transform.position = Vector3.Lerp(
                transform.position,
                newPosition,
                Time.deltaTime * lerpTime
            );

            // Zoom, based on cursor position
            if (newZoom != cameraTransform.localPosition)
            {
                Vector3 cursorPoint = PlaneRayCast(cursorPosition);

                newZoom = ZoomClamp(newZoom);
                cameraTransform.localPosition = Vector3.Lerp(
                    cameraTransform.localPosition,
                    newZoom,
                    Time.deltaTime * lerpTime
                );

                Vector3 cursorPointNew = PlaneRayCast(cursorPosition);
                Vector3 direction = (cursorPointNew - cursorPoint).normalized;
                float distance = Vector3.Distance(cursorPoint, cursorPointNew);
                transform.position = PositionClamp(transform.position - direction * distance);
                newPosition -= direction * distance;
            }

            // Rotation
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                RotationClamp(ref newRotation),
                Time.deltaTime * lerpTime
            );
        }
    }
}

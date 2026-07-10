using UnityEngine;
using Camera_TopDownNS;

namespace StrategyCore
{
    /// <summary>
    /// Ограничивает движение камеры по осям: по отдельности можно зафиксировать X и/или Z
    /// на заданных в инспекторе значениях. Включи нужную ось — другая остаётся свободной.
    /// Камера ассета (Camera_TopDown) двигает свой transform по плоскости X/Z и лерпит
    /// позицию в Update. Здесь после её Update (в LateUpdate) выбранные координаты возвращаются
    /// к фиксированным; свободная ось и Y не трогаем. Рендер кадра идёт после LateUpdate,
    /// поэтому камера всегда отрисовывается на фиксированных значениях (без дёрганья).
    /// Camera_TopDown.cs не правится (правило 1), приватные поля камеры не трогаем (правило 9).
    /// Эффект чисто локальный/визуальный для камеры игрока — серверная логика не нужна (правило 6).
    /// Отключается стандартным флажком enabled компонента.
    /// </summary>
    [RequireComponent(typeof(Camera_TopDown))]
    public class CameraAxisLock : MonoBehaviour
    {
        [Header("Ось X")]
        [Tooltip("Фиксировать координату X. Если включено — камера не двигается по X.")]
        [SerializeField] private bool lockX;
        [Tooltip("Фиксированная мировая координата X (используется, если включён lockX). Задаётся в инспекторе.")]
        [SerializeField] private float fixedX;

        [Header("Ось Z")]
        [Tooltip("Фиксировать координату Z. Если включено — камера не двигается по Z.")]
        [SerializeField] private bool lockZ;
        [Tooltip("Фиксированная мировая координата Z (используется, если включён lockZ). Задаётся в инспекторе.")]
        [SerializeField] private float fixedZ;

        // У Camera_TopDown нет LateUpdate — наш LateUpdate гарантированно идёт после её Update.
        private void LateUpdate()
        {
            if (!lockX && !lockZ) return;

            Vector3 position = transform.position;
            if (lockX) position.x = fixedX;
            if (lockZ) position.z = fixedZ;
            transform.position = position;
        }
    }
}

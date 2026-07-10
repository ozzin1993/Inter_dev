using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StrategyCore
{
    public class VFXRotator : MonoBehaviour
    {
        [SerializeField] float rotationSpeed = 100f;

        // Update is called once per frame
        void Update()
        {
            transform.Rotate(0, rotationSpeed * Time.deltaTime, 0);
        }
    }
}

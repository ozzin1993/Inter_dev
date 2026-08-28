using System;
using UnityEngine;
using Unity.Netcode;

namespace StrategyCore
{
    public class ItemDropped : MonoBehaviour
    {
        // Just a reference to item scriptable object for a unit that acts like a dropped item, that other units can destroy or pickup
        public Ability item;
        public int charges; // If item has charges
        public float cooldown; // If item has cooldown

        private void Start()
        {
            // Initialize the dropped item
            Unit itemPrefab = GetComponent<Unit>();

            itemPrefab.unitName = item.abilityName[0];
            itemPrefab.description = item.description[0];
            // [Interflow 2026-08-01 client-data] После миграции на сервере иконок нет (пустой массив) — гейт вместо IndexOutOfRange; клиент регидрирует и получит иконку.
            itemPrefab.icon = (item.icon != null && item.icon.Length > 0) ? item.icon[0] : null;
        }

        // This method can be called to create a dropped item object at the location
        public static bool Spawn(Ability dropItem, int itemCharges, float itemcd, Vector3 location, float unitRadius = 0)
        {
            float itemRadius = ReferenceManager.Instance.itemPrefab.unitRadius;

            // Collision check
            if (unitRadius != 0)
            {
                // Unit radius is defined, it means we check for collisions around the unit and drop the item at empty place
                Vector3 positionOnCircle = Utils.CircleCheck(new Vector2(location.x, location.z), unitRadius, itemRadius, ReferenceManager.Instance.itemPrefab.isGround, ReferenceManager.Instance.itemPrefab.isWater, ReferenceManager.Instance.itemPrefab.isAir);

                if (positionOnCircle != Vector3.zero)
                {
                    // There is space available, check for slope
                    if (Utils.SlopeCheck(new Vector2(positionOnCircle.x, positionOnCircle.z), itemRadius))
                    {
                        SpawnInternal(dropItem, itemCharges, itemcd, positionOnCircle);
                        // Unit itemPrefab = Instantiate(ReferenceManager.Instance.itemPrefab, positionOnCircle, Quaternion.identity);
                        // 
                        // ItemDropped item = itemPrefab.GetComponent<ItemDropped>();
                        // item.item = dropItem;
                        // if (itemCharges > 0) item.charges = itemCharges;
                        // if (itemcd != 0) item.cooldown = itemcd;
 
                        return true;
                    }
                    else return false;
                }
                else return false;
            }
            else
            {
                if (Utils.SlopeCheck(new Vector2(location.x, location.z), itemRadius))
                {
                    RaycastHit hit;

                    int mask = Utils.groundMask;
                    if ((ReferenceManager.Instance.itemPrefab.isGround && ReferenceManager.Instance.itemPrefab.isWater) || ReferenceManager.Instance.itemPrefab.isAir) mask = Utils.terrainMask;
                    else if (ReferenceManager.Instance.itemPrefab.isWater) mask = Utils.waterMask;

                    if (Physics.SphereCast(new Vector3(location.x, Utils.raycastPointY, location.z), itemRadius, Vector3.down, out hit, Utils.raycastPointY * 5f, mask))
                    {
                        SpawnInternal(dropItem, itemCharges, itemcd, location);
                        // Unit itemPrefab = Instantiate(ReferenceManager.Instance.itemPrefab, location, Quaternion.identity);
                        // 
                        // ItemDropped item = itemPrefab.GetComponent<ItemDropped>();
                        // item.item = dropItem;
                        // if (itemCharges > 0) item.charges = itemCharges;
                        // if (itemcd != 0) item.cooldown = itemcd;

                        return true;
                    }
                    return false;
                }
                else return false;
            }
        }

        // Use spawn, this is for internal usage
        public static Unit SpawnInternal(Ability dropItem, int itemCharges, float itemcd, Vector3 location, UInt16 netID = 0)
        {
            Unit itemPrefab = Instantiate(ReferenceManager.Instance.itemPrefab, location, Quaternion.identity);
            itemPrefab.SetOwnership((int)Players.NeutralPassive);
            SlotManager.Instance.AssignNetID(itemPrefab, netID);

            ItemDropped item = itemPrefab.GetComponent<ItemDropped>();
            item.item = dropItem;
            if (itemCharges > 0) item.charges = itemCharges;
            if (itemcd != 0) item.cooldown = itemcd;

            // Network sync
            if (NetworkManager.Singleton.IsServer) NetworkDataSync.Instance.ItemDroppedSpawn(dropItem.id, itemCharges, itemcd, location, itemPrefab.netID);
            return itemPrefab;
        }
    }
}
